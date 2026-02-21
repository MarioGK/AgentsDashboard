namespace AgentsDashboard.Contracts.Domain;

































































public sealed class RepositoryInstructionDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RepositoryId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Content { get; set; } = "";
    public int Priority { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
