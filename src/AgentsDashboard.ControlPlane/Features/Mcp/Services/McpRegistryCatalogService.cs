using System.Text.Json;
using System.Text.RegularExpressions;



namespace AgentsDashboard.ControlPlane.Features.Mcp.Services;

public sealed partial class McpRegistryCatalogService(
    IHttpClientFactory httpClientFactory,
    ISystemStore store,
    ILogger<McpRegistryCatalogService> logger)
{
    private const string SourceOfficialRegistry = "official-registry";
    private const string SourceMcpSo = "mcp.so";
    private const string SourceAwesome = "awesome-mcp-servers";
    private const int DefaultSearchLimit = 80;
    private const int MaximumSearchLimit = 500;
    private const int MaximumCatalogEntries = 2400;

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(6);

    public static IReadOnlyList<string> SupportedRegistrySources { get; } =
    [
        SourceOfficialRegistry,
        SourceMcpSo,
        SourceAwesome,
    ];

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

    public async Task<IReadOnlyList<McpCatalogEntry>> SearchCatalogAsync(
        string? query,
        IReadOnlyCollection<string>? sources,
        bool forceRefresh,
        int limit,
        CancellationToken cancellationToken)
    {
        var catalog = await GetCatalogAsync(forceRefresh, cancellationToken);
        var normalizedSources = NormalizeSourceFilter(sources);
        var terms = TokenizeQuery(query);
        var normalizedLimit = NormalizeLimit(limit);

        var ranked = new List<(McpCatalogEntry Entry, double Rank)>();
        foreach (var entry in catalog)
        {
            var sourceKey = NormalizeSourceKey(entry.Source);
            if (normalizedSources.Count > 0 && !normalizedSources.Contains(sourceKey))
            {
                continue;
            }

            var score = ComputeSearchScore(entry, terms);
            if (score < 0)
            {
                continue;
            }

            ranked.Add((entry with { Source = sourceKey }, score));
        }

        return ranked
            .OrderByDescending(x => x.Rank)
            .ThenByDescending(x => x.Entry.Score)
            .ThenBy(x => x.Entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(normalizedLimit)
            .Select(x => x.Entry)
            .ToList();
    }

    public async Task<IReadOnlyList<McpCatalogEntry>> RefreshCatalogAsync(CancellationToken cancellationToken)
    {
        var refreshState = await store.GetMcpRegistryStateAsync(cancellationToken);

        try
        {
            var officialEntriesTask = SafeFetchEntriesAsync(SourceOfficialRegistry, FetchOfficialRegistryAsync, cancellationToken);
            var mcpSoEntriesTask = SafeFetchEntriesAsync(SourceMcpSo, FetchMcpSoRegistryAsync, cancellationToken);
            var awesomeEntriesTask = SafeFetchEntriesAsync(SourceAwesome, FetchAwesomeRegistryAsync, cancellationToken);
            var popularityRanksTask = SafeFetchPopularityAsync(cancellationToken);

            await Task.WhenAll(officialEntriesTask, mcpSoEntriesTask, awesomeEntriesTask, popularityRanksTask);

            var entries = officialEntriesTask.Result
                .Concat(mcpSoEntriesTask.Result)
                .Concat(awesomeEntriesTask.Result)
                .ToList();

            var merged = MergeAndRankEntries(entries, popularityRanksTask.Result);

            await store.ReplaceMcpRegistryServersAsync(merged.Select(MapToDocument).ToList(), cancellationToken);

            refreshState.LastRefreshedAtUtc = DateTime.UtcNow;
            refreshState.LastRefreshError = string.Empty;
            refreshState.LastServerCount = merged.Count;
            refreshState.UpdatedAtUtc = DateTime.UtcNow;
            await store.UpsertMcpRegistryStateAsync(refreshState, cancellationToken);

            return merged;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MCP registry refresh failed");
            refreshState.LastRefreshError = ex.Message;
            refreshState.UpdatedAtUtc = DateTime.UtcNow;
            await store.UpsertMcpRegistryStateAsync(refreshState, cancellationToken);

            var fallback = await store.ListMcpRegistryServersAsync(cancellationToken);
            return fallback.Select(MapToEntry).ToList();
        }
    }

    private async Task<List<McpCatalogEntry>> SafeFetchEntriesAsync(
        string source,
        Func<CancellationToken, Task<List<McpCatalogEntry>>> loader,
        CancellationToken cancellationToken)
    {
        try
        {
            return await loader(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MCP registry source load failed for {Source}", source);
            return [];
        }
    }

    private async Task<Dictionary<string, int>> SafeFetchPopularityAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await FetchMcpSoPopularityAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "MCP popularity ranking fetch failed");
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
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
                    Source = SourceOfficialRegistry,
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
                    Source = SourceOfficialRegistry,
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
            Source = SourceOfficialRegistry,
            ServerName = serverName,
            DisplayName = displayName,
            Description = description,
            RepositoryUrl = repositoryUrl,
            RegistryType = "generic",
            TransportType = "stdio",
        };
    }

    private async Task<List<McpCatalogEntry>> FetchMcpSoRegistryAsync(CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        using var response = await client.GetAsync("https://mcp.so/servers", cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var matches = McpSoServerRegex().Matches(html);
        var entries = new List<McpCatalogEntry>();

        foreach (Match match in matches)
        {
            if (!match.Success)
            {
                continue;
            }

            var serverName = DecodeEscapedJsonString(match.Groups["name"].Value);
            if (string.IsNullOrWhiteSpace(serverName))
            {
                continue;
            }

            var displayName = DecodeEscapedJsonString(match.Groups["title"].Value);
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = BuildDisplayName(serverName);
            }

            var description = DecodeEscapedJsonString(match.Groups["description"].Value);
            var repositoryUrl = DecodeEscapedJsonString(match.Groups["url"].Value);
            var configJson = DecodeEscapedJsonString(match.Groups["config"].Value);
            var parsedConfig = ParseServerConfig(configJson);

            var command = parsedConfig.Command;
            var args = parsedConfig.Args;
            var env = parsedConfig.Environment;
            var url = parsedConfig.Url;
            var transport = parsedConfig.Transport;
            var identifier = parsedConfig.Identifier;

            if (string.IsNullOrWhiteSpace(identifier))
            {
                identifier = string.IsNullOrWhiteSpace(repositoryUrl) ? serverName : repositoryUrl;
            }

            var installCommand = BuildInstallCommandFromCommand(command, args);
            if (string.IsNullOrWhiteSpace(transport))
            {
                transport = string.IsNullOrWhiteSpace(url) ? "stdio" : "streamable-http";
            }

            entries.Add(new McpCatalogEntry
            {
                Id = BuildEntryId(serverName, "directory", identifier, transport),
                Source = SourceMcpSo,
                ServerName = serverName,
                DisplayName = displayName,
                Description = description,
                RepositoryUrl = repositoryUrl,
                RegistryType = string.IsNullOrWhiteSpace(url) ? "directory" : "remote",
                PackageIdentifier = identifier,
                TransportType = transport,
                Command = command,
                CommandArgs = args,
                EnvironmentVariables = env,
                InstallCommand = installCommand,
            });

            if (entries.Count >= 400)
            {
                break;
            }
        }

        return entries;
    }

    private async Task<List<McpCatalogEntry>> FetchAwesomeRegistryAsync(CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient();
        using var response = await client.GetAsync(
            "https://raw.githubusercontent.com/punkpeye/awesome-mcp-servers/main/README.md",
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return [];
        }

        var markdown = await response.Content.ReadAsStringAsync(cancellationToken);
        return ParseAwesomeEntries(markdown);
    }

    private static List<McpCatalogEntry> ParseAwesomeEntries(string markdown)
    {
        var lines = markdown.Split('\n');
        var entries = new List<McpCatalogEntry>();
        var insideServerSection = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.StartsWith("## Server Implementations", StringComparison.OrdinalIgnoreCase))
            {
                insideServerSection = true;
                continue;
            }

            if (!insideServerSection)
            {
                continue;
            }

            if (line.StartsWith("## Frameworks", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var match = AwesomeServerLineRegex().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var displayName = CleanInlineMarkdown(match.Groups["name"].Value);
            var repositoryUrl = match.Groups["url"].Value.Trim();
            var description = CleanInlineMarkdown(match.Groups["description"].Value);

            if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(repositoryUrl))
            {
                continue;
            }

            if (!repositoryUrl.Contains("github.com/", StringComparison.OrdinalIgnoreCase) &&
                !repositoryUrl.Contains("glama.ai/mcp/servers/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var serverName = GuessServerName(repositoryUrl, displayName);
            entries.Add(new McpCatalogEntry
            {
                Id = BuildEntryId(serverName, "curated", repositoryUrl, ""),
                Source = SourceAwesome,
                ServerName = serverName,
                DisplayName = displayName,
                Description = description,
                RepositoryUrl = repositoryUrl,
                RegistryType = "curated",
                PackageIdentifier = repositoryUrl,
                TransportType = "unknown",
            });

            if (entries.Count >= 900)
            {
                break;
            }
        }

        return entries;
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

            if (rank > 1200)
            {
                break;
            }
        }

        return ranks;
    }

    private static List<McpCatalogEntry> MergeAndRankEntries(
        IReadOnlyList<McpCatalogEntry> entries,
        IReadOnlyDictionary<string, int> popularityRanks)
    {
        var merged = new Dictionary<string, McpCatalogEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var normalizedSource = NormalizeSourceKey(entry.Source);
            var slug = BuildSlug(entry.ServerName, entry.DisplayName);
            var score = BaseScoreBySource(normalizedSource);

            if (popularityRanks.TryGetValue(slug, out var rank))
            {
                score += Math.Max(0, 1800 - rank);
            }

            if (!string.IsNullOrWhiteSpace(entry.InstallCommand))
            {
                score += 35;
            }

            if (!string.IsNullOrWhiteSpace(entry.Command))
            {
                score += 20;
            }

            if (!string.IsNullOrWhiteSpace(entry.RepositoryUrl))
            {
                score += 10;
            }

            if (!string.IsNullOrWhiteSpace(entry.Description))
            {
                score += 5;
            }

            if (entry.EnvironmentVariables.Count > 0)
            {
                score += 4;
            }

            var ranked = entry with { Source = normalizedSource, Score = score };
            var dedupeKey = BuildDedupeKey(ranked);

            if (!merged.TryGetValue(dedupeKey, out var existing) || ranked.Score > existing.Score)
            {
                merged[dedupeKey] = ranked;
            }
        }

        return merged.Values
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumCatalogEntries)
            .ToList();
    }

    private static double ComputeSearchScore(McpCatalogEntry entry, IReadOnlyList<string> terms)
    {
        if (terms.Count == 0)
        {
            return entry.Score;
        }

        var source = NormalizeSourceKey(entry.Source);
        var total = entry.Score + BaseScoreBySource(source);

        foreach (var term in terms)
        {
            var termScore = 0d;
            termScore += FieldScore(entry.DisplayName, term, 220, 130, 90);
            termScore += FieldScore(entry.ServerName, term, 180, 100, 70);
            termScore += FieldScore(entry.Description, term, 120, 70, 35);
            termScore += FieldScore(entry.PackageIdentifier, term, 160, 90, 60);
            termScore += FieldScore(entry.RepositoryUrl, term, 140, 70, 50);
            termScore += FieldScore(entry.Command, term, 130, 60, 40);
            termScore += FieldScore(source, term, 110, 60, 40);
            termScore += FieldScore(entry.RegistryType, term, 90, 50, 30);
            termScore += FieldScore(entry.TransportType, term, 85, 45, 25);

            if (termScore <= 0)
            {
                return -1;
            }

            total += termScore;
        }

        return total;
    }

    private static double FieldScore(string field, string term, double exactScore, double prefixScore, double containsScore)
    {
        if (string.IsNullOrWhiteSpace(field) || string.IsNullOrWhiteSpace(term))
        {
            return 0;
        }

        if (string.Equals(field, term, StringComparison.OrdinalIgnoreCase))
        {
            return exactScore;
        }

        if (field.StartsWith(term, StringComparison.OrdinalIgnoreCase))
        {
            return prefixScore;
        }

        if (field.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return containsScore;
        }

        return 0;
    }

    private static List<string> TokenizeQuery(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        return SearchTokenRegex()
            .Matches(query)
            .Select(match => match.Value.Trim().ToLowerInvariant())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();
    }

    private static int NormalizeLimit(int requestedLimit)
    {
        if (requestedLimit <= 0)
        {
            return DefaultSearchLimit;
        }

        return Math.Clamp(requestedLimit, 1, MaximumSearchLimit);
    }

    private static HashSet<string> NormalizeSourceFilter(IReadOnlyCollection<string>? sources)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (sources is null || sources.Count == 0)
        {
            return normalized;
        }

        foreach (var source in sources)
        {
            var sourceKey = NormalizeSourceKey(source);
            if (sourceKey.Length == 0)
            {
                continue;
            }

            normalized.Add(sourceKey);
        }

        return normalized;
    }

    private static string NormalizeSourceKey(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        var normalized = source.Trim().ToLowerInvariant();
        return normalized switch
        {
            "official" => SourceOfficialRegistry,
            "official-registry" => SourceOfficialRegistry,
            "mcp.so" => SourceMcpSo,
            "mcpso" => SourceMcpSo,
            "awesome" => SourceAwesome,
            "awesome-mcp-servers" => SourceAwesome,
            _ => normalized,
        };
    }

    private static double BaseScoreBySource(string source)
    {
        return source switch
        {
            SourceOfficialRegistry => 180,
            SourceMcpSo => 140,
            SourceAwesome => 95,
            _ => 80,
        };
    }

    private static string BuildDedupeKey(McpCatalogEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.RepositoryUrl))
        {
            return $"repo:{entry.RepositoryUrl.Trim().ToLowerInvariant()}";
        }

        if (!string.IsNullOrWhiteSpace(entry.PackageIdentifier))
        {
            return $"pkg:{entry.PackageIdentifier.Trim().ToLowerInvariant()}|{entry.TransportType.Trim().ToLowerInvariant()}";
        }

        var source = NormalizeSourceKey(entry.Source);
        return $"name:{entry.ServerName.Trim().ToLowerInvariant()}|{source}|{entry.RegistryType.Trim().ToLowerInvariant()}";
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

    private static string BuildInstallCommandFromCommand(string command, IReadOnlyList<string> args)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return string.Empty;
        }

        if (string.Equals(command, "npx", StringComparison.OrdinalIgnoreCase))
        {
            var packageRef = args.FirstOrDefault(arg => !arg.StartsWith("-", StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(packageRef))
            {
                return $"npm i -g {packageRef}";
            }
        }

        if (string.Equals(command, "uvx", StringComparison.OrdinalIgnoreCase))
        {
            var packageRef = args.FirstOrDefault(arg => !arg.StartsWith("-", StringComparison.Ordinal));
            if (!string.IsNullOrWhiteSpace(packageRef))
            {
                return $"uv tool install {packageRef}";
            }
        }

        return string.Empty;
    }

    private static (string Command, IReadOnlyList<string> Args, IReadOnlyDictionary<string, string> Environment, string Url, string Transport, string Identifier) ParseServerConfig(string configJson)
    {
        var command = string.Empty;
        var args = (IReadOnlyList<string>)[];
        var env = (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var url = string.Empty;
        var transport = string.Empty;
        var identifier = string.Empty;

        if (string.IsNullOrWhiteSpace(configJson))
        {
            return (command, args, env, url, transport, identifier);
        }

        try
        {
            using var json = JsonDocument.Parse(configJson);
            if (!json.RootElement.TryGetProperty("mcpServers", out var serversNode) ||
                serversNode.ValueKind != JsonValueKind.Object)
            {
                return (command, args, env, url, transport, identifier);
            }

            var firstServer = serversNode.EnumerateObject().FirstOrDefault();
            if (firstServer.Value.ValueKind != JsonValueKind.Object)
            {
                return (command, args, env, url, transport, identifier);
            }

            var serverObject = firstServer.Value;
            command = GetString(serverObject, "command");
            url = GetString(serverObject, "url");
            transport = GetString(serverObject, "transport");
            args = ParseStringArray(serverObject, "args");
            env = ParseEnvironmentMap(serverObject, "env");

            if (!string.IsNullOrWhiteSpace(url))
            {
                identifier = url;
            }
            else if (!string.IsNullOrWhiteSpace(command))
            {
                var firstArgument = args.FirstOrDefault(arg => !arg.StartsWith("-", StringComparison.Ordinal));
                identifier = string.IsNullOrWhiteSpace(firstArgument) ? command : firstArgument;
            }

            if (string.IsNullOrWhiteSpace(transport))
            {
                transport = string.IsNullOrWhiteSpace(url) ? "stdio" : "streamable-http";
            }

            return (command, args, env, url, transport, identifier);
        }
        catch (JsonException)
        {
            return (command, args, env, url, transport, identifier);
        }
    }

    private static IReadOnlyList<string> ParseStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var arrayNode) || arrayNode.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var values = new List<string>();
        foreach (var item in arrayNode.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var value = item.GetString()?.Trim() ?? string.Empty;
            if (value.Length == 0)
            {
                continue;
            }

            values.Add(value);
        }

        return values;
    }

    private static IReadOnlyDictionary<string, string> ParseEnvironmentMap(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var envNode) || envNode.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in envNode.EnumerateObject())
        {
            var key = property.Name?.Trim() ?? string.Empty;
            if (key.Length == 0)
            {
                continue;
            }

            env[key] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                JsonValueKind.Number => property.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => string.Empty,
            };
        }

        return env;
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

    private static string GuessServerName(string url, string fallback)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var segments = uri.AbsolutePath
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (segments.Length > 0)
            {
                return segments[^1];
            }
        }

        return fallback;
    }

    private static string CleanInlineMarkdown(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        normalized = MarkdownLinkRegex().Replace(normalized, "$1");
        normalized = normalized.Replace("`", string.Empty, StringComparison.Ordinal);
        normalized = normalized.Replace("*", string.Empty, StringComparison.Ordinal);
        normalized = normalized.Replace("_", " ", StringComparison.Ordinal);
        normalized = normalized.Replace("&amp;", "&", StringComparison.Ordinal);
        normalized = Regex.Replace(normalized, "\\s+", " ");
        return normalized.Trim();
    }

    private static string DecodeEscapedJsonString(string encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
        {
            return string.Empty;
        }

        try
        {
            var wrapped = $"\"{encoded}\"";
            return JsonSerializer.Deserialize<string>(wrapped) ?? string.Empty;
        }
        catch (JsonException)
        {
            return encoded;
        }
    }

    private static McpCatalogEntry MapToEntry(McpRegistryServerDocument document)
    {
        return new McpCatalogEntry
        {
            Id = document.Id,
            Source = NormalizeSourceKey(document.Source),
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
            Source = NormalizeSourceKey(entry.Source),
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

    [GeneratedRegex("\\{\"id\":\\d+,\"uuid\":\"[^\"]+\",\"name\":\"(?<name>(?:\\\\.|[^\"])*)\",\"title\":\"(?<title>(?:\\\\.|[^\"])*)\",\"description\":\"(?<description>(?:\\\\.|[^\"])*)\"[\\s\\S]*?\"url\":\"(?<url>(?:\\\\.|[^\"])*)\"[\\s\\S]*?\"server_config\":\"(?<config>(?:\\\\.|[^\"])*)\"", RegexOptions.Compiled)]
    private static partial Regex McpSoServerRegex();

    [GeneratedRegex("^\\s*[-*]\\s*.*?\\[(?<name>[^\\]]+)\\]\\((?<url>https?://[^)\\s]+)\\)\\s*(?:[-–—:]\\s*(?<description>.+))?$", RegexOptions.Compiled)]
    private static partial Regex AwesomeServerLineRegex();

    [GeneratedRegex("\\[(.*?)\\]\\((.*?)\\)", RegexOptions.Compiled)]
    private static partial Regex MarkdownLinkRegex();

    [GeneratedRegex("[A-Za-z0-9._:/@+-]+", RegexOptions.Compiled)]
    private static partial Regex SearchTokenRegex();
}
