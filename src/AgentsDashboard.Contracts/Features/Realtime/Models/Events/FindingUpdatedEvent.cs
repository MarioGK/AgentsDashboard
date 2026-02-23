namespace AgentsDashboard.Contracts.Features.Realtime.Models.Events;

public sealed record FindingUpdatedEvent(
    string FindingId,
    string RepositoryId,
    string State,
    string Severity,
    string Title)
{
}
