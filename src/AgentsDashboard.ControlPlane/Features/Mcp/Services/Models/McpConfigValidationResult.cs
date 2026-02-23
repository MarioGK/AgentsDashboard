namespace AgentsDashboard.ControlPlane.Features.Mcp.Services.Models;

public sealed record McpConfigValidationResult
{
    public bool IsValid { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public string FormattedJson { get; init; } = string.Empty;
    public IReadOnlyList<McpConfiguredServer> Servers { get; init; } = [];
}
