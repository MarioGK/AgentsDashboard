using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.SignalR;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class BlazorRunEventPublisher(IUiRealtimeBroker broker) : IRunEventPublisher
{
    public Task PublishStatusAsync(RunDocument run, CancellationToken cancellationToken)
    {
        return broker.PublishAsync(
            new RunStatusChangedEvent(
                run.Id,
                run.State.ToString(),
                run.Summary,
                run.StartedAtUtc,
                run.EndedAtUtc),
            cancellationToken);
    }

    public Task PublishLogAsync(RunLogEvent logEvent, CancellationToken cancellationToken)
    {
        return broker.PublishAsync(
            new RunLogChunkEvent(
                logEvent.RunId,
                logEvent.Level,
                logEvent.Message,
                logEvent.TimestampUtc),
            cancellationToken);
    }

    public Task PublishFindingUpdatedAsync(FindingDocument finding, CancellationToken cancellationToken)
    {
        return broker.PublishAsync(
            new FindingUpdatedEvent(
                finding.Id,
                finding.RepositoryId,
                finding.State.ToString(),
                finding.Severity.ToString(),
                finding.Title),
            cancellationToken);
    }

    public Task PublishWorkerHeartbeatAsync(string workerId, string hostName, int activeSlots, int maxSlots, CancellationToken cancellationToken)
    {
        return broker.PublishAsync(
            new WorkerHeartbeatEvent(
                workerId,
                hostName,
                activeSlots,
                maxSlots,
                DateTime.UtcNow),
            cancellationToken);
    }

    public Task PublishRouteAvailableAsync(string runId, string routePath, CancellationToken cancellationToken)
    {
        return broker.PublishAsync(
            new RouteAvailableEvent(
                runId,
                routePath,
                DateTime.UtcNow),
            cancellationToken);
    }
}
