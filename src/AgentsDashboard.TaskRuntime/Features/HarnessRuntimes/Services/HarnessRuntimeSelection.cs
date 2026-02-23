namespace AgentsDashboard.TaskRuntime.Features.HarnessRuntimes.Services;

public sealed record HarnessRuntimeSelection(
    IHarnessRuntime Primary,
    IHarnessRuntime? Fallback,
    string RuntimeMode);
