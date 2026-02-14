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

public sealed record FindingUpdatedEvent(
    string FindingId,
    string RepositoryId,
    string State,
    string Severity,
    string Title);

public sealed record WorkerHeartbeatEvent(
    string WorkerId,
    string HostName,
    int ActiveSlots,
    int MaxSlots,
    DateTime Timestamp);

public sealed record RouteAvailableEvent(
    string RunId,
    string RoutePath,
    DateTime Timestamp);

public sealed record WorkflowV2ExecutionStateChangedEvent(
    string ExecutionId,
    string WorkflowId,
    string State,
    string? CurrentNodeId,
    string? FailureReason,
    DateTime Timestamp);

public sealed record WorkflowV2NodeStateChangedEvent(
    string ExecutionId,
    string NodeId,
    string NodeName,
    string NodeState,
    string? RunId,
    string? Summary,
    DateTime Timestamp);
