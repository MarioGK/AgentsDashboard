namespace AgentsDashboard.Contracts.SignalR;

public sealed record RunToolTimelineUpdatedEvent(
    string RunId,
    long Sequence,
    string Category,
    string ToolName,
    string ToolCallId,
    string State,
    string Payload,
    string Schema,
    DateTime TimestampUtc)
{
}
