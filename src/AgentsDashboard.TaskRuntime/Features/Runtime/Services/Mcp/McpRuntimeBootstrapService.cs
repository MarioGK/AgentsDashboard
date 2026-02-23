using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using AgentsDashboard.Contracts.TaskRuntime;

namespace AgentsDashboard.TaskRuntime.Services;

public sealed class McpRuntimeBootstrapService(ILogger<McpRuntimeBootstrapService> logger)
{
    private const string ConfigDirectoryName = ".agentsdashboard";
    private const string ConfigFileName = "mcp.settings.json";

    public async Task<McpRuntimeBootstrapResult> PrepareAsync(
        DispatchJobRequest request,
        string workspacePath,
        Dictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
        var raw = request.McpConfigSnapshotJson?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new McpRuntimeBootstrapResult
            {
                HasConfig = false,
                IsValid = true,
                EffectiveJson = string.Empty,
                Diagnostics = ["No MCP snapshot configuration provided."]
            };
        }

        var diagnostics = new List<string>();
        JsonObject root;
        JsonObject servers;

        try
        {
            var parsed = JsonNode.Parse(raw);
            if (parsed is not JsonObject parsedObject)
            {
                return new McpRuntimeBootstrapResult
                {
                    HasConfig = true,
                    IsValid = false,
                    Diagnostics = ["MCP config root is not a JSON object."]
                };
            }

            root = parsedObject;
            var serversNode = root["mcpServers"];
            if (serversNode is null)
            {
                servers = new JsonObject();
                root["mcpServers"] = servers;
            }
            else if (serversNode is JsonObject serversObject)
            {
                servers = serversObject;
            }
            else
            {
                return new McpRuntimeBootstrapResult
                {
                    HasConfig = true,
                    IsValid = false,
                    Diagnostics = ["Property 'mcpServers' must be a JSON object."]
                };
            }
        }
        catch (JsonException ex)
        {
            return new McpRuntimeBootstrapResult
            {
                HasConfig = true,
                IsValid = false,
                Diagnostics = [$"MCP config JSON parse failed: {ex.Message}"]
            };
        }

        var installActions = 0;

        foreach (var server in servers)
        {
            if (server.Value is not JsonObject serverObject)
            {
                diagnostics.Add($"Server '{server.Key}' is not a JSON object.");
                continue;
            }

            var command = GetString(serverObject, "command");
            if (string.IsNullOrWhiteSpace(command))
            {
                continue;
            }

            if (CommandExists(command))
            {
                continue;
            }

            var installCommand = GetInstallCommand(serverObject);
            if (string.IsNullOrWhiteSpace(installCommand))
            {
                diagnostics.Add($"Missing command '{command}' for server '{server.Key}' and no install recipe was provided.");
                continue;
            }

            var installResult = await TryRunInstallCommandAsync(installCommand, workspacePath, cancellationToken);
            diagnostics.Add(installResult);
            installActions++;
        }

        var formattedJson = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var configDirectory = Path.Combine(workspacePath, ConfigDirectoryName);
        Directory.CreateDirectory(configDirectory);
        var configPath = Path.Combine(configDirectory, ConfigFileName);

        await File.WriteAllTextAsync(configPath, formattedJson, cancellationToken);

        environment["AGENTSDASHBOARD_MCP_CONFIG_PATH"] = configPath;
        environment["AGENTSDASHBOARD_MCP_CONFIG_JSON"] = formattedJson;
        environment["MCP_CONFIG_PATH"] = configPath;
        environment["CODEX_MCP_CONFIG_PATH"] = configPath;
        environment["OPENCODE_MCP_CONFIG_PATH"] = configPath;

        logger.LogInformation(
            "Prepared MCP runtime config {ConfigPath} for run {RunId} ({InstallActions} install actions)",
            configPath,
            request.RunId,
            installActions);

        return new McpRuntimeBootstrapResult
        {
            HasConfig = true,
            IsValid = true,
            ConfigPath = configPath,
            EffectiveJson = formattedJson,
            InstallActionCount = installActions,
            Diagnostics = diagnostics
        };
    }

    private static string GetInstallCommand(JsonObject serverObject)
    {
        if (serverObject["x-agentsdashboard-install"] is not JsonObject installObject)
        {
            return string.Empty;
        }

        return GetString(installObject, "command");
    }

    private static bool CommandExists(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return false;
        }

        if (command.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            command.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            return File.Exists(command);
        }

        if (OperatingSystem.IsWindows())
        {
            var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT;.COM")
                .Split(';', StringSplitOptions.RemoveEmptyEntries);

            foreach (var path in paths)
            {
                foreach (var extension in extensions)
                {
                    var candidate = Path.Combine(path, command + extension);
                    if (File.Exists(candidate))
                    {
                        return true;
                    }
                }

                var plain = Path.Combine(path, command);
                if (File.Exists(plain))
                {
                    return true;
                }
            }

            return false;
        }

        var unixPaths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

        foreach (var path in unixPaths)
        {
            var candidate = Path.Combine(path, command);
            if (File.Exists(candidate))
            {
                return true;
            }
        }

        return false;
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

    private async Task<string> TryRunInstallCommandAsync(string command, string workingDirectory, CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromMinutes(5);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(timeout);

        var isWindows = OperatingSystem.IsWindows();
        var shell = isWindows ? "cmd.exe" : "/bin/bash";
        var args = isWindows ? $"/c {command}" : $"-lc \"{EscapeForBash(command)}\"";

        var startInfo = new ProcessStartInfo(shell)
        {
            Arguments = args,
            WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                ? Directory.GetCurrentDirectory()
                : workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        try
        {
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return $"Install command failed to start: {command}";
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(linkedCts.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode == 0)
            {
                return $"Install command succeeded: {command}";
            }

            return $"Install command failed ({process.ExitCode}): {command}. {Truncate(stderr)} {Truncate(stdout)}";
        }
        catch (OperationCanceledException)
        {
            return $"Install command timed out: {command}";
        }
        catch (Exception ex)
        {
            return $"Install command error: {command}. {ex.Message}";
        }
    }

    private static string EscapeForBash(string input)
    {
        return input.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static string Truncate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 400
            ? trimmed
            : trimmed[..400];
    }
}
