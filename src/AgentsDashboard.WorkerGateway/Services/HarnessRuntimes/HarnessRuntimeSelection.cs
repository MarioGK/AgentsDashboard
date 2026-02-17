namespace AgentsDashboard.WorkerGateway.Services.HarnessRuntimes;

public sealed record HarnessRuntimeSelection(
    IHarnessRuntime Primary,
    IHarnessRuntime? Fallback,
    string RuntimeMode);
