namespace AgentsDashboard.TaskRuntimeGateway.Services.HarnessRuntimes;

public sealed class NullHarnessEventSink : IHarnessEventSink
{
    public static NullHarnessEventSink Instance { get; } = new();

    private NullHarnessEventSink()
    {
    }

    public ValueTask PublishAsync(HarnessRuntimeEvent @event, CancellationToken cancellationToken)
    {
        return ValueTask.CompletedTask;
    }
}
