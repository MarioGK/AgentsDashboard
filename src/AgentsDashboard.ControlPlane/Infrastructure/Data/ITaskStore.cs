namespace AgentsDashboard.ControlPlane.Infrastructure.Data;

public interface ITaskStore
{
    Task<TaskDocument> CreateTaskAsync(CreateTaskRequest request, CancellationToken cancellationToken);
    Task<List<TaskDocument>> ListTasksAsync(string repositoryId, CancellationToken cancellationToken);
    Task<TaskDocument?> GetTaskAsync(string taskId, CancellationToken cancellationToken);
    Task<List<TaskDocument>> ListDueTasksAsync(DateTime utcNow, int limit, CancellationToken cancellationToken);
    Task<TaskDocument?> UpdateTaskGitMetadataAsync(
        string taskId,
        DateTime? lastGitSyncAtUtc,
        string? lastGitSyncError,
        CancellationToken cancellationToken);
    Task<TaskDocument?> UpdateTaskAsync(string taskId, UpdateTaskRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteTaskAsync(string taskId, CancellationToken cancellationToken);

    Task<DbStorageSnapshot> GetStorageSnapshotAsync(CancellationToken cancellationToken);
    Task<List<TaskCleanupCandidate>> ListTaskCleanupCandidatesAsync(TaskCleanupQuery query, CancellationToken cancellationToken);
    Task<TaskCascadeDeleteResult> DeleteTaskCascadeAsync(string taskId, CancellationToken cancellationToken);
    Task<CleanupBatchResult> DeleteTasksCascadeAsync(IReadOnlyList<string> taskIds, CancellationToken cancellationToken);
    Task VacuumAsync(CancellationToken cancellationToken);
}
