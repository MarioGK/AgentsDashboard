namespace AgentsDashboard.ControlPlane.Data;

public sealed record TaskCleanupQuery(
    DateTime OlderThanUtc,
    DateTime ProtectedSinceUtc,
    int Limit,
    bool OnlyWithNoActiveRuns = true,
    string? RepositoryId = null,
    int ScanLimit = 0,
    bool IncludeRetentionEligibility = true,
    bool IncludeDisabledInactiveEligibility = false,
    DateTime DisabledInactiveOlderThanUtc = default,
    bool ExcludeWorkflowReferencedTasks = false,
    bool ExcludeTasksWithOpenFindings = false);

public sealed record TaskCleanupCandidate(
    string TaskId,
    string RepositoryId,
    DateTime CreatedAtUtc,
    DateTime LastActivityUtc,
    bool HasActiveRuns,
    int RunCount,
    DateTime? OldestRunUtc,
    bool IsRetentionEligible = false,
    bool IsDisabledInactiveEligible = false,
    bool IsWorkflowReferenced = false,
    bool HasOpenFindings = false);

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

public sealed record StructuredRunDataPruneResult(
    int RunsScanned,
    int DeletedStructuredEvents,
    int DeletedDiffSnapshots,
    int DeletedToolProjections);
