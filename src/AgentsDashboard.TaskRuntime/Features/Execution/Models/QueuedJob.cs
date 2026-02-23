

namespace AgentsDashboard.TaskRuntime.Features.Execution.Models;

public sealed class QueuedJob
{
    public required DispatchJobRequest Request { get; init; }
    public CancellationTokenSource CancellationSource { get; } = new();
}
