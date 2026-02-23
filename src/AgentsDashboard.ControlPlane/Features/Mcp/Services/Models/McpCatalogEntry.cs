namespace AgentsDashboard.ControlPlane.Features.Mcp.Services.Models;

public sealed record McpCatalogEntry
{
    public required string Id { get; init; }
    public required string Source { get; init; }
    public required string ServerName { get; init; }
    public required string DisplayName { get; init; }
    public string Description { get; init; } = string.Empty;
    public string RepositoryUrl { get; init; } = string.Empty;
    public string RegistryType { get; init; } = string.Empty;
    public string PackageIdentifier { get; init; } = string.Empty;
    public string TransportType { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public IReadOnlyList<string> CommandArgs { get; init; } = [];
    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public string InstallCommand { get; init; } = string.Empty;
    public double Score { get; init; }
}
