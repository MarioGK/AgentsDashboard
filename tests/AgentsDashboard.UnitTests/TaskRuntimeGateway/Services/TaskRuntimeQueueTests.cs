using AgentsDashboard.Contracts.TaskRuntime;
using AgentsDashboard.TaskRuntime.Configuration;
using AgentsDashboard.TaskRuntime.Models;
using AgentsDashboard.TaskRuntime.Services;

namespace AgentsDashboard.UnitTests.TaskRuntime.Services;

public class TaskRuntimeQueueTests
{
    private static TaskRuntimeQueue CreateQueue(int maxSlots = 3) => new(new TaskRuntimeOptions { MaxSlots = maxSlots });

    private static QueuedJob CreateJob(string runId) => new()
    {
        Request = new DispatchJobRequest
        {
            RunId = runId,
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

        Assert.That(snapshot.Count()).IsEqualTo(2);
        Assert.That(snapshot.Any(id => id.Equals("run-a", StringComparison.OrdinalIgnoreCase))).IsTrue();
        Assert.That(snapshot.Any(id => id.Equals("run-b", StringComparison.OrdinalIgnoreCase))).IsTrue();

        Assert.That(queue.ActiveRunIds.Count()).IsEqualTo(1);
        Assert.That(queue.ActiveRunIds).Contains("Run-B");
        Assert.That(queue.ActiveRunIds).DoesNotContain("run-a");
    }

    [Test]
    public async Task CanAcceptJob_TracksCapacityWhenJobsComplete()
    {
        var queue = CreateQueue(maxSlots: 1);

        Assert.That(queue.CanAcceptJob()).IsTrue();
        await queue.EnqueueAsync(CreateJob("run-1"), CancellationToken.None);

        Assert.That(queue.CanAcceptJob()).IsFalse();
        Assert.That(queue.ActiveSlots).IsEqualTo(1);

        queue.MarkCompleted("run-1");
        Assert.That(queue.CanAcceptJob()).IsTrue();
        Assert.That(queue.ActiveSlots).IsEqualTo(0);
    }

    [Test]
    public async Task Cancel_IsSafeAndCaseInsensitive()
    {
        var queue = CreateQueue();
        await queue.EnqueueAsync(CreateJob("Run-Case"), CancellationToken.None);

        Assert.That(queue.Cancel("run-case")).IsTrue();
        Assert.That(queue.Cancel("run-case")).IsTrue();
    }
}
