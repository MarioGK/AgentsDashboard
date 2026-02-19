namespace AgentsDashboard.Contracts.SignalR;

public sealed record TaskRuntimeHeartbeatEvent(
    string WorkerId,
    string HostName,
    int ActiveSlots,
    int MaxSlots,
    DateTime TimestampUtc)
{
}
