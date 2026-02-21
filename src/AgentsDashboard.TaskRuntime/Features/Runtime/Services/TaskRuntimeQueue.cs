using System.Collections.Concurrent;
using System.Threading.Channels;
using AgentsDashboard.TaskRuntime.Configuration;
using AgentsDashboard.TaskRuntime.Models;

namespace AgentsDashboard.TaskRuntime.Services;

public sealed class TaskRuntimeQueue : ITaskRuntimeQueue
{
    private readonly Channel<QueuedJob> _channel = Channel.CreateUnbounded<QueuedJob>();
    private readonly ConcurrentDictionary<string, QueuedJob> _activeJobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _maxSlots;

    public TaskRuntimeQueue(TaskRuntimeOptions options)
    {
        _maxSlots = Math.Max(1, options.MaxSlots);
    }

    public int MaxSlots => _maxSlots;

    public int ActiveSlots => _activeJobs.Count;

    public IReadOnlyCollection<string> ActiveRunIds => _activeJobs.Keys.ToList();

    public bool CanAcceptJob() => ActiveSlots < _maxSlots;

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
