using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class SignalRRunEventPublisher(IHubContext<RunEventsHub> hubContext) : IRunEventPublisher
{
    public Task PublishStatusAsync(RunDocument run, CancellationToken cancellationToken)
    {
        return hubContext.Clients.All.SendAsync(
            "RunStatusChanged",
            run.Id,
            run.State.ToString(),
            run.Summary,
            run.StartedAtUtc,
            run.EndedAtUtc,
            cancellationToken);
    }

    public Task PublishLogAsync(RunLogEvent logEvent, CancellationToken cancellationToken)
    {
        return hubContext.Clients.All.SendAsync(
            "RunLogChunk",
            logEvent.RunId,
            logEvent.Level,
            logEvent.Message,
            logEvent.TimestampUtc,
            cancellationToken);
    }

    public Task PublishFindingUpdatedAsync(FindingDocument finding, CancellationToken cancellationToken)
    {
        return hubContext.Clients.All.SendAsync(
            "FindingUpdated",
            finding.Id,
            finding.RepositoryId,
            finding.State.ToString(),
            finding.Severity.ToString(),
            finding.Title,
            cancellationToken);
    }

    public Task PublishWorkerHeartbeatAsync(string workerId, string hostName, int activeSlots, int maxSlots, CancellationToken cancellationToken)
    {
        return hubContext.Clients.All.SendAsync(
            "WorkerHeartbeat",
            workerId,
            hostName,
            activeSlots,
            maxSlots,
            DateTime.UtcNow,
            cancellationToken);
    }

    public Task PublishRouteAvailableAsync(string runId, string routePath, CancellationToken cancellationToken)
    {
        return hubContext.Clients.All.SendAsync(
            "RouteAvailable",
            runId,
            routePath,
            DateTime.UtcNow,
            cancellationToken);
    }
}
