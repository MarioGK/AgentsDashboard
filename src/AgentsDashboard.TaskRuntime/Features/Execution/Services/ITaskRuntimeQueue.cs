

namespace AgentsDashboard.TaskRuntime.Features.Execution.Services;

public interface ITaskRuntimeQueue
{
    int MaxSlots { get; }
    int ActiveSlots { get; }
    IReadOnlyCollection<string> ActiveRunIds { get; }
    bool IsTracked(string runId);
    bool CanAcceptJob();
    ValueTask<bool> EnqueueAsync(QueuedJob job, CancellationToken cancellationToken);
    IAsyncEnumerable<QueuedJob> ReadAllAsync(CancellationToken cancellationToken);
    bool Cancel(string runId);
    void MarkCompleted(string runId);
}
