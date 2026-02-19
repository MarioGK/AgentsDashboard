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

public enum TaskRuntimeState
{
    Cold = 0,
    Starting = 1,
    Ready = 2,
    Busy = 3,
    Stopping = 4,
    Inactive = 5,
    Failed = 6
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
    OpenCode = 1
}

public enum HarnessExecutionMode
{
    Default = 0,
    Plan = 1,
    Review = 2
}

public enum RunSessionProfileScope
{
    Global = 0,
    Repository = 1
}

public enum TaskRuntimeImagePolicy
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





public enum WorkflowExecutionState
{
    Running = 0,
    Succeeded = 1,
    Failed = 2,
    Cancelled = 3,
    PendingApproval = 4
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
