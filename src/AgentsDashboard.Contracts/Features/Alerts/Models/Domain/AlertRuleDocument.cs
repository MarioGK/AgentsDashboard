namespace AgentsDashboard.Contracts.Features.Alerts.Models.Domain;

































































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
