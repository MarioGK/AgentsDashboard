namespace AgentsDashboard.TaskRuntime.Features.HarnessRuntimes.Services;

public sealed record HarnessRuntimeEvent(
    HarnessRuntimeEventType Type,
    string Content,
    IReadOnlyDictionary<string, string>? Metadata = null);
