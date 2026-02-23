namespace AgentsDashboard.Contracts.Features.Realtime.Models.Events;

public sealed record RunStructuredEventChangedEvent(
    string RunId,
    long Sequence,
    string Category,
    string Payload,
    string Schema,
    DateTime TimestampUtc)
{
}
