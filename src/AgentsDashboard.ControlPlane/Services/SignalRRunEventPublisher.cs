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
}
