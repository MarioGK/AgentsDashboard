namespace AgentsDashboard.Contracts.SignalR;

public sealed record RunLogChunkEvent(
    string RunId,
    string Level,
    string Message,
    DateTime Timestamp)
{
}
