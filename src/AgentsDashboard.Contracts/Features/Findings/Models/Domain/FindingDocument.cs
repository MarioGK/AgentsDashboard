namespace AgentsDashboard.Contracts.Features.Findings.Models.Domain;

































































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
