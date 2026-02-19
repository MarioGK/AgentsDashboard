namespace AgentsDashboard.TaskRuntime.Services.HarnessRuntimes;

public sealed record HarnessRuntimeEvent(
    HarnessRuntimeEventType Type,
    string Content,
    IReadOnlyDictionary<string, string>? Metadata = null);
