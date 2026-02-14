using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.ControlPlane.Services;

public interface IRunEventPublisher
{
    Task PublishStatusAsync(RunDocument run, CancellationToken cancellationToken);
    Task PublishLogAsync(RunLogEvent logEvent, CancellationToken cancellationToken);
}
