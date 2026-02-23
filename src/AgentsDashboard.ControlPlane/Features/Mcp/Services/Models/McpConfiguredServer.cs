namespace AgentsDashboard.ControlPlane.Features.Mcp.Services.Models;

public sealed record McpConfiguredServer
{
    public required string Key { get; init; }
    public string TransportType { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public IReadOnlyList<string> Args { get; init; } = [];
    public string Url { get; init; } = string.Empty;
}
