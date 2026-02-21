namespace AgentsDashboard.TaskRuntime.Services.HarnessRuntimes;

public interface IHarnessEventSink
{
    ValueTask PublishAsync(HarnessRuntimeEvent @event, CancellationToken cancellationToken);
}
