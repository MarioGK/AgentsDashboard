namespace AgentsDashboard.TaskRuntime.Features.Health.Services;


public sealed record TaskRuntimeHarnessToolHealth(
    string Command,
    string DisplayName,
    string Status,
    string? Version);
