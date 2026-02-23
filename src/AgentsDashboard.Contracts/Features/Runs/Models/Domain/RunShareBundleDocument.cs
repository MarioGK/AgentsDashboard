namespace AgentsDashboard.Contracts.Features.Runs.Models.Domain;

































































public sealed class RunShareBundleDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RunId { get; set; } = string.Empty;
    public string RepositoryId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string BundleJson { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
