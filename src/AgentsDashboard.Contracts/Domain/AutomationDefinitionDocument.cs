namespace AgentsDashboard.Contracts.Domain;

































































public sealed class AutomationDefinitionDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RepositoryId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string CronExpression { get; set; } = string.Empty;
    public string TriggerKind { get; set; } = "cron";
    public string ReplayPolicy { get; set; } = "skip";
    public bool Enabled { get; set; } = true;
    public DateTime? NextRunAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
