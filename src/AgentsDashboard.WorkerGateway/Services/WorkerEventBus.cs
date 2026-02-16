using System.Threading.Channels;
using AgentsDashboard.Contracts.Worker;

namespace AgentsDashboard.WorkerGateway.Services;

public sealed class WorkerEventBus
{
    private readonly Channel<JobEventMessage> _channel = Channel.CreateUnbounded<JobEventMessage>();

    public ValueTask PublishAsync(JobEventMessage message, CancellationToken cancellationToken)
        => _channel.Writer.WriteAsync(message, cancellationToken);

    public IAsyncEnumerable<JobEventMessage> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}
