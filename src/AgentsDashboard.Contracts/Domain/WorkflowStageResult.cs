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











public sealed class WorkflowStageResult
{
    public string StageId { get; set; } = string.Empty;
    public string StageName { get; set; } = string.Empty;
    public WorkflowStageType StageType { get; set; }
    public bool Succeeded { get; set; }
    public string Summary { get; set; } = string.Empty;
    public List<string> RunIds { get; set; } = [];
    public WorkflowAgentTeamDiffResult? AgentTeamDiff { get; set; }
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAtUtc { get; set; }
}
