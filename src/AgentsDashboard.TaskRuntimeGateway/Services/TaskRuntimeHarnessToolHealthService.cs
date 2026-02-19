using CliWrap;
using CliWrap.Buffered;

namespace AgentsDashboard.TaskRuntimeGateway.Services;

public sealed class TaskRuntimeHarnessToolHealthService
{
    private static readonly ToolDefinition[] Tools =
    [
        new("codex", "Codex"),
        new("opencode", "OpenCode")
    ];

    public async Task<IReadOnlyList<TaskRuntimeHarnessToolHealth>> GetHarnessToolsAsync(CancellationToken cancellationToken)
    {
        var checks = Tools.Select(tool => CheckToolAsync(tool, cancellationToken));
        return await Task.WhenAll(checks);
    }

    private static async Task<TaskRuntimeHarnessToolHealth> CheckToolAsync(ToolDefinition tool, CancellationToken cancellationToken)
    {
        try
        {
            var whichResult = await Cli.Wrap("which")
                .WithArguments(tool.Command)
                .ExecuteBufferedAsync(cancellationToken);

            if (whichResult.ExitCode != 0)
            {
                return new TaskRuntimeHarnessToolHealth(tool.Command, tool.DisplayName, "unavailable", null);
            }

            var version = await GetVersionAsync(tool.Command, cancellationToken);
            return new TaskRuntimeHarnessToolHealth(tool.Command, tool.DisplayName, "available", version);
        }
        catch
        {
            return new TaskRuntimeHarnessToolHealth(tool.Command, tool.DisplayName, "unknown", null);
        }
    }

    private static async Task<string?> GetVersionAsync(string command, CancellationToken cancellationToken)
    {
        try
        {
            var versionResult = await Cli.Wrap(command)
                .WithArguments("--version")
                .ExecuteBufferedAsync(cancellationToken);

            if (versionResult.ExitCode != 0 || string.IsNullOrWhiteSpace(versionResult.StandardOutput))
            {
                return null;
            }

            var line = versionResult.StandardOutput
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(line))
            {
                return null;
            }

            return line[..Math.Min(80, line.Length)];
        }
        catch
        {
            return null;
        }
    }

    private sealed record ToolDefinition(string Command, string DisplayName);
}

public sealed record TaskRuntimeHarnessToolHealth(
    string Command,
    string DisplayName,
    string Status,
    string? Version);
