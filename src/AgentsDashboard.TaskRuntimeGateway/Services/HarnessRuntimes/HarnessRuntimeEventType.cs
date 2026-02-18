namespace AgentsDashboard.TaskRuntimeGateway.Services.HarnessRuntimes;

public enum HarnessRuntimeEventType
{
    Log,
    ReasoningDelta,
    AssistantDelta,
    CommandOutput,
    DiffUpdate,
    Completion,
    Diagnostic,
}

public static class HarnessRuntimeEventTypeExtensions
{
    public static string ToCanonicalName(this HarnessRuntimeEventType value)
    {
        return value switch
        {
            HarnessRuntimeEventType.Log => "log",
            HarnessRuntimeEventType.ReasoningDelta => "reasoning_delta",
            HarnessRuntimeEventType.AssistantDelta => "assistant_delta",
            HarnessRuntimeEventType.CommandOutput => "command_output",
            HarnessRuntimeEventType.DiffUpdate => "diff_update",
            HarnessRuntimeEventType.Completion => "completion",
            HarnessRuntimeEventType.Diagnostic => "diagnostic",
            _ => "log",
        };
    }
}
