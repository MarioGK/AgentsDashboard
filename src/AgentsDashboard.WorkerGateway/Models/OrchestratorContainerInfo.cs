namespace AgentsDashboard.WorkerGateway.Models;

public sealed class OrchestratorContainerInfo
{
    public required string ContainerId { get; init; }
    public required string RunId { get; init; }
    public string TaskId { get; init; } = string.Empty;
    public string RepoId { get; init; } = string.Empty;
    public string State { get; init; } = string.Empty;
    public string Image { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

public sealed record ContainerKillResult(bool Killed, string ContainerId, string Error);
