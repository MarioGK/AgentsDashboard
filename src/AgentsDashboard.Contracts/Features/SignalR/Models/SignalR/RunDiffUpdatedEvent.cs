namespace AgentsDashboard.Contracts.SignalR;

public sealed record RunDiffUpdatedEvent(
    string RunId,
    long Sequence,
    string Category,
    string Payload,
    string Schema,
    DateTime TimestampUtc)
{
}
