namespace AgentsDashboard.ControlPlane.Data;






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
