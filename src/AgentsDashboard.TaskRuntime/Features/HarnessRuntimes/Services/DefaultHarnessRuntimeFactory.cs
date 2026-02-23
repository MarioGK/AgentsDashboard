namespace AgentsDashboard.TaskRuntime.Features.HarnessRuntimes.Services;

public sealed class DefaultHarnessRuntimeFactory(
    CodexAppServerRuntime codexAppServerRuntime,
    OpenCodeSseRuntime openCodeSseRuntime) : IHarnessRuntimeFactory
{
    public HarnessRuntimeSelection Select(HarnessRunRequest request)
    {
        var harness = request.Harness?.Trim() ?? string.Empty;

        if (IsOpenCodeHarness(harness))
        {
            return new HarnessRuntimeSelection(openCodeSseRuntime, null, "sse");
        }

        if (string.Equals(harness, "codex", StringComparison.OrdinalIgnoreCase))
        {
            return new HarnessRuntimeSelection(codexAppServerRuntime, null, "stdio");
        }

        throw new NotSupportedException($"Harness '{request.Harness}' is not supported. Supported harnesses: codex, opencode.");
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
