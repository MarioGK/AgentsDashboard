namespace AgentsDashboard.Contracts.Domain;

































































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
