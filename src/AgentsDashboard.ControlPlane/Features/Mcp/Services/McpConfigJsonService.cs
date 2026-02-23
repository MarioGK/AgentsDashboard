using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentsDashboard.ControlPlane.Features.Mcp.Services;

public sealed class McpConfigJsonService
{
    private static readonly JsonSerializerOptions IndentedJson = new()
    {
        WriteIndented = true
    };

    public McpConfigValidationResult ValidateAndFormat(string? rawJson)
    {
        var errors = new List<string>();
        var root = ParseRootObject(rawJson, errors);
        if (root is null)
        {
            return new McpConfigValidationResult
            {
                IsValid = false,
                Errors = errors,
                FormattedJson = NormalizeEmptyConfig(),
                Servers = []
            };
        }

        var serversNode = GetOrCreateServersNode(root, errors);
        if (serversNode is null)
        {
            return new McpConfigValidationResult
            {
                IsValid = false,
                Errors = errors,
                FormattedJson = root.ToJsonString(IndentedJson),
                Servers = []
            };
        }

        var servers = ExtractServers(serversNode, errors);
        return new McpConfigValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            FormattedJson = root.ToJsonString(IndentedJson),
            Servers = servers
        };
    }

    public string AddOrUpdateServer(string? rawJson, McpCatalogEntry entry)
    {
        var errors = new List<string>();
        var root = ParseRootObject(rawJson, errors) ?? CreateDefaultRoot();
        var serversNode = GetOrCreateServersNode(root, errors);
        if (serversNode is null)
        {
            return NormalizeEmptyConfig();
        }

        var key = BuildServerKey(entry.ServerName, entry.DisplayName);
        serversNode[key] = BuildServerDefinition(entry);
        return root.ToJsonString(IndentedJson);
    }

    public string RemoveServer(string? rawJson, string key)
    {
        var errors = new List<string>();
        var root = ParseRootObject(rawJson, errors) ?? CreateDefaultRoot();
        var serversNode = GetOrCreateServersNode(root, errors);
        if (serversNode is null)
        {
            return root.ToJsonString(IndentedJson);
        }

        serversNode.Remove(key);
        return root.ToJsonString(IndentedJson);
    }

    public string NormalizeEmptyConfig()
    {
        return CreateDefaultRoot().ToJsonString(IndentedJson);
    }

    private static JsonObject CreateDefaultRoot()
    {
        return new JsonObject
        {
            ["mcpServers"] = new JsonObject()
        };
    }

    private static JsonObject? ParseRootObject(string? rawJson, List<string> errors)
    {
        var text = rawJson?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return CreateDefaultRoot();
        }

        try
        {
            var rootNode = JsonNode.Parse(text);
            if (rootNode is not JsonObject rootObject)
            {
                errors.Add("MCP configuration root must be a JSON object.");
                return null;
            }

            return rootObject;
        }
        catch (JsonException ex)
        {
            errors.Add($"Invalid JSON: {ex.Message}");
            return null;
        }
    }

    private static JsonObject? GetOrCreateServersNode(JsonObject root, List<string> errors)
    {
        var mcpServersNode = root["mcpServers"];
        if (mcpServersNode is null)
        {
            var created = new JsonObject();
            root["mcpServers"] = created;
            return created;
        }

        if (mcpServersNode is not JsonObject serversObject)
        {
            errors.Add("Property 'mcpServers' must be a JSON object.");
            return null;
        }

        return serversObject;
    }

    private static IReadOnlyList<McpConfiguredServer> ExtractServers(JsonObject serversNode, List<string> errors)
    {
        var servers = new List<McpConfiguredServer>();

        foreach (var item in serversNode)
        {
            var key = item.Key;
            var value = item.Value;
            if (value is not JsonObject serverObject)
            {
                errors.Add($"Server '{key}' must be a JSON object.");
                continue;
            }

            var command = GetString(serverObject, "command");
            var url = GetString(serverObject, "url");
            var transport = GetString(serverObject, "transport");
            if (string.IsNullOrWhiteSpace(transport))
            {
                transport = string.IsNullOrWhiteSpace(url) ? "stdio" : "streamable-http";
            }

            if (string.IsNullOrWhiteSpace(command) && string.IsNullOrWhiteSpace(url))
            {
                errors.Add($"Server '{key}' must define either 'command' or 'url'.");
            }

            var args = new List<string>();
            if (serverObject["args"] is JsonArray argsNode)
            {
                foreach (var arg in argsNode)
                {
                    if (arg is null)
                    {
                        continue;
                    }

                    if (arg is JsonValue argValueNode &&
                        argValueNode.TryGetValue<string>(out var argValue))
                    {
                        args.Add(argValue);
                        continue;
                    }

                    errors.Add($"Server '{key}' has a non-string argument in 'args'.");
                }
            }

            servers.Add(new McpConfiguredServer
            {
                Key = key,
                Command = command,
                Url = url,
                TransportType = transport,
                Args = args
            });
        }

        return servers
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static JsonObject BuildServerDefinition(McpCatalogEntry entry)
    {
        var server = new JsonObject();

        var command = entry.Command.Trim();
        if (!string.IsNullOrWhiteSpace(command))
        {
            server["command"] = command;
        }

        if (entry.CommandArgs.Count > 0)
        {
            var args = new JsonArray();
            foreach (var arg in entry.CommandArgs)
            {
                args.Add(arg);
            }

            server["args"] = args;
        }

        if (entry.EnvironmentVariables.Count > 0)
        {
            var env = new JsonObject();
            foreach (var (key, value) in entry.EnvironmentVariables)
            {
                env[key] = value;
            }

            server["env"] = env;
        }

        if (string.IsNullOrWhiteSpace(command) &&
            !string.IsNullOrWhiteSpace(entry.PackageIdentifier) &&
            Uri.TryCreate(entry.PackageIdentifier, UriKind.Absolute, out _))
        {
            server["url"] = entry.PackageIdentifier;
        }

        if (!string.IsNullOrWhiteSpace(entry.TransportType))
        {
            server["transport"] = entry.TransportType;
        }

        if (!string.IsNullOrWhiteSpace(entry.InstallCommand))
        {
            server["x-agentsdashboard-install"] = new JsonObject
            {
                ["command"] = entry.InstallCommand.Trim()
            };
        }

        return server;
    }

    private static string BuildServerKey(string serverName, string displayName)
    {
        var preferred = string.IsNullOrWhiteSpace(serverName)
            ? displayName
            : serverName;

        if (string.IsNullOrWhiteSpace(preferred))
        {
            return $"server-{Guid.NewGuid():N}";
        }

        var key = preferred.Trim();
        var normalized = new string(
            key
                .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' ? char.ToLowerInvariant(ch) : '-')
                .ToArray());

        normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        normalized = normalized.Trim('-');

        return string.IsNullOrWhiteSpace(normalized)
            ? $"server-{Guid.NewGuid():N}"
            : normalized;
    }

    private static string GetString(JsonObject obj, string propertyName)
    {
        var node = obj[propertyName];
        if (node is null)
        {
            return string.Empty;
        }

        return node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var value)
            ? value
            : string.Empty;
    }
}
