using System.Collections.Concurrent;
using System.Threading.Channels;
using AgentsDashboard.WorkerGateway.Configuration;
using AgentsDashboard.WorkerGateway.Models;

namespace AgentsDashboard.WorkerGateway.Services;

public sealed class WorkerQueue
{
    private readonly Channel<QueuedJob> _channel = Channel.CreateUnbounded<QueuedJob>();
    private readonly ConcurrentDictionary<string, QueuedJob> _activeJobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly WorkerOptions _options;

    public WorkerQueue(WorkerOptions options)
    {
        _options = options;
    }

    public int MaxSlots => _options.MaxSlots;

    public int ActiveSlots => _activeJobs.Count;

    public bool CanAcceptJob() => ActiveSlots < MaxSlots;

    public ValueTask EnqueueAsync(QueuedJob job, CancellationToken cancellationToken)
    {
        _activeJobs[job.Request.RunId] = job;
        return _channel.Writer.WriteAsync(job, cancellationToken);
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
        return true;
    }

    public void MarkCompleted(string runId)
    {
        _activeJobs.TryRemove(runId, out _);
    }
}
