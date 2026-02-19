using System.Threading.Channels;
using AgentsDashboard.Contracts.TaskRuntime;

namespace AgentsDashboard.TaskRuntime.Services;

public sealed class TaskRuntimeEventBus
{
    private readonly Channel<JobEventMessage> _channel = Channel.CreateUnbounded<JobEventMessage>();

    public ValueTask PublishAsync(JobEventMessage message, CancellationToken cancellationToken)
        => _channel.Writer.WriteAsync(message, cancellationToken);

    public IAsyncEnumerable<JobEventMessage> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}
