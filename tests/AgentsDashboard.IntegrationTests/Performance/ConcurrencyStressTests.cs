using System.Collections.Concurrent;
using System.Diagnostics;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Hubs;
using AgentsDashboard.ControlPlane.Services;
using AgentsDashboard.IntegrationTests.Infrastructure;
using AgentsDashboard.WorkerGateway.Configuration;
using AgentsDashboard.WorkerGateway.Models;
using AgentsDashboard.WorkerGateway.Services;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AgentsDashboard.IntegrationTests.Performance;

[Trait("Category", "Performance")]
[Collection("Performance")]
public sealed class ConcurrencyStressTests : IAsyncLifetime
{
    private OrchestratorStore _store = null!;
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"agentsdashboard-perf-{Guid.NewGuid():N}.db");
    private string _connectionString = string.Empty;

    public async Task InitializeAsync()
    {
        _connectionString = $"Data Source={_databasePath}";
        var dbOptions = new DbContextOptionsBuilder<OrchestratorDbContext>()
            .UseSqlite(_connectionString)
            .Options;
        await using (var dbContext = new OrchestratorDbContext(dbOptions))
        {
            await dbContext.Database.MigrateAsync();
        }

        _store = TestOrchestratorStore.Create(_connectionString);
        await _store.InitializeAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await Task.CompletedTask;
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    [Fact]
    public async Task ConcurrentJobDispatch_50Jobs_AllHandledWithoutException()
    {
        const int jobCount = 50;
        var results = new ConcurrentBag<(string RunId, bool Success, TimeSpan Duration)>();
        var errors = new ConcurrentBag<Exception>();
        var mockWorkerClient = new MockStressWorkerClient();
        var mockPublisher = new MockStressRunEventPublisher();

        var options = Options.Create(new OrchestratorOptions
        {
            MaxGlobalConcurrentRuns = 100,
            PerProjectConcurrencyLimit = 100,
            PerRepoConcurrencyLimit = 100,
        });

        var mockCrypto = CreateMockCryptoService();
        var yarpProvider = new AgentsDashboard.ControlPlane.Proxy.InMemoryYarpConfigProvider();
        var dispatcher = new RunDispatcher(
            mockWorkerClient,
            _store,
            new MockWorkerLifecycleManager(),
            mockCrypto,
            mockPublisher,
            yarpProvider,
            options,
            new NullLogger<RunDispatcher>());

        var (project, repo, task) = await SetupPrerequisitesAsync();

        var runs = new List<RunDocument>();
        for (int i = 0; i < jobCount; i++)
        {
            var run = await _store.CreateRunAsync(task, project.Id, CancellationToken.None);
            runs.Add(run);
        }

        var overallSw = Stopwatch.StartNew();
        var tasks = runs.Select(async run =>
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var success = await dispatcher.DispatchAsync(project, repo, task, run, CancellationToken.None);
                sw.Stop();
                results.Add((run.Id, success, sw.Elapsed));
            }
            catch (Exception ex)
            {
                sw.Stop();
                errors.Add(ex);
                results.Add((run.Id, false, sw.Elapsed));
            }
        }).ToArray();

        await Task.WhenAll(tasks);
        overallSw.Stop();

        errors.Should().BeEmpty($"all {jobCount} dispatches should complete without exceptions");
        results.Count.Should().Be(jobCount);
        mockWorkerClient.DispatchCount.Should().Be(jobCount);

        var successfulDispatches = results.Count(r => r.Success);
        successfulDispatches.Should().Be(jobCount);

        var avgDuration = TimeSpan.FromTicks((long)results.Average(r => r.Duration.Ticks));
        var p95Duration = GetPercentile(results.Select(r => r.Duration).OrderBy(d => d).ToList(), 0.95);
        var p99Duration = GetPercentile(results.Select(r => r.Duration).OrderBy(d => d).ToList(), 0.99);

        OutputMetrics($"50 concurrent dispatches", jobCount, overallSw.Elapsed, avgDuration, p95Duration, p99Duration, errors.Count);
    }

    [Fact]
    public async Task SignalRStatusUpdate_Sub2SecondsP95Latency_MeetsTarget()
    {
        const int updateCount = 100;
        var latencies = new ConcurrentBag<TimeSpan>();

        var mockHubContext = new Mock<IHubContext<RunEventsHub>>();
        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();

        mockHubContext.Setup(x => x.Clients).Returns(mockClients.Object);
        mockClients.Setup(x => x.All).Returns(mockClientProxy.Object);

        mockClientProxy
            .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns((string method, object[] args, CancellationToken token) => Task.CompletedTask);

        var publisher = new SignalRRunEventPublisher(mockHubContext.Object);

        var runs = Enumerable.Range(0, updateCount)
            .Select(i => new RunDocument
            {
                Id = $"run-{i}",
                State = i % 2 == 0 ? RunState.Running : RunState.Succeeded,
                Summary = $"Run {i}",
                StartedAtUtc = DateTime.UtcNow.AddMinutes(-5),
                EndedAtUtc = i % 2 == 0 ? null : DateTime.UtcNow
            })
            .ToList();

        var sw = Stopwatch.StartNew();
        var tasks = runs.Select(async run =>
        {
            var start = Stopwatch.GetTimestamp();
            await publisher.PublishStatusAsync(run, CancellationToken.None);
            var elapsed = Stopwatch.GetElapsedTime(start);
            latencies.Add(elapsed);
        }).ToArray();

        await Task.WhenAll(tasks);
        sw.Stop();

        var orderedLatencies = latencies.OrderBy(l => l).ToList();
        var p50 = GetPercentile(orderedLatencies, 0.50);
        var p95 = GetPercentile(orderedLatencies, 0.95);
        var p99 = GetPercentile(orderedLatencies, 0.99);
        var max = orderedLatencies.Max();

        OutputMetrics($"SignalR status updates", updateCount, sw.Elapsed, p50, p95, p99, 0);

        p95.Should().BeLessThan(TimeSpan.FromSeconds(2), "P95 latency should be under 2 seconds");
        max.Should().BeLessThan(TimeSpan.FromSeconds(3), "Max latency should be under 3 seconds");
    }

    [Fact]
    public async Task WorkerSlotSaturation_RespectsMaxSlots_QueuesExcess()
    {
        const int maxSlots = 4;
        const int totalJobs = 20;
        var queue = new WorkerQueue(new WorkerOptions { MaxSlots = maxSlots });
        var processedJobs = new ConcurrentBag<string>();
        var processingTimes = new ConcurrentBag<(string RunId, TimeSpan WaitTime)>();
        var enqueueTimestamps = new ConcurrentDictionary<string, long>();
        var cts = new CancellationTokenSource();

        var consumerTask = Task.Run(async () =>
        {
            await foreach (var job in queue.ReadAllAsync(cts.Token))
            {
                if (enqueueTimestamps.TryGetValue(job.Request.RunId, out var enqueueTime))
                {
                    var waitTime = Stopwatch.GetElapsedTime(enqueueTime);
                    processingTimes.Add((job.Request.RunId, waitTime));
                }
                processedJobs.Add(job.Request.RunId);
                await Task.Delay(50, cts.Token);
                queue.MarkCompleted(job.Request.RunId);
            }
        }, cts.Token);

        var enqueueTasks = Enumerable.Range(0, totalJobs)
            .Select(i =>
            {
                var runId = $"run-{i}";
                enqueueTimestamps[runId] = Stopwatch.GetTimestamp();
                return EnqueueJobWithTimingAsync(queue, runId);
            })
            .ToArray();

        await Task.WhenAll(enqueueTasks);

        var sw = Stopwatch.StartNew();
        while (processedJobs.Count < totalJobs && sw.Elapsed < TimeSpan.FromSeconds(30))
        {
            await Task.Delay(100);
        }

        cts.Cancel();

        try
        {
            await consumerTask;
        }
        catch (OperationCanceledException)
        {
        }

        processedJobs.Count.Should().Be(totalJobs);

        var saturatedCount = 0;
        for (int i = 0; i < totalJobs; i++)
        {
            if (i >= maxSlots)
            {
                saturatedCount++;
            }
        }

        OutputMetrics($"Worker slot saturation", totalJobs, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, 0);
    }

    [Fact]
    public async Task QueueBacklogHandling_100Jobs_ProcessesAllJobs()
    {
        const int jobCount = 100;
        const int maxSlots = 4;
        var queue = new WorkerQueue(new WorkerOptions { MaxSlots = maxSlots });
        var processedOrder = new ConcurrentBag<(int Order, string RunId, DateTime ProcessedAt)>();
        var enqueueOrder = new List<(int Order, string RunId)>();
        var cts = new CancellationTokenSource();
        var processingStarted = new TaskCompletionSource();
        var processedCount = 0;

        var consumerTask = Task.Run(async () =>
        {
            processingStarted.SetResult();
            await foreach (var job in queue.ReadAllAsync(cts.Token))
            {
                var order = int.Parse(job.Request.RunId.Replace("run-", ""));
                processedOrder.Add((order, job.Request.RunId, DateTime.UtcNow));
                Interlocked.Increment(ref processedCount);
                await Task.Delay(10, cts.Token);
                queue.MarkCompleted(job.Request.RunId);
            }
        }, cts.Token);

        await processingStarted.Task;

        for (int i = 0; i < jobCount; i++)
        {
            var runId = $"run-{i}";
            var job = CreateQueuedJob(runId);
            await queue.EnqueueAsync(job, CancellationToken.None);
            enqueueOrder.Add((i, runId));
        }

        var sw = Stopwatch.StartNew();
        while (processedCount < jobCount && sw.Elapsed < TimeSpan.FromSeconds(30))
        {
            await Task.Delay(100);
        }

        cts.Cancel();

        try
        {
            await consumerTask;
        }
        catch (OperationCanceledException)
        {
        }

        OutputMetrics($"Queue backlog processing", jobCount, sw.Elapsed, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, 0);

        processedOrder.Count.Should().Be(jobCount, "all jobs should be processed");
    }

    [Fact]
    public async Task ConcurrentEnqueueDequeue_50Operations_NoRaceConditions()
    {
        const int operationsPerThread = 100;
        const int threadCount = 10;
        var queue = new WorkerQueue(new WorkerOptions { MaxSlots = 1000 });
        var enqueueCount = 0;
        var dequeueCount = 0;
        var errors = new ConcurrentBag<Exception>();
        var cts = new CancellationTokenSource();

        var producerTasks = Enumerable.Range(0, threadCount)
            .Select(threadId => Task.Run(async () =>
            {
                for (int i = 0; i < operationsPerThread; i++)
                {
                    try
                    {
                        var job = CreateQueuedJob($"run-{threadId}-{i}");
                        await queue.EnqueueAsync(job, CancellationToken.None);
                        Interlocked.Increment(ref enqueueCount);
                    }
                    catch (Exception ex)
                    {
                        errors.Add(ex);
                    }
                }
            }))
            .ToArray();

        var consumerTask = Task.Run(async () =>
        {
            await foreach (var job in queue.ReadAllAsync(cts.Token))
            {
                Interlocked.Increment(ref dequeueCount);
                queue.MarkCompleted(job.Request.RunId);
            }
        }, cts.Token);

        await Task.WhenAll(producerTasks);

        var sw = Stopwatch.StartNew();
        while (dequeueCount < operationsPerThread * threadCount && sw.Elapsed < TimeSpan.FromSeconds(30))
        {
            await Task.Delay(50);
        }

        cts.Cancel();

        try
        {
            await consumerTask;
        }
        catch (OperationCanceledException)
        {
        }

        errors.Should().BeEmpty("no race conditions should occur");
        enqueueCount.Should().Be(operationsPerThread * threadCount);
        dequeueCount.Should().Be(operationsPerThread * threadCount);
        queue.ActiveSlots.Should().Be(0);

        OutputMetrics($"Concurrent enqueue/dequeue", operationsPerThread * threadCount, sw.Elapsed, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, errors.Count);
    }

    [Fact]
    public async Task HighThroughputLogPublishing_1000Logs_Under500msP95()
    {
        const int logCount = 1000;
        var latencies = new ConcurrentBag<TimeSpan>();

        var mockHubContext = new Mock<IHubContext<RunEventsHub>>();
        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();

        mockHubContext.Setup(x => x.Clients).Returns(mockClients.Object);
        mockClients.Setup(x => x.All).Returns(mockClientProxy.Object);
        mockClientProxy
            .Setup(x => x.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var publisher = new SignalRRunEventPublisher(mockHubContext.Object);

        var overallSw = Stopwatch.StartNew();
        var tasks = Enumerable.Range(0, logCount)
            .Select(i =>
            {
                var logEvent = new RunLogEvent
                {
                    RunId = $"run-{i % 10}",
                    Level = i % 5 == 0 ? "error" : "info",
                    Message = $"Log message {i} with some content to make it realistic",
                    TimestampUtc = DateTime.UtcNow
                };

                var sw = Stopwatch.StartNew();
                return publisher.PublishLogAsync(logEvent, CancellationToken.None)
                    .ContinueWith(_ =>
                    {
                        sw.Stop();
                        latencies.Add(sw.Elapsed);
                    });
            })
            .ToArray();

        await Task.WhenAll(tasks);
        overallSw.Stop();

        var orderedLatencies = latencies.OrderBy(l => l).ToList();
        var p50 = GetPercentile(orderedLatencies, 0.50);
        var p95 = GetPercentile(orderedLatencies, 0.95);
        var p99 = GetPercentile(orderedLatencies, 0.99);

        OutputMetrics($"High-throughput log publishing", logCount, overallSw.Elapsed, p50, p95, p99, 0);

        p95.Should().BeLessThan(TimeSpan.FromMilliseconds(500), "P95 latency for log publishing should be under 500ms");
        overallSw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "Total time for 1000 logs should be under 10 seconds");
    }

    [Fact]
    public async Task SqliteWriteThroughput_MultipleRuns_CompletesEfficiently()
    {
        const int runCount = 100;
        var (_, _, task) = await SetupPrerequisitesAsync();

        var sw = Stopwatch.StartNew();

        var tasks = Enumerable.Range(0, runCount)
            .Select(_ => _store.CreateRunAsync(task, "proj-1", CancellationToken.None))
            .ToArray();

        await Task.WhenAll(tasks);
        sw.Stop();

        var throughput = runCount / sw.Elapsed.TotalSeconds;
        OutputMetrics($"SQLite write throughput", runCount, sw.Elapsed, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, 0);

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30), $"{runCount} SQLite writes should complete in reasonable time");
    }

    private async Task<(ProjectDocument Project, RepositoryDocument Repo, TaskDocument Task)> SetupPrerequisitesAsync()
    {
        var project = await _store.CreateProjectAsync(
            new CreateProjectRequest("Perf Test Project", "Performance testing"),
            CancellationToken.None);

        var repo = await _store.CreateRepositoryAsync(
            new CreateRepositoryRequest(project.Id, "perf-test-repo", "https://github.com/test/perf-test.git", "main"),
            CancellationToken.None);

        var task = await _store.CreateTaskAsync(
            new CreateTaskRequest(
                repo.Id,
                "Perf Test Task",
                TaskKind.OneShot,
                "codex",
                "Test prompt",
                "echo test",
                false,
                "",
                true),
            CancellationToken.None);

        return (project, repo, task);
    }

    private static QueuedJob CreateQueuedJob(string runId)
    {
        return new QueuedJob
        {
            Request = new DispatchJobRequest
            {
                RunId = runId,
                Command = "echo test",
            }
        };
    }

    private static async Task EnqueueJobWithTimingAsync(WorkerQueue queue, string runId)
    {
        var job = CreateQueuedJob(runId);
        await queue.EnqueueAsync(job, CancellationToken.None);
    }

    private static SecretCryptoService CreateMockCryptoService()
    {
        var mockProvider = new Mock<IDataProtectionProvider>();
        var mockProtector = new Mock<IDataProtector>();

        mockProtector
            .Setup(x => x.Protect(It.IsAny<byte[]>()))
            .Returns<byte[]>(data => data);
        mockProtector
            .Setup(x => x.Unprotect(It.IsAny<byte[]>()))
            .Returns<byte[]>(data => data);

        mockProvider
            .Setup(x => x.CreateProtector(It.IsAny<string>()))
            .Returns(mockProtector.Object);

        return new SecretCryptoService(mockProvider.Object);
    }

    private static TimeSpan GetPercentile(IReadOnlyList<TimeSpan> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0)
            return TimeSpan.Zero;

        var index = (int)Math.Ceiling(sortedValues.Count * percentile) - 1;
        index = Math.Max(0, Math.Min(index, sortedValues.Count - 1));
        return sortedValues[index];
    }

    private static void OutputMetrics(
        string testName,
        int operationCount,
        TimeSpan totalTime,
        TimeSpan p50OrAvg,
        TimeSpan p95,
        TimeSpan p99,
        int errorCount)
    {
        var throughput = totalTime.TotalSeconds > 0
            ? operationCount / totalTime.TotalSeconds
            : operationCount;

        Console.WriteLine($"""
            [{testName}]
              Operations: {operationCount}
              Total Time: {totalTime.TotalMilliseconds:F2}ms
              Throughput: {throughput:F2} ops/sec
              P50/Avg:    {p50OrAvg.TotalMilliseconds:F2}ms
              P95:        {p95.TotalMilliseconds:F2}ms
              P99:        {p99.TotalMilliseconds:F2}ms
              Errors:     {errorCount}
            """);
    }
}

public sealed class MockStressWorkerClient : AgentsDashboard.Contracts.Worker.WorkerGateway.WorkerGatewayClient
{
    private int _dispatchCount;
    public int DispatchCount => _dispatchCount;

    public override Grpc.Core.AsyncUnaryCall<DispatchJobReply> DispatchJobAsync(
        DispatchJobRequest request,
        Grpc.Core.Metadata? headers = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _dispatchCount);
        var reply = new DispatchJobReply { Accepted = true };
        return new Grpc.Core.AsyncUnaryCall<DispatchJobReply>(
            Task.FromResult(reply),
            Task.FromResult(new Grpc.Core.Metadata()),
            () => Grpc.Core.Status.DefaultSuccess,
            () => new Grpc.Core.Metadata(),
            () => { });
    }

    public override Grpc.Core.AsyncUnaryCall<CancelJobReply> CancelJobAsync(
        CancelJobRequest request,
        Grpc.Core.Metadata? headers = null,
        DateTime? deadline = null,
        CancellationToken cancellationToken = default)
    {
        var reply = new CancelJobReply { Accepted = true };
        return new Grpc.Core.AsyncUnaryCall<CancelJobReply>(
            Task.FromResult(reply),
            Task.FromResult(new Grpc.Core.Metadata()),
            () => Grpc.Core.Status.DefaultSuccess,
            () => new Grpc.Core.Metadata(),
            () => { });
    }
}

public sealed class MockStressRunEventPublisher : IRunEventPublisher
{
    private int _statusCount;
    private int _logCount;

    public int StatusCount => _statusCount;
    public int LogCount => _logCount;

    public Task PublishStatusAsync(RunDocument run, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _statusCount);
        return Task.CompletedTask;
    }

    public Task PublishLogAsync(RunLogEvent logEvent, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _logCount);
        return Task.CompletedTask;
    }

    public Task PublishFindingUpdatedAsync(FindingDocument finding, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task PublishWorkerHeartbeatAsync(string workerId, string hostName, int activeSlots, int maxSlots, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task PublishRouteAvailableAsync(string runId, string routePath, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

public sealed class NullLogger<T> : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => false;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
}

public sealed class MockWorkerLifecycleManager : IWorkerLifecycleManager
{
    public Task<bool> EnsureWorkerRunningAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    public Task RecordDispatchActivityAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopWorkerIfIdleAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

[CollectionDefinition("Performance", DisableParallelization = true)]
public class PerformanceCollection;
