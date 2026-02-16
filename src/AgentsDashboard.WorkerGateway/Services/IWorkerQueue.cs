using AgentsDashboard.WorkerGateway.Models;

namespace AgentsDashboard.WorkerGateway.Services;

public interface IWorkerQueue
{
    int MaxSlots { get; }
    int ActiveSlots { get; }
    IReadOnlyCollection<string> ActiveRunIds { get; }
    bool CanAcceptJob();
    ValueTask EnqueueAsync(QueuedJob job, CancellationToken cancellationToken);
    IAsyncEnumerable<QueuedJob> ReadAllAsync(CancellationToken cancellationToken);
    bool Cancel(string runId);
    void MarkCompleted(string runId);
}
