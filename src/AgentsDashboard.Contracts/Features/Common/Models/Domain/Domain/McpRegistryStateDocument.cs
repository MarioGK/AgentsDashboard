namespace AgentsDashboard.Contracts.Domain;

public sealed class McpRegistryStateDocument
{
    public string Id { get; set; } = "singleton";
    public DateTime LastRefreshedAtUtc { get; set; }
    public string LastRefreshError { get; set; } = string.Empty;
    public int LastServerCount { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
