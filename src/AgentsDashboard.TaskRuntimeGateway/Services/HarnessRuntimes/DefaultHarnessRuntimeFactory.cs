namespace AgentsDashboard.TaskRuntimeGateway.Services.HarnessRuntimes;

public sealed class DefaultHarnessRuntimeFactory(
    CodexAppServerRuntime codexAppServerRuntime,
    OpenCodeSseRuntime openCodeSseRuntime,
    CommandHarnessRuntime commandHarnessRuntime) : IHarnessRuntimeFactory
{
    public HarnessRuntimeSelection Select(HarnessRunRequest request)
    {
        var harness = request.Harness?.Trim() ?? string.Empty;
        var mode = request.Mode?.Trim() ?? string.Empty;

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
