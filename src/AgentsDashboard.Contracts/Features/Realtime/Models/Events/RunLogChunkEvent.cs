namespace AgentsDashboard.Contracts.Features.Realtime.Models.Events;

public sealed record RunLogChunkEvent(
    string RunId,
    string Level,
    string Message,
    DateTime Timestamp)
{
}
