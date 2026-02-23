




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

        await Assert.That(snapshot.Count()).IsEqualTo(2);
        await Assert.That(snapshot.Any(id => id.Equals("run-a", StringComparison.OrdinalIgnoreCase))).IsTrue();
        await Assert.That(snapshot.Any(id => id.Equals("run-b", StringComparison.OrdinalIgnoreCase))).IsTrue();

        await Assert.That(queue.ActiveRunIds.Count()).IsEqualTo(1);
        await Assert.That(queue.ActiveRunIds).Contains("Run-B");
        await Assert.That(queue.ActiveRunIds).DoesNotContain("run-a");
    }

    [Test]
    public async Task CanAcceptJob_TracksCapacityWhenJobsComplete()
    {
        var queue = CreateQueue(maxSlots: 1);

        await Assert.That(queue.CanAcceptJob()).IsTrue();
        await queue.EnqueueAsync(CreateJob("run-1"), CancellationToken.None);

        await Assert.That(queue.CanAcceptJob()).IsFalse();
        await Assert.That(queue.ActiveSlots).IsEqualTo(1);

        queue.MarkCompleted("run-1");
        await Assert.That(queue.CanAcceptJob()).IsTrue();
        await Assert.That(queue.ActiveSlots).IsEqualTo(0);
    }

    [Test]
    public async Task Cancel_IsSafeAndCaseInsensitive()
    {
        var queue = CreateQueue();
        await queue.EnqueueAsync(CreateJob("Run-Case"), CancellationToken.None);

        await Assert.That(queue.Cancel("run-case")).IsTrue();
        await Assert.That(queue.Cancel("run-case")).IsTrue();
    }
}
