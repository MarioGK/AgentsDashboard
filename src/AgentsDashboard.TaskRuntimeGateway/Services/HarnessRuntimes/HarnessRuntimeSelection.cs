namespace AgentsDashboard.TaskRuntimeGateway.Services.HarnessRuntimes;

public sealed record HarnessRuntimeSelection(
    IHarnessRuntime Primary,
    IHarnessRuntime? Fallback,
    string RuntimeMode);
