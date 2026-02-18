namespace AgentsDashboard.Contracts.SignalR;

public sealed record RunStatusChangedEvent(
    string RunId,
    string State,
    string? Summary,
    DateTime? StartedAt,
    DateTime? EndedAt);

public sealed record RunLogChunkEvent(
    string RunId,
    string Level,
    string Message,
    DateTime Timestamp);

public sealed record RunStructuredEventChangedEvent(
    string RunId,
    long Sequence,
    string Category,
    string Payload,
    string Schema,
    DateTime Timestamp);

public sealed record RunDiffUpdatedEvent(
    string RunId,
    long Sequence,
    string Category,
    string Payload,
    string Schema,
    DateTime Timestamp);

public sealed record RunToolTimelineUpdatedEvent(
    string RunId,
    long Sequence,
    string Category,
    string ToolName,
    string ToolCallId,
    string State,
    string Payload,
    string Schema,
    DateTime Timestamp);

public sealed record FindingUpdatedEvent(
    string FindingId,
    string RepositoryId,
    string State,
    string Severity,
    string Title);

public sealed record TaskRuntimeHeartbeatEvent(
    string TaskRuntimeId,
    string HostName,
    int ActiveSlots,
    int MaxSlots,
    DateTime Timestamp);

public sealed record RouteAvailableEvent(
    string RunId,
    string RoutePath,
    DateTime Timestamp);
