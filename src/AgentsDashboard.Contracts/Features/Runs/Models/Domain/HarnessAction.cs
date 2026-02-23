namespace AgentsDashboard.Contracts.Features.Runs.Models.Domain;

































































public sealed class HarnessAction
{
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
}
