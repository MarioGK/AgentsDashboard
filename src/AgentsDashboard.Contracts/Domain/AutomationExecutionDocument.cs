namespace AgentsDashboard.Contracts.Domain;

































































public sealed class AutomationExecutionDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AutomationDefinitionId { get; set; } = string.Empty;
    public string RepositoryId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string TriggeredBy { get; set; } = "scheduler";
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
}
