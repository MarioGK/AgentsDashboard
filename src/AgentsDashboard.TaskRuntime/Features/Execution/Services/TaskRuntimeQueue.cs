using System.Collections.Concurrent;
using System.Threading.Channels;



namespace AgentsDashboard.TaskRuntime.Features.Execution.Services;

public sealed class TaskRuntimeQueue : ITaskRuntimeQueue
{
    private readonly Channel<QueuedJob> _channel = Channel.CreateUnbounded<QueuedJob>();
    private readonly ConcurrentDictionary<string, QueuedJob> _activeJobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _maxSlots;
    private readonly TaskRuntimeRunLedgerStore _runLedgerStore;

    public TaskRuntimeQueue(TaskRuntimeOptions options, TaskRuntimeRunLedgerStore runLedgerStore)
    {
        _maxSlots = Math.Max(1, options.MaxSlots);
        _runLedgerStore = runLedgerStore;

        InitializePendingQueue();
    }

    public int MaxSlots => _maxSlots;

    public int ActiveSlots => _activeJobs.Count;

    public IReadOnlyCollection<string> ActiveRunIds => _activeJobs.Keys.ToList();

    public bool CanAcceptJob() => ActiveSlots < _maxSlots;

    public async ValueTask EnqueueAsync(QueuedJob job, CancellationToken cancellationToken)
    {
        await _runLedgerStore.UpsertQueuedAsync(job.Request, cancellationToken);
        _activeJobs[job.Request.RunId] = job;
        await _channel.Writer.WriteAsync(job, cancellationToken);
    }

    public IAsyncEnumerable<QueuedJob> ReadAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);

    public bool Cancel(string runId)
    {
        if (!_activeJobs.TryGetValue(runId, out var job))
        {
            return false;
        }

        job.CancellationSource.Cancel();
        _ = _runLedgerStore.MarkCompletedAsync(
            runId,
            job.Request.TaskId,
            TaskRuntimeExecutionState.Cancelled,
            "Cancelled",
            string.Empty,
            CancellationToken.None);
        return true;
    }

    public void MarkCompleted(string runId)
    {
        _activeJobs.TryRemove(runId, out _);
    }

    private void InitializePendingQueue()
    {
        _runLedgerStore.RecoverStaleRunningRunsAsync(CancellationToken.None).GetAwaiter().GetResult();

        var queuedRequests = _runLedgerStore.ListQueuedRequestsAsync(CancellationToken.None).GetAwaiter().GetResult();
        foreach (var request in queuedRequests)
        {
            if (string.IsNullOrWhiteSpace(request.RunId))
            {
                continue;
            }

            var queuedJob = new QueuedJob { Request = request };
            _activeJobs[request.RunId] = queuedJob;
            _channel.Writer.TryWrite(queuedJob);
        }
    }
}
