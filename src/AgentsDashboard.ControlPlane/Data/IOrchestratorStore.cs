using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.ControlPlane.Data;

public interface IOrchestratorStore
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<RepositoryDocument> CreateRepositoryAsync(CreateRepositoryRequest request, CancellationToken cancellationToken);
    Task<List<RepositoryDocument>> ListRepositoriesAsync(CancellationToken cancellationToken);
    Task<RepositoryDocument?> GetRepositoryAsync(string repositoryId, CancellationToken cancellationToken);
    Task<RepositoryDocument?> UpdateRepositoryAsync(string repositoryId, UpdateRepositoryRequest request, CancellationToken cancellationToken);
    Task<RepositoryDocument?> UpdateRepositoryGitStateAsync(string repositoryId, RepositoryGitStatus gitStatus, CancellationToken cancellationToken);
    Task<RepositoryDocument?> TouchRepositoryAsync(string repositoryId, CancellationToken cancellationToken);
    Task<bool> DeleteRepositoryAsync(string repositoryId, CancellationToken cancellationToken);
    Task<List<InstructionFile>> GetRepositoryInstructionFilesAsync(string repositoryId, CancellationToken cancellationToken);
    Task<RepositoryDocument?> UpdateRepositoryInstructionFilesAsync(string repositoryId, List<InstructionFile> instructionFiles, CancellationToken cancellationToken);
    Task<List<RepositoryInstructionDocument>> GetInstructionsAsync(string repositoryId, CancellationToken cancellationToken);
    Task<RepositoryInstructionDocument?> GetInstructionAsync(string instructionId, CancellationToken cancellationToken);
    Task<RepositoryInstructionDocument> UpsertInstructionAsync(string repositoryId, string? instructionId, CreateRepositoryInstructionRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteInstructionAsync(string instructionId, CancellationToken cancellationToken);
    Task<HarnessProviderSettingsDocument?> GetHarnessProviderSettingsAsync(string repositoryId, string harness, CancellationToken cancellationToken);
    Task<HarnessProviderSettingsDocument> UpsertHarnessProviderSettingsAsync(string repositoryId, string harness, string model, double temperature, int maxTokens, Dictionary<string, string>? additionalSettings, CancellationToken cancellationToken);
    Task<PromptSkillDocument> CreatePromptSkillAsync(CreatePromptSkillRequest request, CancellationToken cancellationToken);
    Task<List<PromptSkillDocument>> ListPromptSkillsAsync(string repositoryId, bool includeGlobal, CancellationToken cancellationToken);
    Task<PromptSkillDocument?> GetPromptSkillAsync(string skillId, CancellationToken cancellationToken);
    Task<PromptSkillDocument?> UpdatePromptSkillAsync(string skillId, UpdatePromptSkillRequest request, CancellationToken cancellationToken);
    Task<bool> DeletePromptSkillAsync(string skillId, CancellationToken cancellationToken);
    Task<RunSessionProfileDocument> CreateRunSessionProfileAsync(CreateRunSessionProfileRequest request, CancellationToken cancellationToken);
    Task<List<RunSessionProfileDocument>> ListRunSessionProfilesAsync(string repositoryId, bool includeGlobal, CancellationToken cancellationToken);
    Task<RunSessionProfileDocument?> GetRunSessionProfileAsync(string sessionProfileId, CancellationToken cancellationToken);
    Task<RunSessionProfileDocument?> UpdateRunSessionProfileAsync(string sessionProfileId, UpdateRunSessionProfileRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteRunSessionProfileAsync(string sessionProfileId, CancellationToken cancellationToken);
    Task<AutomationDefinitionDocument> UpsertAutomationDefinitionAsync(string? automationId, UpsertAutomationDefinitionRequest request, CancellationToken cancellationToken);
    Task<List<AutomationDefinitionDocument>> ListAutomationDefinitionsAsync(string repositoryId, CancellationToken cancellationToken);
    Task<AutomationDefinitionDocument?> GetAutomationDefinitionAsync(string automationId, CancellationToken cancellationToken);
    Task<bool> DeleteAutomationDefinitionAsync(string automationId, CancellationToken cancellationToken);
    Task<AutomationExecutionDocument> CreateAutomationExecutionAsync(AutomationExecutionDocument execution, CancellationToken cancellationToken);
    Task<List<AutomationExecutionDocument>> ListAutomationExecutionsAsync(string repositoryId, int limit, CancellationToken cancellationToken);

    Task<TaskDocument> CreateTaskAsync(CreateTaskRequest request, CancellationToken cancellationToken);
    Task<List<TaskDocument>> ListTasksAsync(string repositoryId, CancellationToken cancellationToken);
    Task<List<TaskDocument>> ListEventDrivenTasksAsync(string repositoryId, CancellationToken cancellationToken);
    Task<TaskDocument?> GetTaskAsync(string taskId, CancellationToken cancellationToken);
    Task<List<TaskDocument>> ListScheduledTasksAsync(CancellationToken cancellationToken);
    Task<List<TaskDocument>> ListDueTasksAsync(DateTime utcNow, int limit, CancellationToken cancellationToken);
    Task MarkOneShotTaskConsumedAsync(string taskId, CancellationToken cancellationToken);
    Task UpdateTaskNextRunAsync(string taskId, DateTime? nextRunAtUtc, CancellationToken cancellationToken);
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

    Task<RunDocument> CreateRunAsync(
        TaskDocument task,
        CancellationToken cancellationToken,
        int attempt = 1,
        HarnessExecutionMode? executionModeOverride = null,
        string? sessionProfileId = null,
        string? automationRunId = null);
    Task<List<RunDocument>> ListRunsByRepositoryAsync(string repositoryId, CancellationToken cancellationToken);
    Task<List<RunDocument>> ListRecentRunsAsync(CancellationToken cancellationToken);
    Task<List<RepositoryDocument>> ListRepositoriesWithRecentTasksAsync(int limit, CancellationToken cancellationToken);
    Task<List<RunDocument>> ListRunsByTaskAsync(string taskId, int limit, CancellationToken cancellationToken);
    Task<Dictionary<string, RunDocument>> GetLatestRunsByTaskIdsAsync(List<string> taskIds, CancellationToken cancellationToken);
    Task<Dictionary<string, RunState>> GetLatestRunStatesByTaskIdsAsync(List<string> taskIds, CancellationToken cancellationToken);
    Task<List<WorkspacePromptEntryDocument>> ListWorkspacePromptHistoryAsync(string taskId, int limit, CancellationToken cancellationToken);
    Task<List<WorkspacePromptEntryDocument>> ListWorkspacePromptEntriesForEmbeddingAsync(string taskId, CancellationToken cancellationToken);
    Task<WorkspacePromptEntryDocument> AppendWorkspacePromptEntryAsync(WorkspacePromptEntryDocument promptEntry, CancellationToken cancellationToken);
    Task<List<RunDocument>> ListCompletedRunsByTaskForEmbeddingAsync(string taskId, CancellationToken cancellationToken);
    Task<RunAiSummaryDocument> UpsertRunAiSummaryAsync(RunAiSummaryDocument summary, CancellationToken cancellationToken);
    Task<RunAiSummaryDocument?> GetRunAiSummaryAsync(string runId, CancellationToken cancellationToken);
    Task UpsertSemanticChunksAsync(string taskId, List<SemanticChunkDocument> chunks, CancellationToken cancellationToken);
    Task<List<SemanticChunkDocument>> SearchWorkspaceSemanticAsync(string taskId, string queryText, string? queryEmbeddingPayload, int limit, CancellationToken cancellationToken);
    Task<ReliabilityMetrics> GetReliabilityMetricsByRepositoryAsync(string repositoryId, CancellationToken cancellationToken);
    Task<RunDocument?> GetRunAsync(string runId, CancellationToken cancellationToken);
    Task<List<RunDocument>> ListRunsByStateAsync(RunState state, CancellationToken cancellationToken);
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
    Task<StructuredRunDataPruneResult> PruneStructuredRunDataAsync(
        DateTime olderThanUtc,
        int maxRuns,
        bool excludeWorkflowReferencedTasks,
        bool excludeTasksWithOpenFindings,
        CancellationToken cancellationToken);

    Task<List<FindingDocument>> ListFindingsAsync(string repositoryId, CancellationToken cancellationToken);
    Task<List<FindingDocument>> ListAllFindingsAsync(CancellationToken cancellationToken);
    Task<FindingDocument?> GetFindingAsync(string findingId, CancellationToken cancellationToken);
    Task<FindingDocument> CreateFindingFromFailureAsync(RunDocument run, string description, CancellationToken cancellationToken);
    Task<FindingDocument?> UpdateFindingStateAsync(string findingId, FindingState state, CancellationToken cancellationToken);
    Task<FindingDocument?> AssignFindingAsync(string findingId, string assignedTo, CancellationToken cancellationToken);
    Task<bool> DeleteFindingAsync(string findingId, CancellationToken cancellationToken);

    Task UpsertProviderSecretAsync(string repositoryId, string provider, string encryptedValue, CancellationToken cancellationToken);
    Task<List<ProviderSecretDocument>> ListProviderSecretsAsync(string repositoryId, CancellationToken cancellationToken);
    Task<ProviderSecretDocument?> GetProviderSecretAsync(string repositoryId, string provider, CancellationToken cancellationToken);
    Task<bool> DeleteProviderSecretAsync(string repositoryId, string provider, CancellationToken cancellationToken);

    Task<List<TaskRuntimeRegistration>> ListTaskRuntimeRegistrationsAsync(CancellationToken cancellationToken);
    Task UpsertTaskRuntimeRegistrationHeartbeatAsync(string runtimeId, string endpoint, int activeSlots, int maxSlots, CancellationToken cancellationToken);
    Task MarkStaleTaskRuntimeRegistrationsOfflineAsync(TimeSpan threshold, CancellationToken cancellationToken);
    Task<List<TaskRuntimeDocument>> ListTaskRuntimesAsync(CancellationToken cancellationToken);
    Task<TaskRuntimeDocument> UpsertTaskRuntimeStateAsync(TaskRuntimeStateUpdate update, CancellationToken cancellationToken);
    Task<TaskRuntimeTelemetrySnapshot> GetTaskRuntimeTelemetryAsync(CancellationToken cancellationToken);

    Task<WebhookRegistration> CreateWebhookAsync(CreateWebhookRequest request, CancellationToken cancellationToken);
    Task<WebhookRegistration?> GetWebhookAsync(string webhookId, CancellationToken cancellationToken);
    Task<List<WebhookRegistration>> ListWebhooksAsync(string repositoryId, CancellationToken cancellationToken);
    Task<WebhookRegistration?> UpdateWebhookAsync(string webhookId, UpdateWebhookRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteWebhookAsync(string webhookId, CancellationToken cancellationToken);

    Task RecordProxyRequestAsync(ProxyAuditDocument audit, CancellationToken cancellationToken);
    Task<List<ProxyAuditDocument>> ListProxyAuditsAsync(string runId, CancellationToken cancellationToken);
    Task<List<ProxyAuditDocument>> ListProxyAuditsAsync(string? repoId, string? taskId, string? runId, int limit, CancellationToken cancellationToken);

    Task<SystemSettingsDocument> GetSettingsAsync(CancellationToken cancellationToken);
    Task<SystemSettingsDocument> UpdateSettingsAsync(SystemSettingsDocument settings, CancellationToken cancellationToken);
    Task<bool> TryAcquireLeaseAsync(string leaseName, string ownerId, TimeSpan ttl, CancellationToken cancellationToken);
    Task<bool> RenewLeaseAsync(string leaseName, string ownerId, TimeSpan ttl, CancellationToken cancellationToken);
    Task ReleaseLeaseAsync(string leaseName, string ownerId, CancellationToken cancellationToken);

    Task<WorkflowDocument> CreateWorkflowAsync(WorkflowDocument workflow, CancellationToken cancellationToken);
    Task<List<WorkflowDocument>> ListWorkflowsByRepositoryAsync(string repositoryId, CancellationToken cancellationToken);
    Task<List<WorkflowDocument>> ListAllWorkflowsAsync(CancellationToken cancellationToken);
    Task<WorkflowDocument?> GetWorkflowAsync(string workflowId, CancellationToken cancellationToken);
    Task<WorkflowDocument?> UpdateWorkflowAsync(string workflowId, WorkflowDocument workflow, CancellationToken cancellationToken);
    Task<bool> DeleteWorkflowAsync(string workflowId, CancellationToken cancellationToken);

    Task<WorkflowExecutionDocument> CreateWorkflowExecutionAsync(WorkflowExecutionDocument execution, CancellationToken cancellationToken);
    Task<List<WorkflowExecutionDocument>> ListWorkflowExecutionsAsync(string workflowId, CancellationToken cancellationToken);
    Task<List<WorkflowExecutionDocument>> ListWorkflowExecutionsByStateAsync(WorkflowExecutionState state, CancellationToken cancellationToken);
    Task<WorkflowExecutionDocument?> GetWorkflowExecutionAsync(string executionId, CancellationToken cancellationToken);
    Task<WorkflowExecutionDocument?> UpdateWorkflowExecutionAsync(WorkflowExecutionDocument execution, CancellationToken cancellationToken);
    Task<WorkflowExecutionDocument?> MarkWorkflowExecutionCompletedAsync(string executionId, WorkflowExecutionState finalState, string failureReason, CancellationToken cancellationToken);
    Task<WorkflowExecutionDocument?> MarkWorkflowExecutionPendingApprovalAsync(string executionId, string pendingApprovalStageId, CancellationToken cancellationToken);
    Task<WorkflowExecutionDocument?> ApproveWorkflowStageAsync(string executionId, string approvedBy, CancellationToken cancellationToken);
    Task<WorkflowExecutionDocument?> GetWorkflowExecutionByRunIdAsync(string runId, CancellationToken cancellationToken);
    Task<WorkflowDocument?> GetWorkflowForExecutionAsync(string workflowId, CancellationToken cancellationToken);

    Task<AlertRuleDocument> CreateAlertRuleAsync(AlertRuleDocument rule, CancellationToken cancellationToken);
    Task<List<AlertRuleDocument>> ListAlertRulesAsync(CancellationToken cancellationToken);
    Task<List<AlertRuleDocument>> ListEnabledAlertRulesAsync(CancellationToken cancellationToken);
    Task<AlertRuleDocument?> GetAlertRuleAsync(string ruleId, CancellationToken cancellationToken);
    Task<AlertRuleDocument?> UpdateAlertRuleAsync(string ruleId, AlertRuleDocument rule, CancellationToken cancellationToken);
    Task<bool> DeleteAlertRuleAsync(string ruleId, CancellationToken cancellationToken);

    Task<AlertEventDocument> RecordAlertEventAsync(AlertEventDocument alertEvent, CancellationToken cancellationToken);
    Task<AlertEventDocument?> GetAlertEventAsync(string eventId, CancellationToken cancellationToken);
    Task<List<AlertEventDocument>> ListRecentAlertEventsAsync(int limit, CancellationToken cancellationToken);
    Task<List<AlertEventDocument>> ListAlertEventsByRuleAsync(string ruleId, CancellationToken cancellationToken);
    Task<AlertEventDocument?> ResolveAlertEventAsync(string eventId, CancellationToken cancellationToken);
    Task<int> ResolveAlertEventsAsync(List<string> eventIds, CancellationToken cancellationToken);

    Task<ReliabilityMetrics> GetReliabilityMetricsAsync(CancellationToken cancellationToken);

}
