using CliWrap;
using CliWrap.Buffered;

namespace AgentsDashboard.TaskRuntimeGateway.Services;

public sealed class TaskRuntimeHarnessToolHealthService
{
    private static readonly ToolDefinition[] Tools =
    [
        new("codex", "Codex"),
        new("opencode", "OpenCode"),
        new("claude-code", "Claude Code"),
        new("zai", "Z.ai")
    ];

    public async Task<IReadOnlyList<WorkerHarnessToolHealth>> GetHarnessToolsAsync(CancellationToken cancellationToken)
    {
        var checks = Tools.Select(tool => CheckToolAsync(tool, cancellationToken));
        return await Task.WhenAll(checks);
    }

    private static async Task<WorkerHarnessToolHealth> CheckToolAsync(ToolDefinition tool, CancellationToken cancellationToken)
    {
        try
        {
            var whichResult = await Cli.Wrap("which")
                .WithArguments(tool.Command)
                .ExecuteBufferedAsync(cancellationToken);

            if (whichResult.ExitCode != 0)
            {
                return new WorkerHarnessToolHealth(tool.Command, tool.DisplayName, "unavailable", null);
            }

            var version = await GetVersionAsync(tool.Command, cancellationToken);
            return new WorkerHarnessToolHealth(tool.Command, tool.DisplayName, "available", version);
        }
        catch
        {
            return new WorkerHarnessToolHealth(tool.Command, tool.DisplayName, "unknown", null);
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

public sealed record WorkerHarnessToolHealth(
    string Command,
    string DisplayName,
    string Status,
    string? Version);
