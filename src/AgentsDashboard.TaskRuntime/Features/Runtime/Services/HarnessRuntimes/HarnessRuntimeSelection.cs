namespace AgentsDashboard.TaskRuntime.Services.HarnessRuntimes;

public sealed record HarnessRuntimeSelection(
    IHarnessRuntime Primary,
    IHarnessRuntime? Fallback,
    string RuntimeMode);
