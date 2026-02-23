using System.Threading.Channels;


namespace AgentsDashboard.TaskRuntime.Features.Events.Services;

public sealed class TaskRuntimeEventBus
{
    private readonly Channel<JobEventMessage> _channel = Channel.CreateUnbounded<JobEventMessage>();

    public ValueTask PublishAsync(JobEventMessage message, CancellationToken cancellationToken)
        => _channel.Writer.WriteAsync(message, cancellationToken);

    public IAsyncEnumerable<JobEventMessage> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}
