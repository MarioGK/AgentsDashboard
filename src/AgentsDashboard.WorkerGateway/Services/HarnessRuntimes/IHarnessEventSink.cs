namespace AgentsDashboard.WorkerGateway.Services.HarnessRuntimes;

public interface IHarnessEventSink
{
    ValueTask PublishAsync(HarnessRuntimeEvent @event, CancellationToken cancellationToken);
}
