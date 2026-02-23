namespace AgentsDashboard.Contracts.SignalR;

public sealed record FindingUpdatedEvent(
    string FindingId,
    string RepositoryId,
    string State,
    string Severity,
    string Title)
{
}
