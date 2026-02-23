using System.Text.Json;
using System.Text.RegularExpressions;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;

namespace AgentsDashboard.ControlPlane.Services;

public sealed partial class McpRegistryCatalogService(
    IHttpClientFactory httpClientFactory,
    IOrchestratorStore store,
    ILogger<McpRegistryCatalogService> logger)
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(6);

    public async Task<IReadOnlyList<McpCatalogEntry>> GetCatalogAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        var cached = await store.ListMcpRegistryServersAsync(cancellationToken);
        var state = await store.GetMcpRegistryStateAsync(cancellationToken);

        var needsRefresh = forceRefresh
            || cached.Count == 0
            || state.LastRefreshedAtUtc <= DateTime.UtcNow.Subtract(RefreshInterval);

        if (!needsRefresh)
        {
            return cached.Select(MapToEntry).ToList();
        }

        return await RefreshCatalogAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<McpCatalogEntry>> RefreshCatalogAsync(CancellationToken cancellationToken)
    {
        List<McpCatalogEntry> merged;
        var refreshState = await store.GetMcpRegistryStateAsync(cancellationToken);
        try
        {
            var officialEntries = await FetchOfficialRegistryAsync(cancellationToken);
            var popularityRanks = await FetchMcpSoPopularityAsync(cancellationToken);
            merged = MergeAndRankEntries(officialEntries, popularityRanks);

            await store.ReplaceMcpRegistryServersAsync(merged.Select(MapToDocument).ToList(), cancellationToken);

            refreshState.LastRefreshedAtUtc = DateTime.UtcNow;
            refreshState.LastRefreshError = string.Empty;
            refreshState.LastServerCount = merged.Count;
            await store.UpsertMcpRegistryStateAsync(refreshState, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MCP registry refresh failed");
            refreshState.LastRefreshError = ex.Message;
            await store.UpsertMcpRegistryStateAsync(refreshState, cancellationToken);

            var fallback = await store.ListMcpRegistryServersAsync(cancellationToken);
            return fallback.Select(MapToEntry).ToList();
        }

        return merged;
    }

    private async Task<List<McpCatalogEntry>> FetchOfficialRegistryAsync(CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        var entries = new List<McpCatalogEntry>();
        string? cursor = null;

        while (!cancellationToken.IsCancellationRequested)
        {
            var endpoint = cursor is null
                ? "https://registry.modelcontextprotocol.io/v0/servers?limit=100"
                : $"https://registry.modelcontextprotocol.io/v0/servers?limit=100&cursor={Uri.EscapeDataString(cursor)}";

            using var response = await client.GetAsync(endpoint, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!json.RootElement.TryGetProperty("servers", out var serversNode) ||
                serversNode.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            foreach (var item in serversNode.EnumerateArray())
            {
                if (!item.TryGetProperty("server", out var serverNode) ||
                    serverNode.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                entries.AddRange(BuildOfficialEntries(serverNode));
            }

            cursor = GetString(json.RootElement, "metadata", "nextCursor");
            if (string.IsNullOrWhiteSpace(cursor))
            {
                break;
            }
        }

        return entries;
    }

    private static IEnumerable<McpCatalogEntry> BuildOfficialEntries(JsonElement serverNode)
    {
        var serverName = GetString(serverNode, "name");
        if (string.IsNullOrWhiteSpace(serverName))
        {
            yield break;
        }

        var description = GetString(serverNode, "description");
        var repositoryUrl = GetString(serverNode, "repository", "url");
        var displayName = BuildDisplayName(serverName);

        if (serverNode.TryGetProperty("packages", out var packagesNode) &&
            packagesNode.ValueKind == JsonValueKind.Array &&
            packagesNode.GetArrayLength() > 0)
        {
            foreach (var package in packagesNode.EnumerateArray())
            {
                var packageIdentifier = GetString(package, "identifier");
                var registryType = GetString(package, "registryType");
                var transportType = GetString(package, "transport", "type");
                var packageVersion = GetString(package, "version");
                var installCommand = BuildInstallCommand(registryType, packageIdentifier, packageVersion);
                var (command, args) = BuildLaunchCommand(registryType, packageIdentifier, packageVersion);

                yield return new McpCatalogEntry
                {
                    Id = BuildEntryId(serverName, registryType, packageIdentifier, transportType),
                    Source = "official",
                    ServerName = serverName,
                    DisplayName = displayName,
                    Description = description,
                    RepositoryUrl = repositoryUrl,
                    RegistryType = registryType,
                    PackageIdentifier = packageIdentifier,
                    TransportType = transportType,
                    Command = command,
                    CommandArgs = args,
                    EnvironmentVariables = BuildEnvironmentVariables(package),
                    InstallCommand = installCommand,
                };
            }

            yield break;
        }

        if (serverNode.TryGetProperty("remotes", out var remotesNode) &&
            remotesNode.ValueKind == JsonValueKind.Array &&
            remotesNode.GetArrayLength() > 0)
        {
            foreach (var remote in remotesNode.EnumerateArray())
            {
                var transportType = GetString(remote, "type");
                var url = GetString(remote, "url");

                yield return new McpCatalogEntry
                {
                    Id = BuildEntryId(serverName, "remote", url, transportType),
                    Source = "official",
                    ServerName = serverName,
                    DisplayName = displayName,
                    Description = description,
                    RepositoryUrl = repositoryUrl,
                    RegistryType = "remote",
                    PackageIdentifier = url,
                    TransportType = transportType,
                    EnvironmentVariables = BuildHeaderVariables(remote),
                };
            }

            yield break;
        }

        yield return new McpCatalogEntry
        {
            Id = BuildEntryId(serverName, "generic", string.Empty, string.Empty),
            Source = "official",
            ServerName = serverName,
            DisplayName = displayName,
            Description = description,
            RepositoryUrl = repositoryUrl,
            RegistryType = "generic",
            TransportType = "stdio",
        };
    }

    private async Task<Dictionary<string, int>> FetchMcpSoPopularityAsync(CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        using var response = await client.GetAsync("https://mcp.so/servers", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var matches = ServerLinkRegex().Matches(html);

        var ranks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var rank = 1;

        foreach (Match match in matches)
        {
            if (!match.Success || match.Groups.Count < 2)
            {
                continue;
            }

            var slug = match.Groups[1].Value;
            if (string.IsNullOrWhiteSpace(slug) || ranks.ContainsKey(slug))
            {
                continue;
            }

            ranks[slug] = rank;
            rank++;

            if (rank > 500)
            {
                break;
            }
        }

        return ranks;
    }

    private static List<McpCatalogEntry> MergeAndRankEntries(
        IReadOnlyList<McpCatalogEntry> officialEntries,
        IReadOnlyDictionary<string, int> popularityRanks)
    {
        var merged = new Dictionary<string, McpCatalogEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in officialEntries)
        {
            var slug = BuildSlug(entry.ServerName, entry.DisplayName);
            var score = 100d;

            if (popularityRanks.TryGetValue(slug, out var rank))
            {
                score += Math.Max(0, 1000 - rank);
            }

            if (!string.IsNullOrWhiteSpace(entry.InstallCommand))
            {
                score += 25;
            }

            if (!string.IsNullOrWhiteSpace(entry.Command))
            {
                score += 10;
            }

            if (!string.IsNullOrWhiteSpace(entry.RepositoryUrl))
            {
                score += 5;
            }

            var ranked = entry with { Score = score };
            var dedupeKey = $"{ranked.ServerName}|{ranked.RegistryType}|{ranked.PackageIdentifier}|{ranked.TransportType}";
            merged[dedupeKey] = ranked;
        }

        return merged.Values
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(1200)
            .ToList();
    }

    private static Dictionary<string, string> BuildEnvironmentVariables(JsonElement packageOrRemote)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!packageOrRemote.TryGetProperty("environmentVariables", out var environmentVariables) ||
            environmentVariables.ValueKind != JsonValueKind.Array)
        {
            return variables;
        }

        foreach (var variable in environmentVariables.EnumerateArray())
        {
            var name = GetString(variable, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            variables[name] = string.Empty;
        }

        return variables;
    }

    private static Dictionary<string, string> BuildHeaderVariables(JsonElement remote)
    {
        var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!remote.TryGetProperty("headers", out var headers) || headers.ValueKind != JsonValueKind.Array)
        {
            return variables;
        }

        foreach (var header in headers.EnumerateArray())
        {
            var name = GetString(header, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            variables[name] = GetString(header, "value");
        }

        return variables;
    }

    private static string BuildInstallCommand(string registryType, string identifier, string version)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return string.Empty;
        }

        if (string.Equals(registryType, "npm", StringComparison.OrdinalIgnoreCase))
        {
            var versionSuffix = string.IsNullOrWhiteSpace(version) ? string.Empty : $"@{version.Trim()}";
            return $"npm i -g {identifier}{versionSuffix}";
        }

        if (string.Equals(registryType, "oci", StringComparison.OrdinalIgnoreCase))
        {
            return $"docker pull {identifier}";
        }

        if (string.Equals(registryType, "pypi", StringComparison.OrdinalIgnoreCase))
        {
            return $"uv tool install {identifier}";
        }

        return string.Empty;
    }

    private static (string Command, IReadOnlyList<string> Args) BuildLaunchCommand(string registryType, string identifier, string version)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return (string.Empty, []);
        }

        if (string.Equals(registryType, "npm", StringComparison.OrdinalIgnoreCase))
        {
            var packageRef = string.IsNullOrWhiteSpace(version)
                ? identifier
                : $"{identifier}@{version.Trim()}";
            return ("npx", ["-y", packageRef]);
        }

        if (string.Equals(registryType, "oci", StringComparison.OrdinalIgnoreCase))
        {
            return ("docker", ["run", "--rm", "-i", identifier]);
        }

        if (string.Equals(registryType, "pypi", StringComparison.OrdinalIgnoreCase))
        {
            return ("uvx", [identifier]);
        }

        return (string.Empty, []);
    }

    private static string BuildDisplayName(string serverName)
    {
        if (string.IsNullOrWhiteSpace(serverName))
        {
            return string.Empty;
        }

        var candidate = serverName;
        var slashIndex = candidate.LastIndexOf('/');
        if (slashIndex >= 0 && slashIndex < candidate.Length - 1)
        {
            candidate = candidate[(slashIndex + 1)..];
        }

        var normalized = candidate.Replace('-', ' ').Replace('_', ' ').Trim();
        return string.IsNullOrWhiteSpace(normalized) ? serverName : normalized;
    }

    private static string BuildSlug(string serverName, string displayName)
    {
        var candidate = string.IsNullOrWhiteSpace(serverName) ? displayName : serverName;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
        }

        var slashIndex = candidate.LastIndexOf('/');
        if (slashIndex >= 0 && slashIndex < candidate.Length - 1)
        {
            candidate = candidate[(slashIndex + 1)..];
        }

        return candidate.Trim().ToLowerInvariant();
    }

    private static string BuildEntryId(string serverName, string registryType, string packageIdentifier, string transport)
    {
        return string.Join(':', [serverName, registryType, packageIdentifier, transport]);
    }

    private static string GetString(JsonElement element, params string[] path)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(segment, out var next))
            {
                return string.Empty;
            }

            current = next;
        }

        return current.ValueKind == JsonValueKind.String
            ? current.GetString() ?? string.Empty
            : string.Empty;
    }

    private static McpCatalogEntry MapToEntry(McpRegistryServerDocument document)
    {
        return new McpCatalogEntry
        {
            Id = document.Id,
            Source = document.Source,
            ServerName = document.ServerName,
            DisplayName = document.DisplayName,
            Description = document.Description,
            RepositoryUrl = document.RepositoryUrl,
            RegistryType = document.RegistryType,
            PackageIdentifier = document.PackageIdentifier,
            TransportType = document.TransportType,
            Command = document.Command,
            CommandArgs = document.CommandArgs,
            EnvironmentVariables = document.EnvironmentVariables,
            InstallCommand = document.InstallCommand,
            Score = document.Score,
        };
    }

    private static McpRegistryServerDocument MapToDocument(McpCatalogEntry entry)
    {
        return new McpRegistryServerDocument
        {
            Id = string.IsNullOrWhiteSpace(entry.Id) ? Guid.NewGuid().ToString("N") : entry.Id,
            Source = entry.Source,
            ServerName = entry.ServerName,
            DisplayName = entry.DisplayName,
            Description = entry.Description,
            RepositoryUrl = entry.RepositoryUrl,
            RegistryType = entry.RegistryType,
            PackageIdentifier = entry.PackageIdentifier,
            TransportType = entry.TransportType,
            Command = entry.Command,
            CommandArgs = entry.CommandArgs.ToList(),
            EnvironmentVariables = new Dictionary<string, string>(entry.EnvironmentVariables, StringComparer.OrdinalIgnoreCase),
            InstallCommand = entry.InstallCommand,
            Score = entry.Score,
            UpdatedAtUtc = DateTime.UtcNow,
        };
    }

    [GeneratedRegex("href=\"/server/([^\"/]+)/[^\"/]+\"", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ServerLinkRegex();
}
