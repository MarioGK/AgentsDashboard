namespace AgentsDashboard.Contracts.Domain;

































































public sealed class TaskRuntimeStateUpdate
{
    public string RuntimeId { get; set; } = string.Empty;
    public string RepositoryId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public TaskRuntimeState State { get; set; } = TaskRuntimeState.Inactive;
    public int ActiveRuns { get; set; }
    public int MaxParallelRuns { get; set; } = 1;
    public string Endpoint { get; set; } = string.Empty;
    public string ContainerId { get; set; } = string.Empty;
    public string WorkspacePath { get; set; } = string.Empty;
    public string RuntimeHomePath { get; set; } = string.Empty;
    public DateTime ObservedAtUtc { get; set; } = DateTime.UtcNow;
    public bool UpdateLastActivityUtc { get; set; } = true;
    public DateTime? InactiveAfterUtc { get; set; }
    public bool ClearInactiveAfterUtc { get; set; }
    public string LastError { get; set; } = string.Empty;
}
