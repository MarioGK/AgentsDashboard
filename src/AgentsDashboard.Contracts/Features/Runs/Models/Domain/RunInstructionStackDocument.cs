namespace AgentsDashboard.Contracts.Features.Runs.Models.Domain;

































































public sealed class RunInstructionStackDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RunId { get; set; } = string.Empty;
    public string RepositoryId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string SessionProfileId { get; set; } = string.Empty;
    public string GlobalRules { get; set; } = string.Empty;
    public string RepositoryRules { get; set; } = string.Empty;
    public string TaskRules { get; set; } = string.Empty;
    public string RunOverrides { get; set; } = string.Empty;
    public string ResolvedText { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
