using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.WorkerGateway.Configuration;
using AgentsDashboard.WorkerGateway.Models;
using AgentsDashboard.WorkerGateway.Services;

namespace AgentsDashboard.UnitTests.WorkerGateway.Services;

public class WorkerQueueTests
{
    private static WorkerQueue CreateQueue(int maxSlots = 4)
    {
        return new WorkerQueue(new WorkerOptions { MaxSlots = maxSlots });
    }

    private static QueuedJob CreateJob(string runId)
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

    [Fact]
    public async Task EnqueueAsync_AddsJobToQueue()
    {
        var queue = CreateQueue();
        var job = CreateJob("run-1");

        await queue.EnqueueAsync(job, CancellationToken.None);

        var jobs = new List<QueuedJob>();
        await foreach (var j in queue.ReadAllAsync(CancellationToken.None))
        {
            jobs.Add(j);
            break;
        }

        jobs.Should().ContainSingle().Which.Request.RunId.Should().Be("run-1");
    }

    [Fact]
    public async Task EnqueueAsync_MultipleJobs_AllAddedToQueue()
    {
        var queue = CreateQueue();

        await queue.EnqueueAsync(CreateJob("run-1"), CancellationToken.None);
        await queue.EnqueueAsync(CreateJob("run-2"), CancellationToken.None);
        await queue.EnqueueAsync(CreateJob("run-3"), CancellationToken.None);

        queue.ActiveSlots.Should().Be(3);
        queue.Cancel("run-1").Should().BeTrue();
        queue.Cancel("run-2").Should().BeTrue();
        queue.Cancel("run-3").Should().BeTrue();
    }

    [Fact]
    public void Cancel_ExistingJob_ReturnsTrue()
    {
        var queue = CreateQueue();
        var job = CreateJob("run-1");

        queue.EnqueueAsync(job, CancellationToken.None).AsTask().Wait();

        var result = queue.Cancel("run-1");

        result.Should().BeTrue();
        job.CancellationSource.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void Cancel_NonExistentJob_ReturnsFalse()
    {
        var queue = CreateQueue();

        var result = queue.Cancel("nonexistent");

        result.Should().BeFalse();
    }

    [Fact]
    public void Cancel_IsCaseInsensitive()
    {
        var queue = CreateQueue();
        var job = CreateJob("Run-ABC");

        queue.EnqueueAsync(job, CancellationToken.None).AsTask().Wait();

        var result = queue.Cancel("run-abc");

        result.Should().BeTrue();
    }

    [Fact]
    public void MarkCompleted_RemovesJobFromActiveJobs()
    {
        var queue = CreateQueue();
        var job = CreateJob("run-1");

        queue.EnqueueAsync(job, CancellationToken.None).AsTask().Wait();

        queue.MarkCompleted("run-1");

        var result = queue.Cancel("run-1");
        result.Should().BeFalse();
    }

    [Fact]
    public void MarkCompleted_Idempotent()
    {
        var queue = CreateQueue();
        var job = CreateJob("run-1");

        queue.EnqueueAsync(job, CancellationToken.None).AsTask().Wait();

        queue.MarkCompleted("run-1");
        var action = () => queue.MarkCompleted("run-1");

        action.Should().NotThrow();
    }

    [Fact]
    public void CanAcceptJob_WhenBelowMaxSlots_ReturnsTrue()
    {
        var queue = CreateQueue(maxSlots: 4);

        var result = queue.CanAcceptJob();

        result.Should().BeTrue();
    }

    [Fact]
    public async Task CanAcceptJob_WhenAtMaxSlots_ReturnsFalse()
    {
        var queue = CreateQueue(maxSlots: 2);

        await queue.EnqueueAsync(CreateJob("run-1"), CancellationToken.None);
        await queue.EnqueueAsync(CreateJob("run-2"), CancellationToken.None);

        var result = queue.CanAcceptJob();

        result.Should().BeFalse();
    }

    [Fact]
    public async Task CanAcceptJob_AfterMarkCompleted_ReturnsTrue()
    {
        var queue = CreateQueue(maxSlots: 2);

        await queue.EnqueueAsync(CreateJob("run-1"), CancellationToken.None);
        await queue.EnqueueAsync(CreateJob("run-2"), CancellationToken.None);

        queue.CanAcceptJob().Should().BeFalse();

        queue.MarkCompleted("run-1");

        queue.CanAcceptJob().Should().BeTrue();
    }

    [Fact]
    public void ActiveSlots_InitiallyZero()
    {
        var queue = CreateQueue();

        queue.ActiveSlots.Should().Be(0);
    }

    [Fact]
    public async Task ActiveSlots_IncrementsOnEnqueue()
    {
        var queue = CreateQueue();

        await queue.EnqueueAsync(CreateJob("run-1"), CancellationToken.None);
        await queue.EnqueueAsync(CreateJob("run-2"), CancellationToken.None);

        queue.ActiveSlots.Should().Be(2);
    }

    [Fact]
    public async Task ActiveSlots_DecrementsOnMarkCompleted()
    {
        var queue = CreateQueue();

        await queue.EnqueueAsync(CreateJob("run-1"), CancellationToken.None);
        await queue.EnqueueAsync(CreateJob("run-2"), CancellationToken.None);

        queue.MarkCompleted("run-1");

        queue.ActiveSlots.Should().Be(1);
    }

    [Fact]
    public void MaxSlots_ReturnsConfiguredValue()
    {
        var queue = CreateQueue(maxSlots: 8);

        queue.MaxSlots.Should().Be(8);
    }
}
