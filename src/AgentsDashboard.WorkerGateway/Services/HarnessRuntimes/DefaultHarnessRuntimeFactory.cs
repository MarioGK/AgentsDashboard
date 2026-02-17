namespace AgentsDashboard.WorkerGateway.Services.HarnessRuntimes;

public sealed class DefaultHarnessRuntimeFactory(
    CodexAppServerRuntime codexAppServerRuntime,
    OpenCodeSseRuntime openCodeSseRuntime,
    ClaudeStreamRuntime claudeStreamRuntime,
    ZaiClaudeCompatibleRuntime zaiClaudeCompatibleRuntime,
    CommandHarnessRuntime commandHarnessRuntime) : IHarnessRuntimeFactory
{
    public HarnessRuntimeSelection Select(HarnessRunRequest request)
    {
        var harness = request.Harness?.Trim() ?? string.Empty;
        var mode = request.Mode?.Trim() ?? string.Empty;

        if (IsZaiHarness(harness))
        {
            return new HarnessRuntimeSelection(zaiClaudeCompatibleRuntime, commandHarnessRuntime, "stream-json");
        }

        if (IsClaudeHarness(harness))
        {
            return new HarnessRuntimeSelection(claudeStreamRuntime, commandHarnessRuntime, "stream-json");
        }

        if (IsOpenCodeHarness(harness))
        {
            return new HarnessRuntimeSelection(openCodeSseRuntime, null, "sse");
        }

        if (string.Equals(harness, "codex", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(mode, "app-server", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mode, "structured", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mode, "auto", StringComparison.OrdinalIgnoreCase))
            {
                return new HarnessRuntimeSelection(codexAppServerRuntime, commandHarnessRuntime, "app-server");
            }

            return string.Equals(mode, "command", StringComparison.OrdinalIgnoreCase)
                ? new HarnessRuntimeSelection(commandHarnessRuntime, null, "command")
                : new HarnessRuntimeSelection(codexAppServerRuntime, commandHarnessRuntime, "app-server");
        }

        return new HarnessRuntimeSelection(commandHarnessRuntime, null, "command");
    }

    private static bool IsClaudeHarness(string harness)
    {
        if (string.IsNullOrWhiteSpace(harness))
        {
            return false;
        }

        return harness.Trim().ToLowerInvariant() switch
        {
            "claude-code" => true,
            "claude code" => true,
            "claude" => true,
            _ => false,
        };
    }

    private static bool IsZaiHarness(string harness)
    {
        return string.Equals(harness?.Trim(), "zai", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOpenCodeHarness(string harness)
    {
        if (string.IsNullOrWhiteSpace(harness))
        {
            return false;
        }

        return harness.Trim().ToLowerInvariant() switch
        {
            "opencode" => true,
            "open-code" => true,
            "open code" => true,
            _ => false,
        };
    }
}
