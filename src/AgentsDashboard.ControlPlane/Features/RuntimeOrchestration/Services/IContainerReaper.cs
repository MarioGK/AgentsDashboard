namespace AgentsDashboard.ControlPlane.Services;

public interface IContainerReaper
{
    Task<ContainerKillResult> KillContainerAsync(string runId, string reason, bool force, CancellationToken cancellationToken);
    Task<int> ReapOrphanedContainersAsync(IEnumerable<string> activeRunIds, CancellationToken cancellationToken);
}

public sealed class ContainerKillResult
{
    public bool Killed { get; init; }
    public string ContainerId { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
}
