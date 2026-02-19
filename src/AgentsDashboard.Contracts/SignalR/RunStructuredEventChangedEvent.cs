namespace AgentsDashboard.Contracts.SignalR;

public sealed record RunStructuredEventChangedEvent(
    string RunId,
    long Sequence,
    string Category,
    string Payload,
    string Schema,
    DateTime TimestampUtc)
{
}
