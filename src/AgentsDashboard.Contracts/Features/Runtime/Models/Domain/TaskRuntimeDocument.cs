namespace AgentsDashboard.Contracts.Features.Runtime.Models.Domain;

































































public sealed class TaskRuntimeDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RepositoryId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string RuntimeId { get; set; } = string.Empty;
    public TaskRuntimeState State { get; set; } = TaskRuntimeState.Inactive;
    public int ActiveRuns { get; set; }
    public int MaxParallelRuns { get; set; } = 1;
    public string Endpoint { get; set; } = string.Empty;
    public string ContainerId { get; set; } = string.Empty;
    public string WorkspacePath { get; set; } = string.Empty;
    public string RuntimeHomePath { get; set; } = string.Empty;
    public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;
    public DateTime? InactiveAfterUtc { get; set; }
    public string LastError { get; set; } = string.Empty;
    public DateTime? LastStateChangeUtc { get; set; }
    public DateTime? LastStartedAtUtc { get; set; }
    public DateTime? LastReadyAtUtc { get; set; }
    public long ColdStartCount { get; set; }
    public long ColdStartDurationTotalMs { get; set; }
    public long LastColdStartDurationMs { get; set; }
    public DateTime? LastBecameInactiveUtc { get; set; }
    public long InactiveTransitionCount { get; set; }
    public long InactiveDurationTotalMs { get; set; }
    public long LastInactiveDurationMs { get; set; }
}
