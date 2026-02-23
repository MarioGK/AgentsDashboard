namespace AgentsDashboard.Contracts.SignalR;

public sealed record RunStatusChangedEvent(
    string RunId,
    string State,
    string Summary,
    DateTime? StartedAtUtc,
    DateTime? EndedAtUtc);

