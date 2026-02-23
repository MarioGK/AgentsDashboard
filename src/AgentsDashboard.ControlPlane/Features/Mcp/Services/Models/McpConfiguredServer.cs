namespace AgentsDashboard.ControlPlane.Services;

public sealed record McpConfiguredServer
{
    public required string Key { get; init; }
    public string TransportType { get; init; } = string.Empty;
    public string Command { get; init; } = string.Empty;
    public IReadOnlyList<string> Args { get; init; } = [];
    public string Url { get; init; } = string.Empty;
}
