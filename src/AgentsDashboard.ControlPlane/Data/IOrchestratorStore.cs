using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.ControlPlane.Data;

public interface IOrchestratorStore
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<ProjectDocument> CreateProjectAsync(CreateProjectRequest request, CancellationToken cancellationToken);
    Task<List<ProjectDocument>> ListProjectsAsync(CancellationToken cancellationToken);
    Task<ProjectDocument?> GetProjectAsync(string projectId, CancellationToken cancellationToken);
    Task<ProjectDocument?> UpdateProjectAsync(string projectId, UpdateProjectRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteProjectAsync(string projectId, CancellationToken cancellationToken);

    Task<RepositoryDocument> CreateRepositoryAsync(CreateRepositoryRequest request, CancellationToken cancellationToken);
    Task<List<RepositoryDocument>> ListRepositoriesAsync(string projectId, CancellationToken cancellationToken);
    Task<RepositoryDocument?> GetRepositoryAsync(string repositoryId, CancellationToken cancellationToken);
    Task<RepositoryDocument?> UpdateRepositoryAsync(string repositoryId, UpdateRepositoryRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteRepositoryAsync(string repositoryId, CancellationToken cancellationToken);
    Task<List<InstructionFile>> GetRepositoryInstructionFilesAsync(string repositoryId, CancellationToken cancellationToken);
    Task<RepositoryDocument?> UpdateRepositoryInstructionFilesAsync(string repositoryId, List<InstructionFile> instructionFiles, CancellationToken cancellationToken);
    Task<List<RepositoryInstructionDocument>> GetInstructionsAsync(string repositoryId, CancellationToken cancellationToken);
    Task<RepositoryInstructionDocument?> GetInstructionAsync(string instructionId, CancellationToken cancellationToken);
    Task<RepositoryInstructionDocument> UpsertInstructionAsync(string repositoryId, string? instructionId, CreateRepositoryInstructionRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteInstructionAsync(string instructionId, CancellationToken cancellationToken);
    Task<HarnessProviderSettingsDocument?> GetHarnessProviderSettingsAsync(string repositoryId, string harness, CancellationToken cancellationToken);
    Task<HarnessProviderSettingsDocument> UpsertHarnessProviderSettingsAsync(string repositoryId, string harness, string model, double temperature, int maxTokens, Dictionary<string, string>? additionalSettings, CancellationToken cancellationToken);

    Task<TaskDocument> CreateTaskAsync(CreateTaskRequest request, CancellationToken cancellationToken);
    Task<List<TaskDocument>> ListTasksAsync(string repositoryId, CancellationToken cancellationToken);
    Task<List<TaskDocument>> ListEventDrivenTasksAsync(string repositoryId, CancellationToken cancellationToken);
    Task<TaskDocument?> GetTaskAsync(string taskId, CancellationToken cancellationToken);
    Task<List<TaskDocument>> ListScheduledTasksAsync(CancellationToken cancellationToken);
    Task<List<TaskDocument>> ListDueTasksAsync(DateTime utcNow, int limit, CancellationToken cancellationToken);
    Task MarkOneShotTaskConsumedAsync(string taskId, CancellationToken cancellationToken);
    Task UpdateTaskNextRunAsync(string taskId, DateTime? nextRunAtUtc, CancellationToken cancellationToken);
    Task<TaskDocument?> UpdateTaskAsync(string taskId, UpdateTaskRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteTaskAsync(string taskId, CancellationToken cancellationToken);

    Task<RunDocument> CreateRunAsync(TaskDocument task, string projectId, CancellationToken cancellationToken, int attempt = 1);
    Task<List<RunDocument>> ListRunsByRepositoryAsync(string repositoryId, CancellationToken cancellationToken);
    Task<List<RunDocument>> ListRecentRunsAsync(CancellationToken cancellationToken);
    Task<List<RunDocument>> ListRecentRunsByProjectAsync(string projectId, CancellationToken cancellationToken);
    Task<ReliabilityMetrics> GetReliabilityMetricsByProjectAsync(string projectId, CancellationToken cancellationToken);
    Task<ReliabilityMetrics> GetReliabilityMetricsByRepositoryAsync(string repositoryId, CancellationToken cancellationToken);
    Task<RunDocument?> GetRunAsync(string runId, CancellationToken cancellationToken);
    Task<List<RunDocument>> ListRunsByStateAsync(RunState state, CancellationToken cancellationToken);
    Task<List<string>> ListAllRunIdsAsync(CancellationToken cancellationToken);
    Task<long> CountActiveRunsAsync(CancellationToken cancellationToken);
    Task<long> CountActiveRunsByProjectAsync(string projectId, CancellationToken cancellationToken);
    Task<long> CountActiveRunsByRepoAsync(string repositoryId, CancellationToken cancellationToken);
    Task<long> CountActiveRunsByTaskAsync(string taskId, CancellationToken cancellationToken);
    Task<RunDocument?> MarkRunStartedAsync(string runId, CancellationToken cancellationToken);
    Task<RunDocument?> MarkRunCompletedAsync(string runId, bool succeeded, string summary, string outputJson, CancellationToken cancellationToken, string? failureClass = null, string? prUrl = null);
    Task<RunDocument?> MarkRunCancelledAsync(string runId, CancellationToken cancellationToken);
    Task<RunDocument?> MarkRunPendingApprovalAsync(string runId, CancellationToken cancellationToken);
    Task<RunDocument?> ApproveRunAsync(string runId, CancellationToken cancellationToken);
    Task<RunDocument?> RejectRunAsync(string runId, CancellationToken cancellationToken);
    Task<int> BulkCancelRunsAsync(List<string> runIds, CancellationToken cancellationToken);

    Task SaveArtifactAsync(string runId, string fileName, Stream stream, CancellationToken cancellationToken);
    Task<List<string>> ListArtifactsAsync(string runId, CancellationToken cancellationToken);
    Task<FileStream?> GetArtifactAsync(string runId, string fileName, CancellationToken cancellationToken);

    Task AddRunLogAsync(RunLogEvent logEvent, CancellationToken cancellationToken);
    Task<List<RunLogEvent>> ListRunLogsAsync(string runId, CancellationToken cancellationToken);

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

    Task<List<WorkerRegistration>> ListWorkersAsync(CancellationToken cancellationToken);
    Task UpsertWorkerHeartbeatAsync(string workerId, string endpoint, int activeSlots, int maxSlots, CancellationToken cancellationToken);
    Task MarkStaleWorkersOfflineAsync(TimeSpan threshold, CancellationToken cancellationToken);

    Task<WebhookRegistration> CreateWebhookAsync(CreateWebhookRequest request, CancellationToken cancellationToken);
    Task<WebhookRegistration?> GetWebhookAsync(string webhookId, CancellationToken cancellationToken);
    Task<List<WebhookRegistration>> ListWebhooksAsync(string repositoryId, CancellationToken cancellationToken);
    Task<WebhookRegistration?> UpdateWebhookAsync(string webhookId, UpdateWebhookRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteWebhookAsync(string webhookId, CancellationToken cancellationToken);

    Task RecordProxyRequestAsync(ProxyAuditDocument audit, CancellationToken cancellationToken);
    Task<List<ProxyAuditDocument>> ListProxyAuditsAsync(string runId, CancellationToken cancellationToken);
    Task<List<ProxyAuditDocument>> ListProxyAuditsAsync(string? projectId, string? repoId, string? taskId, string? runId, int limit, CancellationToken cancellationToken);

    Task<SystemSettingsDocument> GetSettingsAsync(CancellationToken cancellationToken);
    Task<SystemSettingsDocument> UpdateSettingsAsync(SystemSettingsDocument settings, CancellationToken cancellationToken);

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

    // ── Graph Agent Workflow (V2) ────────────────────────────────────────

    Task<AgentDocument> CreateAgentAsync(AgentDocument agent, CancellationToken cancellationToken);
    Task<List<AgentDocument>> ListAgentsByRepositoryAsync(string repositoryId, CancellationToken cancellationToken);
    Task<List<AgentDocument>> ListAllAgentsAsync(CancellationToken cancellationToken);
    Task<AgentDocument?> GetAgentAsync(string agentId, CancellationToken cancellationToken);
    Task<AgentDocument?> UpdateAgentAsync(string agentId, AgentDocument agent, CancellationToken cancellationToken);
    Task<bool> DeleteAgentAsync(string agentId, CancellationToken cancellationToken);

    Task<WorkflowV2Document> CreateWorkflowV2Async(WorkflowV2Document workflow, CancellationToken cancellationToken);
    Task<List<WorkflowV2Document>> ListWorkflowsV2ByRepositoryAsync(string repositoryId, CancellationToken cancellationToken);
    Task<List<WorkflowV2Document>> ListAllWorkflowsV2Async(CancellationToken cancellationToken);
    Task<WorkflowV2Document?> GetWorkflowV2Async(string workflowId, CancellationToken cancellationToken);
    Task<WorkflowV2Document?> UpdateWorkflowV2Async(string workflowId, WorkflowV2Document workflow, CancellationToken cancellationToken);
    Task<bool> DeleteWorkflowV2Async(string workflowId, CancellationToken cancellationToken);

    Task<WorkflowExecutionV2Document> CreateExecutionV2Async(WorkflowExecutionV2Document execution, CancellationToken cancellationToken);
    Task<List<WorkflowExecutionV2Document>> ListExecutionsV2ByWorkflowAsync(string workflowId, CancellationToken cancellationToken);
    Task<List<WorkflowExecutionV2Document>> ListExecutionsV2ByStateAsync(WorkflowV2ExecutionState state, CancellationToken cancellationToken);
    Task<WorkflowExecutionV2Document?> GetExecutionV2Async(string executionId, CancellationToken cancellationToken);
    Task<WorkflowExecutionV2Document?> UpdateExecutionV2Async(WorkflowExecutionV2Document execution, CancellationToken cancellationToken);
    Task<WorkflowExecutionV2Document?> MarkExecutionV2CompletedAsync(string executionId, WorkflowV2ExecutionState finalState, string failureReason, CancellationToken cancellationToken);
    Task<WorkflowExecutionV2Document?> MarkExecutionV2PendingApprovalAsync(string executionId, string pendingNodeId, CancellationToken cancellationToken);
    Task<WorkflowExecutionV2Document?> ApproveExecutionV2NodeAsync(string executionId, string approvedBy, CancellationToken cancellationToken);
    Task<WorkflowExecutionV2Document?> GetExecutionV2ByRunIdAsync(string runId, CancellationToken cancellationToken);

    Task<WorkflowDeadLetterDocument> CreateDeadLetterAsync(WorkflowDeadLetterDocument deadLetter, CancellationToken cancellationToken);
    Task<List<WorkflowDeadLetterDocument>> ListDeadLettersByExecutionAsync(string executionId, CancellationToken cancellationToken);
    Task<List<WorkflowDeadLetterDocument>> ListUnreplayedDeadLettersAsync(CancellationToken cancellationToken);
    Task<WorkflowDeadLetterDocument?> GetDeadLetterAsync(string deadLetterId, CancellationToken cancellationToken);
    Task<WorkflowDeadLetterDocument?> MarkDeadLetterReplayedAsync(string deadLetterId, string replayedExecutionId, CancellationToken cancellationToken);

    // ── Terminal Sessions ─────────────────────────────────────────────────────

    Task<TerminalSessionDocument> CreateTerminalSessionAsync(TerminalSessionDocument session, CancellationToken cancellationToken = default);
    Task<TerminalSessionDocument?> GetTerminalSessionAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TerminalSessionDocument>> ListActiveTerminalSessionsByWorkerAsync(string workerId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TerminalSessionDocument>> ListTerminalSessionsByRunAsync(string runId, CancellationToken cancellationToken = default);
    Task UpdateTerminalSessionAsync(TerminalSessionDocument session, CancellationToken cancellationToken = default);
    Task CloseTerminalSessionAsync(string sessionId, string reason, CancellationToken cancellationToken = default);

    // ── Terminal Audit Events ────────────────────────────────────────────────

    Task AppendTerminalAuditEventAsync(TerminalAuditEventDocument auditEvent, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TerminalAuditEventDocument>> GetTerminalAuditEventsAsync(string sessionId, long? afterSequence = null, int limit = 2000, CancellationToken cancellationToken = default);
}
