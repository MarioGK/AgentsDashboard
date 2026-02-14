using AgentsDashboard.Contracts.Worker;

namespace AgentsDashboard.WorkerGateway.Models;

public sealed class QueuedJob
{
    public required DispatchJobRequest Request { get; init; }
    public CancellationTokenSource CancellationSource { get; } = new();
}
