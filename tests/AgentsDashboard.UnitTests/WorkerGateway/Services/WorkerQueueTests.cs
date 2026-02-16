using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.WorkerGateway.Configuration;
using AgentsDashboard.WorkerGateway.Models;
using AgentsDashboard.WorkerGateway.Services;

namespace AgentsDashboard.UnitTests.WorkerGateway.Services;

public class WorkerQueueTests
{
    private static WorkerQueue CreateQueue(int maxSlots = 3) => new(new WorkerOptions { MaxSlots = maxSlots });

    private static QueuedJob CreateJob(string runId) => new()
    {
        Request = new DispatchJobRequest
        {
            RunId = runId,
            ProjectId = "project-1",
            RepositoryId = "repo-1",
            TaskId = "task-1",
            HarnessType = "codex",
            ImageTag = "latest",
            CloneUrl = "https://github.com/example/repo.git",
            Instruction = "Run task"
        }
    };

    [Test]
    public async Task ActiveRunIds_ReturnsCaseInsensitiveSnapshot()
    {
        var queue = CreateQueue();
        await queue.EnqueueAsync(CreateJob("Run-A"), CancellationToken.None);
        await queue.EnqueueAsync(CreateJob("Run-B"), CancellationToken.None);

        var snapshot = queue.ActiveRunIds;

        queue.MarkCompleted("run-a");

        snapshot.Should().HaveCount(2);
        snapshot.Any(id => id.Equals("run-a", StringComparison.OrdinalIgnoreCase)).Should().BeTrue();
        snapshot.Any(id => id.Equals("run-b", StringComparison.OrdinalIgnoreCase)).Should().BeTrue();

        queue.ActiveRunIds.Should().HaveCount(1);
        queue.ActiveRunIds.Should().Contain("Run-B");
        queue.ActiveRunIds.Should().NotContain("run-a");
    }

    [Test]
    public async Task CanAcceptJob_TracksCapacityWhenJobsComplete()
    {
        var queue = CreateQueue(maxSlots: 2);

        queue.CanAcceptJob().Should().BeTrue();
        await queue.EnqueueAsync(CreateJob("run-1"), CancellationToken.None);
        await queue.EnqueueAsync(CreateJob("run-2"), CancellationToken.None);

        queue.CanAcceptJob().Should().BeFalse();
        queue.ActiveSlots.Should().Be(2);

        queue.MarkCompleted("run-1");
        queue.CanAcceptJob().Should().BeTrue();
        queue.ActiveSlots.Should().Be(1);
    }

    [Test]
    public async Task Cancel_IsSafeAndCaseInsensitive()
    {
        var queue = CreateQueue();
        await queue.EnqueueAsync(CreateJob("Run-Case"), CancellationToken.None);

        queue.Cancel("run-case").Should().BeTrue();
        queue.Cancel("run-case").Should().BeTrue();
    }
}
