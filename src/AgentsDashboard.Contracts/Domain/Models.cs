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
    PendingApproval = 5
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

public sealed record ApprovalProfileConfig(bool RequireApproval = false, string ApproverRole = "operator");

public sealed record SandboxProfileConfig(
    double CpuLimit = 1.5,
    string MemoryLimit = "2g",
    bool NetworkDisabled = false,
    bool ReadOnlyRootFs = false);

public sealed record ArtifactPolicyConfig(int MaxArtifacts = 50, long MaxTotalSizeBytes = 104_857_600);

public sealed class ProjectDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class RepositoryDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProjectId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string GitUrl { get; set; } = string.Empty;
    public string DefaultBranch { get; set; } = "main";
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
    public string ProjectId { get; set; } = string.Empty;
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
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
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

public sealed class ProxyAuditDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ProjectId { get; set; } = string.Empty;
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
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
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
    public string? ApproverRole { get; set; }
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
    public string ProjectId { get; set; } = string.Empty;
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

// ── Graph Agent Workflow (V2) ────────────────────────────────────────

public enum WorkflowNodeType
{
    Start = 0,
    Agent = 1,
    Delay = 2,
    Approval = 3,
    End = 4
}

public enum WorkflowV2ExecutionState
{
    Running = 0,
    Succeeded = 1,
    Failed = 2,
    Cancelled = 3,
    PendingApproval = 4
}

public enum WorkflowNodeState
{
    Pending = 0,
    Running = 1,
    Succeeded = 2,
    Failed = 3,
    Skipped = 4,
    TimedOut = 5,
    DeadLettered = 6
}

public sealed class AgentDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RepositoryId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Harness { get; set; } = "codex";
    public string Prompt { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public bool AutoCreatePullRequest { get; set; }
    public RetryPolicyConfig RetryPolicy { get; set; } = new();
    public TimeoutConfig Timeouts { get; set; } = new();
    public SandboxProfileConfig SandboxProfile { get; set; } = new();
    public ArtifactPolicyConfig ArtifactPolicy { get; set; } = new();
    public List<string> ArtifactPatterns { get; set; } = [];
    public List<InstructionFile> InstructionFiles { get; set; } = [];
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class WorkflowNodeConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public WorkflowNodeType Type { get; set; }
    public string? AgentId { get; set; }
    public int? DelaySeconds { get; set; }
    public string? ApproverRole { get; set; }
    public int? TimeoutMinutes { get; set; }
    public RetryPolicyConfig? RetryPolicy { get; set; }
    public Dictionary<string, string> InputMappings { get; set; } = [];
    public Dictionary<string, string> OutputMappings { get; set; } = [];
    public double PositionX { get; set; }
    public double PositionY { get; set; }
}

public sealed class WorkflowEdgeConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SourceNodeId { get; set; } = string.Empty;
    public string TargetNodeId { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public int Priority { get; set; }
    public string Label { get; set; } = string.Empty;
}

public sealed class WorkflowV2TriggerConfig
{
    public string Type { get; set; } = "Manual";
    public string CronExpression { get; set; } = string.Empty;
    public string WebhookEventFilter { get; set; } = string.Empty;
}

public sealed class WorkflowV2Document
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RepositoryId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<WorkflowNodeConfig> Nodes { get; set; } = [];
    public List<WorkflowEdgeConfig> Edges { get; set; } = [];
    public WorkflowV2TriggerConfig Trigger { get; set; } = new();
    public string WebhookToken { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int MaxConcurrentNodes { get; set; } = 4;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public sealed class WorkflowNodeResult
{
    public string NodeId { get; set; } = string.Empty;
    public string NodeName { get; set; } = string.Empty;
    public WorkflowNodeType NodeType { get; set; }
    public WorkflowNodeState State { get; set; }
    public string? RunId { get; set; }
    public string Summary { get; set; } = string.Empty;
    public int Attempt { get; set; } = 1;
    public Dictionary<string, System.Text.Json.JsonElement> OutputContext { get; set; } = [];
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
}

public sealed class WorkflowExecutionV2Document
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string WorkflowV2Id { get; set; } = string.Empty;
    public string RepositoryId { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public WorkflowV2ExecutionState State { get; set; } = WorkflowV2ExecutionState.Running;
    public string CurrentNodeId { get; set; } = string.Empty;
    public Dictionary<string, System.Text.Json.JsonElement> Context { get; set; } = [];
    public List<WorkflowNodeResult> NodeResults { get; set; } = [];
    public string PendingApprovalNodeId { get; set; } = string.Empty;
    public string ApprovedBy { get; set; } = string.Empty;
    public string FailureReason { get; set; } = string.Empty;
    public string TriggeredBy { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
}

public sealed class WorkflowDeadLetterDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string ExecutionId { get; set; } = string.Empty;
    public string WorkflowV2Id { get; set; } = string.Empty;
    public string FailedNodeId { get; set; } = string.Empty;
    public string FailedNodeName { get; set; } = string.Empty;
    public string FailureReason { get; set; } = string.Empty;
    public Dictionary<string, System.Text.Json.JsonElement> InputContextSnapshot { get; set; } = [];
    public string? RunId { get; set; }
    public int Attempt { get; set; } = 1;
    public bool Replayed { get; set; }
    public string? ReplayedExecutionId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReplayedAtUtc { get; set; }
}
