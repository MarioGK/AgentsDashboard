namespace AgentsDashboard.ControlPlane.Data;

public sealed record TaskCleanupQuery(
    DateTime OlderThanUtc,
    DateTime ProtectedSinceUtc,
    int Limit,
    bool OnlyWithNoActiveRuns = true,
    string? RepositoryId = null,
    int ScanLimit = 0);

public sealed record TaskCleanupCandidate(
    string TaskId,
    string RepositoryId,
    DateTime CreatedAtUtc,
    DateTime LastActivityUtc,
    bool HasActiveRuns,
    int RunCount,
    DateTime? OldestRunUtc);

public sealed record DbStorageSnapshot(
    string DatabasePath,
    long MainFileBytes,
    long WalFileBytes,
    long TotalBytes,
    bool Exists,
    DateTime MeasuredAtUtc);

public sealed record TaskCascadeDeleteResult(
    string TaskId,
    string RepositoryId,
    bool TaskDeleted,
    int DeletedRuns,
    int DeletedRunLogs,
    int DeletedFindings,
    int DeletedPromptEntries,
    int DeletedRunSummaries,
    int DeletedSemanticChunks,
    int DeletedArtifactDirectories,
    int ArtifactDeleteErrors,
    int DeletedTaskWorkspaceDirectories,
    int TaskWorkspaceDeleteErrors);

public sealed record CleanupBatchResult(
    int TasksRequested,
    int TasksDeleted,
    int FailedTasks,
    int DeletedRuns,
    int DeletedRunLogs,
    int DeletedFindings,
    int DeletedPromptEntries,
    int DeletedRunSummaries,
    int DeletedSemanticChunks,
    int DeletedArtifactDirectories,
    int ArtifactDeleteErrors,
    int DeletedTaskWorkspaceDirectories,
    int TaskWorkspaceDeleteErrors);
