namespace AgentsDashboard.Contracts.Features.Realtime.Models.Events;

public sealed record TaskRuntimeHeartbeatEvent(
    string WorkerId,
    string HostName,
    int ActiveSlots,
    int MaxSlots,
    DateTime TimestampUtc)
{
}
