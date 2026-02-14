using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.ControlPlane.Services;

public interface IRunEventPublisher
{
    Task PublishStatusAsync(RunDocument run, CancellationToken cancellationToken);
    Task PublishLogAsync(RunLogEvent logEvent, CancellationToken cancellationToken);
    Task PublishFindingUpdatedAsync(FindingDocument finding, CancellationToken cancellationToken);
    Task PublishWorkerHeartbeatAsync(string workerId, string hostName, int activeSlots, int maxSlots, CancellationToken cancellationToken);
    Task PublishRouteAvailableAsync(string runId, string routePath, CancellationToken cancellationToken);
}
