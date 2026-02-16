namespace AgentsDashboard.Contracts.Domain;

public enum TaskKind
{
    OneShot = 0,
    Cron = 1,
    EventDriven = 2
}

public enum RunState
{
    Queued = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Cancelled = 4,
    PendingApproval = 5,
    Obsolete = 6
}

public enum FindingSeverity
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3
}

public enum FindingState
{
    New = 0,
    Acknowledged = 1,
    InProgress = 2,
    Resolved = 3,
    Ignored = 4
}

public enum HarnessType
{
    Codex = 0,
    OpenCode = 1,
    ClaudeCode = 2,
    Zai = 3
}

public enum WorkerImagePolicy
{
    PullOnly = 0,
    BuildOnly = 1,
    PreferLocal = 2,
    PullThenBuild = 3,
    BuildThenPull = 4
}

public enum ContainerUnhealthyAction
{
    Restart = 0,
    Recreate = 1,
    Quarantine = 2
}

public enum WebhookEventType
{
    Push = 0,
    PullRequest = 1,
    Issues = 2,
    Release = 3,
    WorkflowRun = 4,
    Any = 99
}

public sealed class ContainerMetrics
{
    public string ContainerId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public double CpuPercent { get; set; }
    public long MemoryUsageBytes { get; set; }
    public long MemoryLimitBytes { get; set; }
    public double MemoryPercent { get; set; }
    public long NetworkRxBytes { get; set; }
    public long NetworkTxBytes { get; set; }
    public long BlockReadBytes { get; set; }
    public long BlockWriteBytes { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ArtifactDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RunId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public string ArtifactType { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed record RetryPolicyConfig(int MaxAttempts = 1, int BackoffBaseSeconds = 10, double BackoffMultiplier = 2.0);

public sealed record TimeoutConfig(int ExecutionSeconds = 600, int OverallSeconds = 1800);

public sealed record ApprovalProfileConfig(bool RequireApproval = false);

public sealed record SandboxProfileConfig(
    double CpuLimit = 1.5,
    string MemoryLimit = "2g",
    bool NetworkDisabled = false,
    bool ReadOnlyRootFs = false);

public sealed record ArtifactPolicyConfig(int MaxArtifacts = 50, long MaxTotalSizeBytes = 104_857_600);

public class RepositoryDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string GitUrl { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public string DefaultBranch { get; set; } = "main";
    public string CurrentBranch { get; set; } = string.Empty;
    public string CurrentCommit { get; set; } = string.Empty;
    public int AheadCount { get; set; }
    public int BehindCount { get; set; }
    public int ModifiedCount { get; set; }
    public int StagedCount { get; set; }
    public int UntrackedCount { get; set; }
    public DateTime? LastScannedAtUtc { get; set; }
    public DateTime? LastFetchedAtUtc { get; set; }
    public DateTime? LastCloneAtUtc { get; set; }
    public DateTime? LastViewedAtUtc { get; set; }
    public string LastSyncError { get; set; } = string.Empty;
    public List<InstructionFile> InstructionFiles { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class TaskDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RepositoryId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public TaskKind Kind { get; set; }
    public string Harness { get; set; } = "codex";
    public string Prompt { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public bool AutoCreatePullRequest { get; set; }
    public string CronExpression { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DateTime? NextRunAtUtc { get; set; }
    public string WorktreePath { get; set; } = string.Empty;
    public string WorktreeBranch { get; set; } = string.Empty;
    public DateTime? LastGitSyncAtUtc { get; set; }
    public string LastGitSyncError { get; set; } = string.Empty;
    public RetryPolicyConfig RetryPolicy { get; set; } = new();
    public TimeoutConfig Timeouts { get; set; } = new();
    public ApprovalProfileConfig ApprovalProfile { get; set; } = new();
    public SandboxProfileConfig SandboxProfile { get; set; } = new();
    public ArtifactPolicyConfig ArtifactPolicy { get; set; } = new();
    public List<string> ArtifactPatterns { get; set; } = [];
    public List<string> LinkedFailureRuns { get; set; } = [];
    public int ConcurrencyLimit { get; set; } = 1;
    public List<InstructionFile> InstructionFiles { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class RunDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RepositoryId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string WorkerId { get; set; } = string.Empty;
    public RunState State { get; set; } = RunState.Queued;
    public string Summary { get; set; } = string.Empty;
    public string OutputJson { get; set; } = string.Empty;
    public int Attempt { get; set; } = 1;
    public string ResultEnvelopeRef { get; set; } = string.Empty;
    public string FailureClass { get; set; } = string.Empty;
    public string PrUrl { get; set; } = string.Empty;
    public string WorkerImageRef { get; set; } = string.Empty;
    public string WorkerImageDigest { get; set; } = string.Empty;
    public string WorkerImageSource { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
}

public sealed class WorkspacePromptEntryDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RepositoryId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class SemanticChunkDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RepositoryId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string ChunkKey { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public string SourceRef { get; set; } = string.Empty;
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public int TokenCount { get; set; }
    public string EmbeddingModel { get; set; } = string.Empty;
    public int EmbeddingDimensions { get; set; }
    public string EmbeddingPayload { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class RunAiSummaryDocument
{
    public string RunId { get; set; } = string.Empty;
    public string RepositoryId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string SourceFingerprint { get; set; } = string.Empty;
    public DateTime SourceUpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAtUtc { get; set; }
}

public sealed class FindingDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RepositoryId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public FindingSeverity Severity { get; set; } = FindingSeverity.Medium;
    public FindingState State { get; set; } = FindingState.New;
    public string AssignedTo { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class RunLogEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RunId { get; set; } = string.Empty;
    public string Level { get; set; } = "info";
    public string Message { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}

public sealed class ProviderSecretDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RepositoryId { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string EncryptedValue { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class WorkerRegistration
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string WorkerId { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public int MaxSlots { get; set; } = 4;
    public int ActiveSlots { get; set; }
    public bool Online { get; set; } = true;
    public DateTime LastHeartbeatUtc { get; set; } = DateTime.UtcNow;
    public DateTime RegisteredAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class WebhookRegistration
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RepositoryId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string EventFilter { get; set; } = "*";
    public string Secret { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class HarnessResultEnvelope
{
    public string RunId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string Status { get; set; } = "unknown";
    public string Summary { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public List<string> Artifacts { get; set; } = [];
    public List<HarnessAction> Actions { get; set; } = [];
    public Dictionary<string, double> Metrics { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = [];
    public string RawOutputRef { get; set; } = string.Empty;
}

public sealed class HarnessAction
{
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
}

public sealed record InstructionFile(string Name, string Content, int Order);
public sealed record RepositoryGitStatus(
    string CurrentBranch,
    string CurrentCommit,
    int AheadCount,
    int BehindCount,
    int ModifiedCount,
    int StagedCount,
    int UntrackedCount,
    DateTime ScannedAtUtc,
    DateTime? FetchedAtUtc,
    string LastSyncError = "");

public sealed class ProxyAuditDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RepoId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string UpstreamTarget { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public double LatencyMs { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}

public sealed class SystemSettingsDocument
{
    public string Id { get; set; } = "singleton";
    public List<string> DockerAllowedImages { get; set; } = [];
    public int RetentionDaysLogs { get; set; } = 30;
    public int RetentionDaysRuns { get; set; } = 90;
    public string VictoriaMetricsEndpoint { get; set; } = "http://localhost:8428";
    public string VmUiEndpoint { get; set; } = "http://localhost:8081";
    public OrchestratorSettings Orchestrator { get; set; } = new();
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class OrchestratorSettings
{
    public int MinWorkers { get; set; } = 4;
    public int MaxWorkers { get; set; } = 100;
    public int MaxProcessesPerWorker { get; set; } = 1;
    public int ReserveWorkers { get; set; } = 0;
    public int MaxQueueDepth { get; set; } = 200;
    public int QueueWaitTimeoutSeconds { get; set; } = 300;
    public string TaskPromptPrefix { get; set; } = string.Empty;
    public string TaskPromptSuffix { get; set; } = string.Empty;

    public WorkerImagePolicy WorkerImagePolicy { get; set; } = WorkerImagePolicy.PreferLocal;
    public string WorkerImageRegistry { get; set; } = string.Empty;
    public string WorkerCanaryImage { get; set; } = string.Empty;
    public string WorkerDockerBuildContextPath { get; set; } = string.Empty;
    public string WorkerDockerfilePath { get; set; } = string.Empty;
    public int MaxConcurrentPulls { get; set; } = 2;
    public int MaxConcurrentBuilds { get; set; } = 1;
    public int ImagePullTimeoutSeconds { get; set; } = 120;
    public int ImageBuildTimeoutSeconds { get; set; } = 600;
    public int WorkerImageCacheTtlMinutes { get; set; } = 240;
    public int ImageFailureCooldownMinutes { get; set; } = 15;
    public int CanaryPercent { get; set; } = 10;

    public int MaxWorkerStartAttemptsPer10Min { get; set; } = 30;
    public int MaxFailedStartsPer10Min { get; set; } = 10;
    public int CooldownMinutes { get; set; } = 15;
    public int ContainerStartTimeoutSeconds { get; set; } = 60;
    public int ContainerStopTimeoutSeconds { get; set; } = 30;
    public int HealthProbeIntervalSeconds { get; set; } = 10;
    public int ContainerRestartLimit { get; set; } = 3;
    public ContainerUnhealthyAction ContainerUnhealthyAction { get; set; } = ContainerUnhealthyAction.Recreate;
    public int OrchestratorErrorBurstThreshold { get; set; } = 20;
    public int OrchestratorErrorCoolDownMinutes { get; set; } = 10;

    public bool EnableDraining { get; set; } = true;
    public int DrainTimeoutSeconds { get; set; } = 120;
    public bool EnableAutoRecycle { get; set; } = true;
    public int RecycleAfterRuns { get; set; } = 200;
    public int RecycleAfterUptimeMinutes { get; set; } = 720;
    public bool EnableContainerAutoCleanup { get; set; } = true;

    public string WorkerCpuLimit { get; set; } = string.Empty;
    public int WorkerMemoryLimitMb { get; set; } = 0;
    public int WorkerPidsLimit { get; set; } = 0;
    public int WorkerFileDescriptorLimit { get; set; } = 0;
    public int RunHardTimeoutSeconds { get; set; } = 3600;
    public int MaxRunLogMb { get; set; } = 50;
}

public sealed class OrchestratorLeaseDocument
{
    public string LeaseName { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}

public enum AlertRuleType
{
    MissingHeartbeat = 0,
    FailureRateSpike = 1,
    QueueBacklog = 2,
    RepeatedPrFailures = 3,
    RouteLeakDetection = 4
}

public enum WorkflowStageType
{
    Task = 0,
    Approval = 1,
    Delay = 2,
    Parallel = 3,
}

public sealed class WorkflowStageConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public WorkflowStageType Type { get; set; }
    public string? TaskId { get; set; }
    public int? DelaySeconds { get; set; }
    public List<string>? ParallelStageIds { get; set; }
    public int? TimeoutMinutes { get; set; }
    public int Order { get; set; }
}

public sealed class WorkflowDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RepositoryId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<WorkflowStageConfig> Stages { get; set; } = [];
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public enum WorkflowExecutionState
{
    Running = 0,
    Succeeded = 1,
    Failed = 2,
    Cancelled = 3,
    PendingApproval = 4
}

public sealed class WorkflowStageResult
{
    public string StageId { get; set; } = string.Empty;
    public string StageName { get; set; } = string.Empty;
    public WorkflowStageType StageType { get; set; }
    public bool Succeeded { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<string> RunIds { get; set; } = [];
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAtUtc { get; set; }
}

public sealed class WorkflowExecutionDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string WorkflowId { get; set; } = string.Empty;
    public string RepositoryId { get; set; } = string.Empty;
    public WorkflowExecutionState State { get; set; } = WorkflowExecutionState.Running;
    public int CurrentStageIndex { get; set; }
    public List<WorkflowStageResult> StageResults { get; set; } = [];
    public string PendingApprovalStageId { get; set; } = string.Empty;
    public string ApprovedBy { get; set; } = string.Empty;
    public string FailureReason { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
}

public sealed class AlertRuleDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public AlertRuleType RuleType { get; set; }
    public int Threshold { get; set; }
    public int WindowMinutes { get; set; } = 10;
    public int CooldownMinutes { get; set; } = 15;
    public string WebhookUrl { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DateTime? LastFiredAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class AlertEventDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RuleId { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime FiredAtUtc { get; set; } = DateTime.UtcNow;
    public bool Resolved { get; set; }
}

public sealed class TaskTemplateDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string TemplateId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Harness { get; set; } = "any";
    public string Prompt { get; set; } = string.Empty;
    public List<string> Commands { get; set; } = [];
    public TaskKind Kind { get; set; } = TaskKind.OneShot;
    public bool AutoCreatePullRequest { get; set; }
    public string CronExpression { get; set; } = string.Empty;
    public RetryPolicyConfig RetryPolicy { get; set; } = new();
    public TimeoutConfig Timeouts { get; set; } = new();
    public SandboxProfileConfig SandboxProfile { get; set; } = new();
    public ArtifactPolicyConfig ArtifactPolicy { get; set; } = new();
    public List<string> ArtifactPatterns { get; set; } = [];
    public List<string> LinkedFailureRuns { get; set; } = [];
    public bool IsBuiltIn { get; set; } = true;
    public bool IsEditable { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class PromptSkillDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RepositoryId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Trigger { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class RepositoryInstructionDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RepositoryId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Content { get; set; } = "";
    public int Priority { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class HarnessProviderSettingsDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RepositoryId { get; set; } = string.Empty;
    public string Harness { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public double Temperature { get; set; } = 0.7;
    public int MaxTokens { get; set; } = 4096;
    public Dictionary<string, string> AdditionalSettings { get; set; } = [];
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
