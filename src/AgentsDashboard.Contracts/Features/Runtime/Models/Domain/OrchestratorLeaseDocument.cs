namespace AgentsDashboard.Contracts.Features.Runtime.Models.Domain;

































































public sealed class OrchestratorLeaseDocument
{
    public string LeaseName { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}
