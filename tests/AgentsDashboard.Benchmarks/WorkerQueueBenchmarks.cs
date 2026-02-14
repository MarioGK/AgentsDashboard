using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.WorkerGateway.Configuration;
using AgentsDashboard.WorkerGateway.Models;
using AgentsDashboard.WorkerGateway.Services;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Jobs;

namespace AgentsDashboard.Benchmarks;

[MemoryDiagnoser]
[RankColumn]
public class WorkerQueueBenchmarks
{
    private WorkerQueue _queue = null!;
    private List<QueuedJob> _jobs = null!;

    [Params(10, 50, 100, 500)]
    public int JobCount { get; set; }

    [Params(4, 8, 16)]
    public int MaxSlots { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _queue = new WorkerQueue(new WorkerOptions { MaxSlots = MaxSlots });
        _jobs = new List<QueuedJob>();

        for (int i = 0; i < JobCount; i++)
        {
            _jobs.Add(new QueuedJob
            {
                Request = new DispatchJobRequest
                {
                    RunId = $"run-{i}",
                    Command = "echo test"
                }
            });
        }
    }

    [IterationSetup]
    public void IterationSetup()
    {
        _queue = new WorkerQueue(new WorkerOptions { MaxSlots = MaxSlots });
    }

    [Benchmark(Description = "Enqueue jobs sequentially")]
    public void EnqueueSequential()
    {
        for (int i = 0; i < JobCount; i++)
        {
            _queue.EnqueueAsync(_jobs[i], CancellationToken.None).AsTask().Wait();
        }
    }

    [Benchmark(Description = "Enqueue jobs concurrently")]
    public void EnqueueConcurrent()
    {
        var tasks = _jobs.Select(job => _queue.EnqueueAsync(job, CancellationToken.None).AsTask()).ToArray();
        Task.WaitAll(tasks);
    }

    [Benchmark(Description = "Enqueue then MarkCompleted cycle")]
    public void EnqueueMarkCompletedCycle()
    {
        for (int i = 0; i < JobCount; i++)
        {
            _queue.EnqueueAsync(_jobs[i], CancellationToken.None).AsTask().Wait();
            _queue.MarkCompleted(_jobs[i].Request.RunId);
        }
    }

    [Benchmark(Description = "CanAcceptJob check")]
    public bool CanAcceptJob()
    {
        return _queue.CanAcceptJob();
    }

    [Benchmark(Description = "Cancel operation")]
    public bool CancelJob()
    {
        _queue.EnqueueAsync(_jobs[0], CancellationToken.None).AsTask().Wait();
        return _queue.Cancel(_jobs[0].Request.RunId);
    }

    [Benchmark(Description = "Full cycle: enqueue, process, complete")]
    public async Task FullCycle()
    {
        var cts = new CancellationTokenSource();
        var processedCount = 0;

        var consumerTask = Task.Run(async () =>
        {
            await foreach (var job in _queue.ReadAllAsync(cts.Token))
            {
                Interlocked.Increment(ref processedCount);
                _queue.MarkCompleted(job.Request.RunId);
                if (processedCount >= JobCount)
                    break;
            }
        }, cts.Token);

        foreach (var job in _jobs)
        {
            await _queue.EnqueueAsync(job, CancellationToken.None);
        }

        await consumerTask.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        cts.Cancel();
    }
}
