using System.Diagnostics;

namespace AgentsDashboard.TaskRuntime.Services;

public sealed partial class TaskRuntimeHarnessToolHealthService
{
    private sealed record ToolDefinition(string Command, string DisplayName);

    private sealed record CommandExecutionResult(int ExitCode, string StandardOutput, string StandardError);

    private static readonly ToolDefinition[] Tools =
    [
        new("codex", "Codex"),
        new("opencode", "OpenCode"),
        new("claude-code", "Claude Code"),
        new("zai", "Z.ai")
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
            var whichResult = await ExecuteCommandAsync("which", [tool.Command], cancellationToken);

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
            var versionResult = await ExecuteCommandAsync(command, ["--version"], cancellationToken);

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

    private static async Task<CommandExecutionResult> ExecuteCommandAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = Directory.GetCurrentDirectory(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (!process.Start())
        {
            return new CommandExecutionResult(-1, string.Empty, $"Failed to start '{fileName}'.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKillProcess(process);
            throw;
        }

        return new CommandExecutionResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }
}
