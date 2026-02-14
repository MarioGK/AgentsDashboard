using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;

public sealed record CreateProjectRequest(string Name, string Description);
public sealed record UpdateProjectRequest(string Name, string Description);
public sealed record CreateRepositoryRequest(string ProjectId, string Name, string GitUrl, string DefaultBranch);
public sealed record UpdateRepositoryRequest(string Name, string GitUrl, string DefaultBranch);
public sealed record CreateTaskRequest(
    string RepositoryId,
    string Name,
    TaskKind Kind,
    string Harness,
    string Prompt,
    string Command,
    bool AutoCreatePullRequest,
    string CronExpression,
    bool Enabled,
    RetryPolicyConfig? RetryPolicy = null,
    TimeoutConfig? Timeouts = null,
    SandboxProfileConfig? SandboxProfile = null,
    ArtifactPolicyConfig? ArtifactPolicy = null,
    ApprovalProfileConfig? ApprovalProfile = null,
    int? ConcurrencyLimit = null,
    List<InstructionFile>? InstructionFiles = null,
    List<string>? ArtifactPatterns = null,
    List<string>? LinkedFailureRuns = null);
public sealed record UpdateTaskRequest(
    string Name,
    TaskKind Kind,
    string Harness,
    string Prompt,
    string Command,
    bool AutoCreatePullRequest,
    string CronExpression,
    bool Enabled,
    RetryPolicyConfig? RetryPolicy = null,
    TimeoutConfig? Timeouts = null,
    SandboxProfileConfig? SandboxProfile = null,
    ArtifactPolicyConfig? ArtifactPolicy = null,
    ApprovalProfileConfig? ApprovalProfile = null,
    int? ConcurrencyLimit = null,
    List<InstructionFile>? InstructionFiles = null,
    List<string>? ArtifactPatterns = null,
    List<string>? LinkedFailureRuns = null);
public sealed record CreateRunRequest(string TaskId);
public sealed record RetryRunRequest(string RunId);
public sealed record CancelRunRequest(string RunId);
public sealed record UpdateFindingStateRequest(FindingState State);
public sealed record AssignFindingRequest(string AssignedTo);
public sealed record CreateTaskFromFindingRequest(string Name, string Harness, string Command, string Prompt, List<string>? LinkedFailureRuns = null);
public sealed record SetProviderSecretRequest(string SecretValue);
public sealed record CreateWebhookRequest(string RepositoryId, string TaskId, string EventFilter, string Secret);
public sealed record UpdateWebhookRequest(string TaskId, string EventFilter, string Secret, bool Enabled);
public sealed record WorkerHeartbeatRequest(string WorkerId, string? Endpoint, int ActiveSlots, int MaxSlots);
public sealed record UpdateSystemSettingsRequest(
    List<string>? DockerAllowedImages = null,
    int? RetentionDaysLogs = null,
    int? RetentionDaysRuns = null,
    string? VictoriaMetricsEndpoint = null,
    string? VmUiEndpoint = null);
public sealed record CreateWorkflowRequest(string RepositoryId, string Name, string Description, List<WorkflowStageConfigRequest> Stages);
public sealed record UpdateWorkflowRequest(string Name, string Description, List<WorkflowStageConfigRequest> Stages, bool Enabled);
public sealed record WorkflowStageConfigRequest(
    string Name,
    WorkflowStageType Type,
    string? TaskId = null,
    int? DelaySeconds = null,
    List<string>? ParallelStageIds = null,
    string? ApproverRole = null,
    int Order = 0);
public sealed record BuildImageRequest(string DockerfileContent, string Tag);
public sealed record CreateAlertRuleRequest(
    string Name,
    AlertRuleType RuleType,
    int Threshold,
    int WindowMinutes,
    string? WebhookUrl = null,
    bool Enabled = true,
    int CooldownMinutes = 15);
public sealed record UpdateAlertRuleRequest(
    string Name,
    AlertRuleType RuleType,
    int Threshold,
    int WindowMinutes,
    string? WebhookUrl = null,
    bool Enabled = true,
    int CooldownMinutes = 15);
public sealed record ExecuteWorkflowRequest(string WorkflowId);
public sealed record ApproveWorkflowStageRequest(string ApprovedBy);
public sealed record ValidateProviderRequest(string Provider, string SecretValue);
public sealed record UpdateRepositoryInstructionsRequest(List<InstructionFile> InstructionFiles);
public sealed record CreateRepositoryInstructionRequest(string Name, string Content, int Priority, bool Enabled = true);
public sealed record UpdateRepositoryInstructionRequest(string Name, string Content, int Priority, bool Enabled);
public sealed record UpdateHarnessProviderSettingsRequest(
    string Model,
    double Temperature,
    int MaxTokens,
    Dictionary<string, string>? AdditionalSettings = null);

public sealed record BulkCancelRunsRequest(List<string> RunIds);
public sealed record BulkResolveAlertsRequest(List<string> EventIds);
public sealed record BulkOperationResult(int AffectedCount, List<string> Errors);

public sealed record ReliabilityMetrics(
    double SuccessRate7Days,
    double SuccessRate30Days,
    int TotalRuns7Days,
    int TotalRuns30Days,
    Dictionary<string, int> RunsByState,
    List<DailyFailureCount> FailureTrend14Days,
    double? AverageDurationSeconds,
    List<ProjectReliabilityMetrics> PerProjectMetrics);

public sealed record DailyFailureCount(DateTime Date, int Count);

public sealed record ProjectReliabilityMetrics(
    string ProjectId,
    string ProjectName,
    int TotalRuns,
    int SuccessfulRuns,
    int FailedRuns,
    double SuccessRate);

public sealed record CreateTaskTemplateRequest(
    string TemplateId,
    string Name,
    string Description,
    TaskKind Kind,
    string Harness,
    string Prompt,
    List<string> Commands,
    string CronExpression,
    bool AutoCreatePullRequest,
    RetryPolicyConfig? RetryPolicy = null,
    TimeoutConfig? Timeouts = null,
    SandboxProfileConfig? SandboxProfile = null,
    ArtifactPolicyConfig? ArtifactPolicy = null);

public sealed record UpdateTaskTemplateRequest(
    string Name,
    string Description,
    TaskKind Kind,
    string Harness,
    string Prompt,
    List<string> Commands,
    string CronExpression,
    bool AutoCreatePullRequest,
    RetryPolicyConfig? RetryPolicy = null,
    TimeoutConfig? Timeouts = null,
    SandboxProfileConfig? SandboxProfile = null,
    ArtifactPolicyConfig? ArtifactPolicy = null);
