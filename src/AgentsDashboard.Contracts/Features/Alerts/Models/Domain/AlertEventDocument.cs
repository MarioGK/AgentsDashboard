namespace AgentsDashboard.Contracts.Domain;

































































public sealed class AlertEventDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RuleId { get; set; } = string.Empty;
    public string RuleName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime FiredAtUtc { get; set; } = DateTime.UtcNow;
    public bool Resolved { get; set; }
}
