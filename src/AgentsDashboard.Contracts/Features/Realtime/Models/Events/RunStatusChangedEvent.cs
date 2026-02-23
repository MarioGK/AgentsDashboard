namespace AgentsDashboard.Contracts.Features.Realtime.Models.Events;

public sealed record RunStatusChangedEvent(
    string RunId,
    string State,
    string Summary,
    DateTime? StartedAtUtc,
    DateTime? EndedAtUtc);

