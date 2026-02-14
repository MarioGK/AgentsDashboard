using System.Threading.Channels;
using AgentsDashboard.Contracts.Worker;

namespace AgentsDashboard.WorkerGateway.Services;

public sealed class WorkerEventBus
{
    private readonly Channel<JobEventReply> _channel = Channel.CreateUnbounded<JobEventReply>();

    public ValueTask PublishAsync(JobEventReply message, CancellationToken cancellationToken)
        => _channel.Writer.WriteAsync(message, cancellationToken);

    public IAsyncEnumerable<JobEventReply> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}
