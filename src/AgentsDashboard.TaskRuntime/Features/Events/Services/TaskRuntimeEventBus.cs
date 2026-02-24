using System.Threading.Channels;


namespace AgentsDashboard.TaskRuntime.Features.Events.Services;

public sealed class TaskRuntimeEventBus(TaskRuntimeEventOutboxService outboxService)
{
    private readonly Channel<JobEventMessage> _channel = Channel.CreateUnbounded<JobEventMessage>();

    public async ValueTask PublishAsync(JobEventMessage message, CancellationToken cancellationToken)
    {
        var persisted = await outboxService.AppendAsync(message, cancellationToken);
        await _channel.Writer.WriteAsync(persisted, cancellationToken);
    }

    public IAsyncEnumerable<JobEventMessage> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}
