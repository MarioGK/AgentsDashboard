namespace AgentsDashboard.Contracts.Domain;

































































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
