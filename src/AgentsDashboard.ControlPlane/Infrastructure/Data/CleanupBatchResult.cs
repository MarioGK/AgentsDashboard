namespace AgentsDashboard.ControlPlane.Infrastructure.Data;






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
