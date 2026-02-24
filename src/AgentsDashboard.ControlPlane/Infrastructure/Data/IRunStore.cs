namespace AgentsDashboard.ControlPlane.Infrastructure.Data;

public interface IRunStore
{
    Task<RunDocument> CreateRunAsync(
        TaskDocument task,
        CancellationToken cancellationToken,
        int attempt = 1,
        HarnessExecutionMode? executionModeOverride = null,
        string? sessionProfileId = null,
        string? mcpConfigSnapshotJson = null);
    Task<List<RunDocument>> ListRunsByRepositoryAsync(string repositoryId, CancellationToken cancellationToken);
    Task<List<RunDocument>> ListRecentRunsAsync(CancellationToken cancellationToken);
    Task<List<RepositoryDocument>> ListRepositoriesWithRecentTasksAsync(int limit, CancellationToken cancellationToken);
    Task<List<RunDocument>> ListRunsByTaskAsync(string taskId, int limit, CancellationToken cancellationToken);
    Task<Dictionary<string, RunDocument>> GetLatestRunsByTaskIdsAsync(List<string> taskIds, CancellationToken cancellationToken);
    Task<Dictionary<string, RunState>> GetLatestRunStatesByTaskIdsAsync(List<string> taskIds, CancellationToken cancellationToken);

    Task<List<WorkspacePromptEntryDocument>> ListWorkspacePromptHistoryAsync(string taskId, int limit, CancellationToken cancellationToken);
    Task<List<WorkspacePromptEntryDocument>> ListWorkspacePromptEntriesForEmbeddingAsync(string taskId, CancellationToken cancellationToken);
    Task<WorkspacePromptEntryDocument> AppendWorkspacePromptEntryAsync(WorkspacePromptEntryDocument promptEntry, CancellationToken cancellationToken);
    Task<WorkspacePromptEntryDocument?> UpdateWorkspacePromptEntryContentAsync(string promptEntryId, string newContent, CancellationToken cancellationToken);
    Task<int> DeleteWorkspacePromptEntriesAsync(IReadOnlyList<string> promptEntryIds, CancellationToken cancellationToken);
    Task<List<WorkspaceQueuedMessageDocument>> ListWorkspaceQueuedMessagesAsync(string taskId, CancellationToken cancellationToken);
    Task<List<string>> ListTaskIdsWithQueuedMessagesAsync(CancellationToken cancellationToken);
    Task<WorkspaceQueuedMessageDocument> AppendWorkspaceQueuedMessageAsync(WorkspaceQueuedMessageDocument queuedMessage, CancellationToken cancellationToken);
    Task<int> DeleteWorkspaceQueuedMessagesAsync(IReadOnlyList<string> queuedMessageIds, CancellationToken cancellationToken);
    Task<int> DeleteWorkspaceQueuedMessagesByTaskAsync(string taskId, CancellationToken cancellationToken);
    Task<RunQuestionRequestDocument?> UpsertRunQuestionRequestAsync(RunQuestionRequestDocument questionRequest, CancellationToken cancellationToken);
    Task<List<RunQuestionRequestDocument>> ListPendingRunQuestionRequestsAsync(string taskId, string runId, CancellationToken cancellationToken);
    Task<RunQuestionRequestDocument?> GetRunQuestionRequestAsync(string questionRequestId, CancellationToken cancellationToken);
    Task<RunQuestionRequestDocument?> MarkRunQuestionRequestAnsweredAsync(
        string questionRequestId,
        IReadOnlyList<RunQuestionAnswerDocument> answers,
        string answeredRunId,
        CancellationToken cancellationToken);
    Task<RunAiSummaryDocument> UpsertRunAiSummaryAsync(RunAiSummaryDocument summary, CancellationToken cancellationToken);
    Task<RunAiSummaryDocument?> GetRunAiSummaryAsync(string runId, CancellationToken cancellationToken);

    Task<ReliabilityMetrics> GetReliabilityMetricsByRepositoryAsync(string repositoryId, CancellationToken cancellationToken);
    Task<List<RunDocument>> ListCompletedRunsByTaskForEmbeddingAsync(string taskId, CancellationToken cancellationToken);

    Task<RunDocument?> GetRunAsync(string runId, CancellationToken cancellationToken);
    Task<List<RunDocument>> ListRunsByStateAsync(RunState state, CancellationToken cancellationToken);
    Task<List<string>> ListTaskIdsWithQueuedRunsAsync(CancellationToken cancellationToken);
    Task<List<string>> ListAllRunIdsAsync(CancellationToken cancellationToken);
    Task<long> CountRunsByStateAsync(RunState state, CancellationToken cancellationToken);
    Task<long> CountActiveRunsAsync(CancellationToken cancellationToken);
    Task<long> CountActiveRunsByRepoAsync(string repositoryId, CancellationToken cancellationToken);
    Task<long> CountActiveRunsByTaskAsync(string taskId, CancellationToken cancellationToken);
    Task<RunDocument?> MarkRunStartedAsync(
        string runId,
        string workerId,
        CancellationToken cancellationToken,
        string? workerImageRef = null,
        string? workerImageDigest = null,
        string? workerImageSource = null);
    Task<RunDocument?> MarkRunCompletedAsync(string runId, bool succeeded, string summary, string outputJson, CancellationToken cancellationToken, string? failureClass = null, string? prUrl = null);
    Task<RunDocument?> MarkRunCancelledAsync(string runId, CancellationToken cancellationToken);
    Task<RunDocument?> MarkRunObsoleteAsync(string runId, CancellationToken cancellationToken);
    Task<int> DeleteRunsCascadeAsync(IReadOnlyList<string> runIds, CancellationToken cancellationToken);
    Task<RunDocument?> MarkRunPendingApprovalAsync(string runId, CancellationToken cancellationToken);
    Task<RunDocument?> ApproveRunAsync(string runId, CancellationToken cancellationToken);
    Task<RunDocument?> RejectRunAsync(string runId, CancellationToken cancellationToken);
    Task<int> BulkCancelRunsAsync(List<string> runIds, CancellationToken cancellationToken);

    Task SaveArtifactAsync(string runId, string fileName, Stream stream, CancellationToken cancellationToken);
    Task<List<string>> ListArtifactsAsync(string runId, CancellationToken cancellationToken);
    Task<Stream?> GetArtifactAsync(string runId, string fileName, CancellationToken cancellationToken);

    Task AddRunLogAsync(RunLogEvent logEvent, CancellationToken cancellationToken);
    Task<List<RunLogEvent>> ListRunLogsAsync(string runId, CancellationToken cancellationToken);
    Task<RunStructuredEventDocument> AppendRunStructuredEventAsync(RunStructuredEventDocument structuredEvent, CancellationToken cancellationToken);
    Task<List<RunStructuredEventDocument>> ListRunStructuredEventsAsync(string runId, int limit, CancellationToken cancellationToken);
    Task<RunDiffSnapshotDocument> UpsertRunDiffSnapshotAsync(RunDiffSnapshotDocument snapshot, CancellationToken cancellationToken);
    Task<RunDiffSnapshotDocument?> GetLatestRunDiffSnapshotAsync(string runId, CancellationToken cancellationToken);
    Task<List<RunToolProjectionDocument>> ListRunToolProjectionsAsync(string runId, CancellationToken cancellationToken);
    Task<RunInstructionStackDocument> UpsertRunInstructionStackAsync(RunInstructionStackDocument stack, CancellationToken cancellationToken);
    Task<RunInstructionStackDocument?> GetRunInstructionStackAsync(string runId, CancellationToken cancellationToken);
    Task<RunShareBundleDocument> UpsertRunShareBundleAsync(RunShareBundleDocument bundle, CancellationToken cancellationToken);
    Task<RunShareBundleDocument?> GetRunShareBundleAsync(string runId, CancellationToken cancellationToken);
    Task<StructuredRunDataPruneResult> PruneStructuredRunDataAsync(DateTime olderThanUtc, int maxRuns, CancellationToken cancellationToken);

    Task UpsertSemanticChunksAsync(string taskId, List<SemanticChunkDocument> chunks, CancellationToken cancellationToken);
    Task<List<SemanticChunkDocument>> SearchWorkspaceSemanticAsync(
        string taskId,
        string queryText,
        string? queryEmbeddingPayload,
        int limit,
        CancellationToken cancellationToken);

    Task<ReliabilityMetrics> GetReliabilityMetricsAsync(CancellationToken cancellationToken);
}
