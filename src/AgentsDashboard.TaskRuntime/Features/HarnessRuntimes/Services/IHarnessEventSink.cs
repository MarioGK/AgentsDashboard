namespace AgentsDashboard.TaskRuntime.Features.HarnessRuntimes.Services;

public interface IHarnessEventSink
{
    ValueTask PublishAsync(HarnessRuntimeEvent @event, CancellationToken cancellationToken);
}
