using AgentsDashboard.Contracts.TaskRuntime;

namespace AgentsDashboard.TaskRuntime.Models;

public sealed class QueuedJob
{
    public required DispatchJobRequest Request { get; init; }
    public CancellationTokenSource CancellationSource { get; } = new();
}
