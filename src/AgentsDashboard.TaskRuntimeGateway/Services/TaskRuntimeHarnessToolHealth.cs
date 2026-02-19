namespace AgentsDashboard.TaskRuntimeGateway.Services;


public sealed record TaskRuntimeHarnessToolHealth(
    string Command,
    string DisplayName,
    string Status,
    string? Version);
