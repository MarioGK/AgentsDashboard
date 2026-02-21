namespace AgentsDashboard.Contracts.Domain;

































































public sealed class TaskDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RepositoryId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public TaskKind Kind { get; set; }
    public string Harness { get; set; } = "codex";
    public HarnessExecutionMode? ExecutionModeDefault { get; set; }
    public string SessionProfileId { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public bool AutoCreatePullRequest { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime? NextRunAtUtc { get; set; }
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
