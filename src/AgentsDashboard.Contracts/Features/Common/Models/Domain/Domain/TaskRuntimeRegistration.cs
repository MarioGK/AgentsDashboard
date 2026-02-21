namespace AgentsDashboard.Contracts.Domain;

































































public sealed class TaskRuntimeRegistration
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string RuntimeId { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public int MaxSlots { get; set; } = 4;
    public int ActiveSlots { get; set; }
    public bool Online { get; set; } = true;
    public DateTime LastHeartbeatUtc { get; set; } = DateTime.UtcNow;
    public DateTime RegisteredAtUtc { get; set; } = DateTime.UtcNow;
}
