namespace AgentsDashboard.Contracts.Domain;

public sealed class McpRegistryServerDocument
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Source { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string RepositoryUrl { get; set; } = string.Empty;
    public string RegistryType { get; set; } = string.Empty;
    public string PackageIdentifier { get; set; } = string.Empty;
    public string TransportType { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public List<string> CommandArgs { get; set; } = [];
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string InstallCommand { get; set; } = string.Empty;
    public double Score { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
