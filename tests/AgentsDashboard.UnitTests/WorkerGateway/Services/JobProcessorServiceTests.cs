using System.Text.Json;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.WorkerGateway.Adapters;
using AgentsDashboard.WorkerGateway.Configuration;
using AgentsDashboard.WorkerGateway.Models;
using AgentsDashboard.WorkerGateway.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.UnitTests.WorkerGateway.Services;

public class JobProcessorServiceTests
{
    private sealed class EventCollector
    {
        public List<JobEventReply> Events { get; } = [];
        private readonly WorkerEventBus _eventBus;
        private CancellationTokenSource? _cts;
        private Task? _collectTask;

        public EventCollector(WorkerEventBus eventBus)
        {
            _eventBus = eventBus;
        }

        public void StartCollecting()
        {
            _cts = new CancellationTokenSource();
            _collectTask = Task.Run(async () =>
            {
                await foreach (var evt in _eventBus.ReadAllAsync(_cts.Token))
                {
                    Events.Add(evt);
                }
            });
        }

        public async Task StopCollecting()
        {
            _cts?.Cancel();
            if (_collectTask != null)
            {
                try
                {
                    await _collectTask;
                }
                catch (OperationCanceledException)
                {
                }
            }
        }
    }

    private static WorkerOptions CreateDefaultOptions()
    {
        return new WorkerOptions
        {
            MaxSlots = 10,
            UseDocker = false,
        };
    }

    private static HarnessExecutor CreateExecutor(WorkerOptions? options = null)
    {
        var opts = Options.Create(options ?? CreateDefaultOptions());
        var redactor = new SecretRedactor(opts);
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var factory = new HarnessAdapterFactory(opts, redactor, serviceProvider);
        var dockerService = new DockerContainerService(NullLogger<DockerContainerService>.Instance);
        var artifactExtractor = new FakeArtifactExtractor();
        return new HarnessExecutor(opts, factory, redactor, dockerService, artifactExtractor, NullLogger<HarnessExecutor>.Instance);
    }

    private sealed class FakeArtifactExtractor : IArtifactExtractor
    {
        public Task<List<ExtractedArtifact>> ExtractArtifactsAsync(
            string workspacePath,
            string runId,
            ArtifactPolicyConfig policy,
            CancellationToken cancellationToken)
            => Task.FromResult(new List<ExtractedArtifact>());
    }

    private static QueuedJob CreateJob(string runId = "test-run-id", string taskId = "test-task-id")
    {
        return new QueuedJob
        {
            Request = new DispatchJobRequest
            {
                RunId = runId,
                TaskId = taskId,
                Command = "echo test",
                Harness = "codex",
            }
        };
    }

    [Fact(Skip = "Requires Docker runtime - Docker.DotNet version mismatch")]
    public async Task ProcessJobs_EmptyCommand_PublishesFailedCompleted()
    {
        var queue = new WorkerQueue(CreateDefaultOptions());
        var executor = CreateExecutor();
        var eventBus = new WorkerEventBus();
        var collector = new EventCollector(eventBus);

        var job = new QueuedJob
        {
            Request = new DispatchJobRequest
            {
                RunId = "test-run-id",
                TaskId = "test-task-id",
                Command = "",
                Harness = "codex",
            }
        };

        collector.StartCollecting();
        await queue.EnqueueAsync(job, CancellationToken.None);

        var processor = new JobProcessorService(queue, executor, eventBus, NullLogger<JobProcessorService>.Instance);
        var cts = new CancellationTokenSource();

        var serviceTask = processor.StartAsync(cts.Token);
        await Task.Delay(200);
        cts.Cancel();

        try
        {
            await serviceTask;
        }
        catch (OperationCanceledException)
        {
        }

        await collector.StopCollecting();

        collector.Events.Should().Contain(e => e.Kind == "log" && e.Message == "Job started");
        collector.Events.Should().Contain(e => e.Kind == "completed");

        var completedEvent = collector.Events.FirstOrDefault(e => e.Kind == "completed");
        completedEvent.Should().NotBeNull();

        var envelope = JsonSerializer.Deserialize<HarnessResultEnvelope>(completedEvent!.PayloadJson);
        envelope.Should().NotBeNull();
        envelope!.Status.Should().Be("failed");
    }

    [Fact(Skip = "Requires Docker runtime - Docker.DotNet version mismatch")]
    public async Task ProcessJobs_ValidCommand_PublishesJobStartedAndCompleted()
    {
        var queue = new WorkerQueue(CreateDefaultOptions());
        var executor = CreateExecutor();
        var eventBus = new WorkerEventBus();
        var collector = new EventCollector(eventBus);
        var job = CreateJob();

        collector.StartCollecting();
        await queue.EnqueueAsync(job, CancellationToken.None);

        var processor = new JobProcessorService(queue, executor, eventBus, NullLogger<JobProcessorService>.Instance);
        var cts = new CancellationTokenSource();

        var serviceTask = processor.StartAsync(cts.Token);

        await Task.Delay(100);
        while (queue.ActiveSlots > 0)
        {
            await Task.Delay(50);
        }

        cts.Cancel();

        try
        {
            await serviceTask;
        }
        catch (OperationCanceledException)
        {
        }

        await collector.StopCollecting();

        collector.Events.Should().Contain(e => e.Kind == "log" && e.Message == "Job started");
        collector.Events.Should().Contain(e => e.Kind == "completed");

        var startEvent = collector.Events.First(e => e.Kind == "log");
        startEvent.RunId.Should().Be("test-run-id");

        var completedEvent = collector.Events.First(e => e.Kind == "completed");
        completedEvent.RunId.Should().Be("test-run-id");
    }

    [Fact(Skip = "Requires Docker runtime - Docker.DotNet version mismatch")]
    public async Task ProcessJobs_AfterCompletion_MarksJobAsCompleted()
    {
        var queue = new WorkerQueue(CreateDefaultOptions());
        var executor = CreateExecutor();
        var eventBus = new WorkerEventBus();
        var job = CreateJob();

        await queue.EnqueueAsync(job, CancellationToken.None);
        queue.ActiveSlots.Should().Be(1);

        var processor = new JobProcessorService(queue, executor, eventBus, NullLogger<JobProcessorService>.Instance);
        var cts = new CancellationTokenSource();

        var serviceTask = processor.StartAsync(cts.Token);

        await Task.Delay(100);
        while (queue.ActiveSlots > 0)
        {
            await Task.Delay(50);
        }

        cts.Cancel();

        try
        {
            await serviceTask;
        }
        catch (OperationCanceledException)
        {
        }

        queue.ActiveSlots.Should().Be(0);
    }

    [Fact(Skip = "Requires Docker runtime - Docker.DotNet version mismatch")]
    public async Task ProcessJobs_MultipleJobs_ProcessesAllSequentially()
    {
        var queue = new WorkerQueue(CreateDefaultOptions());
        var executor = CreateExecutor();
        var eventBus = new WorkerEventBus();
        var collector = new EventCollector(eventBus);

        collector.StartCollecting();
        await queue.EnqueueAsync(CreateJob("run-1", "task-1"), CancellationToken.None);
        await queue.EnqueueAsync(CreateJob("run-2", "task-2"), CancellationToken.None);
        await queue.EnqueueAsync(CreateJob("run-3", "task-3"), CancellationToken.None);

        var processor = new JobProcessorService(queue, executor, eventBus, NullLogger<JobProcessorService>.Instance);
        var cts = new CancellationTokenSource();

        var serviceTask = processor.StartAsync(cts.Token);

        await Task.Delay(100);
        while (queue.ActiveSlots > 0)
        {
            await Task.Delay(50);
        }

        cts.Cancel();

        try
        {
            await serviceTask;
        }
        catch (OperationCanceledException)
        {
        }

        await collector.StopCollecting();

        var completedEvents = collector.Events.Where(e => e.Kind == "completed").ToList();
        completedEvents.Should().HaveCountGreaterThan(0);
        completedEvents.Should().Contain(e => e.RunId == "run-1");
        completedEvents.Should().Contain(e => e.RunId == "run-2");
        completedEvents.Should().Contain(e => e.RunId == "run-3");
    }

    [Fact(Skip = "Requires Docker runtime - Docker.DotNet version mismatch")]
    public async Task ProcessJobs_JobCancelledViaQueue_HandlesGracefully()
    {
        var queue = new WorkerQueue(CreateDefaultOptions());
        var executor = CreateExecutor();
        var eventBus = new WorkerEventBus();
        var collector = new EventCollector(eventBus);
        var job = CreateJob();

        collector.StartCollecting();
        await queue.EnqueueAsync(job, CancellationToken.None);
        queue.Cancel(job.Request.RunId);

        var processor = new JobProcessorService(queue, executor, eventBus, NullLogger<JobProcessorService>.Instance);
        var cts = new CancellationTokenSource();

        var serviceTask = processor.StartAsync(cts.Token);
        await Task.Delay(300);
        cts.Cancel();

        try
        {
            await serviceTask;
        }
        catch (OperationCanceledException)
        {
        }

        await collector.StopCollecting();

        var completedEvent = collector.Events.FirstOrDefault(e => e.Kind == "completed");
        if (completedEvent != null)
        {
            completedEvent.RunId.Should().Be("test-run-id");
        }
    }

    [Fact(Skip = "Requires Docker runtime - Docker.DotNet version mismatch")]
    public async Task ProcessJobs_EventTimestamps_ArePopulated()
    {
        var queue = new WorkerQueue(CreateDefaultOptions());
        var executor = CreateExecutor();
        var eventBus = new WorkerEventBus();
        var collector = new EventCollector(eventBus);
        var job = CreateJob();

        collector.StartCollecting();
        await queue.EnqueueAsync(job, CancellationToken.None);

        var processor = new JobProcessorService(queue, executor, eventBus, NullLogger<JobProcessorService>.Instance);
        var cts = new CancellationTokenSource();

        var serviceTask = processor.StartAsync(cts.Token);
        await Task.Delay(300);
        cts.Cancel();

        try
        {
            await serviceTask;
        }
        catch (OperationCanceledException)
        {
        }

        await collector.StopCollecting();

        collector.Events.Should().NotBeEmpty();
        collector.Events.Should().AllSatisfy(e => e.TimestampUnixMs.Should().BeGreaterThan(0));
    }

    [Fact(Skip = "Requires Docker runtime - Docker.DotNet version mismatch")]
    public async Task ProcessJobs_CompletedEventPayload_ContainsValidJson()
    {
        var queue = new WorkerQueue(CreateDefaultOptions());
        var executor = CreateExecutor();
        var eventBus = new WorkerEventBus();
        var collector = new EventCollector(eventBus);
        var job = CreateJob();

        collector.StartCollecting();
        await queue.EnqueueAsync(job, CancellationToken.None);

        var processor = new JobProcessorService(queue, executor, eventBus, NullLogger<JobProcessorService>.Instance);
        var cts = new CancellationTokenSource();

        var serviceTask = processor.StartAsync(cts.Token);

        await Task.Delay(100);
        while (queue.ActiveSlots > 0)
        {
            await Task.Delay(50);
        }

        cts.Cancel();

        try
        {
            await serviceTask;
        }
        catch (OperationCanceledException)
        {
        }

        await collector.StopCollecting();

        var completedEvent = collector.Events.FirstOrDefault(e => e.Kind == "completed");
        completedEvent.Should().NotBeNull();

        var action = () => JsonSerializer.Deserialize<HarnessResultEnvelope>(completedEvent!.PayloadJson);
        action.Should().NotThrow();

        var payload = action();
        payload.Should().NotBeNull();
        payload!.RunId.Should().Be("test-run-id");
    }

    [Fact(Skip = "Requires Docker runtime - Docker.DotNet version mismatch")]
    public async Task ProcessJobs_NoJobs_DoesNotPublishEvents()
    {
        var queue = new WorkerQueue(CreateDefaultOptions());
        var executor = CreateExecutor();
        var eventBus = new WorkerEventBus();
        var collector = new EventCollector(eventBus);

        collector.StartCollecting();

        var processor = new JobProcessorService(queue, executor, eventBus, NullLogger<JobProcessorService>.Instance);
        var cts = new CancellationTokenSource();

        var serviceTask = processor.StartAsync(cts.Token);
        await Task.Delay(100);
        cts.Cancel();

        try
        {
            await serviceTask;
        }
        catch (OperationCanceledException)
        {
        }

        await collector.StopCollecting();

        collector.Events.Should().BeEmpty();
    }

    [Fact(Skip = "Requires Docker runtime - Docker.DotNet version mismatch")]
    public async Task ProcessJobs_ServiceStopsImmediately_HandlesGracefully()
    {
        var queue = new WorkerQueue(CreateDefaultOptions());
        var executor = CreateExecutor();
        var eventBus = new WorkerEventBus();

        var processor = new JobProcessorService(queue, executor, eventBus, NullLogger<JobProcessorService>.Instance);
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var action = async () => await processor.StartAsync(cts.Token);
        await action.Should().NotThrowAsync();
    }

    [Fact(Skip = "Requires Docker runtime - Docker.DotNet version mismatch")]
    public async Task ProcessJobs_AllJobsComplete_QueueBecomesEmpty()
    {
        var queue = new WorkerQueue(CreateDefaultOptions());
        var executor = CreateExecutor();
        var eventBus = new WorkerEventBus();

        await queue.EnqueueAsync(CreateJob("run-1", "task-1"), CancellationToken.None);
        await queue.EnqueueAsync(CreateJob("run-2", "task-2"), CancellationToken.None);
        queue.ActiveSlots.Should().Be(2);

        var processor = new JobProcessorService(queue, executor, eventBus, NullLogger<JobProcessorService>.Instance);
        var cts = new CancellationTokenSource();

        var serviceTask = processor.StartAsync(cts.Token);

        await Task.Delay(100);
        while (queue.ActiveSlots > 0)
        {
            await Task.Delay(50);
        }

        cts.Cancel();

        try
        {
            await serviceTask;
        }
        catch (OperationCanceledException)
        {
        }

        queue.ActiveSlots.Should().Be(0);
    }
}
