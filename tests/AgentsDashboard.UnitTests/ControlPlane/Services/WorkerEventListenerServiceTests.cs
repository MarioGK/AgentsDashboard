using System.Reflection;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Proxy;
using AgentsDashboard.ControlPlane.Services;
using MagicOnion;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class WorkerEventListenerServiceTests
{
    [Test]
    public async Task HandleJobEventAsync_WhenRunCompletes_DispatchesNextQueuedRunForTask()
    {
        var repository = CreateRepository();
        var task = CreateTask(repository.Id);
        var completedRun = CreateRun("run-completed", task.Id, repository.Id, RunState.Running, new DateTime(2026, 2, 16, 11, 0, 0, DateTimeKind.Utc), workerId: "worker-1");
        var nextQueuedRun = CreateRun("run-queued-1", task.Id, repository.Id, RunState.Queued, new DateTime(2026, 2, 16, 11, 1, 0, DateTimeKind.Utc));
        var queuedBehindRun = CreateRun("run-queued-2", task.Id, repository.Id, RunState.Queued, new DateTime(2026, 2, 16, 11, 2, 0, DateTimeKind.Utc));
        var startedRun = CreateRun(nextQueuedRun.Id, task.Id, repository.Id, RunState.Running, nextQueuedRun.CreatedAtUtc, workerId: "worker-2");

        var store = new Mock<IOrchestratorStore>(MockBehavior.Loose);
        store.Setup(s => s.AddRunLogAsync(It.IsAny<RunLogEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        store.Setup(s => s.MarkRunCompletedAsync(
                completedRun.Id,
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()))
            .ReturnsAsync(completedRun);
        store.Setup(s => s.GetTaskAsync(task.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);
        store.Setup(s => s.GetRepositoryAsync(repository.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(repository);
        store.Setup(s => s.ListRunsByTaskAsync(task.Id, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([queuedBehindRun, nextQueuedRun]);
        store.Setup(s => s.CountRunsByStateAsync(RunState.Queued, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        store.Setup(s => s.CountActiveRunsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        store.Setup(s => s.CountActiveRunsByRepoAsync(repository.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        store.Setup(s => s.ListProviderSecretsAsync(repository.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        store.Setup(s => s.GetHarnessProviderSettingsAsync(repository.Id, task.Harness, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HarnessProviderSettingsDocument?)null);
        store.Setup(s => s.GetInstructionsAsync(repository.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        store.Setup(s => s.GetSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSettingsDocument { Orchestrator = new OrchestratorSettings() });
        store.Setup(s => s.MarkRunStartedAsync(
                nextQueuedRun.Id,
                "worker-2",
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()))
            .ReturnsAsync(startedRun);

        var workerClient = new Mock<IWorkerGatewayService>(MockBehavior.Loose);
        workerClient.Setup(c => c.DispatchJobAsync(It.IsAny<DispatchJobRequest>()))
            .Returns(UnaryResult.FromResult(new DispatchJobReply { Success = true, DispatchedAt = DateTimeOffset.UtcNow }));

        var clientFactory = new Mock<IMagicOnionClientFactory>(MockBehavior.Loose);
        clientFactory.Setup(f => f.CreateWorkerGatewayService("worker-2", It.IsAny<string>()))
            .Returns(workerClient.Object);

        var lifecycleManager = new Mock<IWorkerLifecycleManager>(MockBehavior.Loose);
        lifecycleManager.Setup(l => l.RecycleWorkerAsync("worker-1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        lifecycleManager.Setup(l => l.AcquireWorkerForDispatchAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkerLease("worker-2", "container-2", "http://worker.local:6201", "http://worker.local:9080"));
        lifecycleManager.Setup(l => l.RecordDispatchActivityAsync("worker-2", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        lifecycleManager.Setup(l => l.GetWorkerAsync("worker-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkerRuntime?)null);

        var publisher = new Mock<IRunEventPublisher>(MockBehavior.Loose);
        publisher.Setup(p => p.PublishLogAsync(It.IsAny<RunLogEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        publisher.Setup(p => p.PublishStatusAsync(It.IsAny<RunDocument>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        publisher.Setup(p => p.PublishRouteAvailableAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var dispatcher = new RunDispatcher(
            clientFactory.Object,
            store.Object,
            lifecycleManager.Object,
            Mock.Of<ISecretCryptoService>(),
            publisher.Object,
            new InMemoryYarpConfigProvider(),
            Options.Create(new OrchestratorOptions()),
            NullLogger<RunDispatcher>.Instance);

        var service = new WorkerEventListenerService(
            clientFactory.Object,
            lifecycleManager.Object,
            store.Object,
            Mock.Of<IWorkerRegistryService>(),
            publisher.Object,
            new InMemoryYarpConfigProvider(),
            dispatcher,
            NullLogger<WorkerEventListenerService>.Instance);

        await InvokeHandleJobEventAsync(service, CreateCompletedMessage(completedRun.Id, succeeded: true));

        workerClient.Verify(c => c.DispatchJobAsync(It.Is<DispatchJobRequest>(request => request.RunId == nextQueuedRun.Id)), Times.Once);
        lifecycleManager.Verify(l => l.RecycleWorkerAsync("worker-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task HandleJobEventAsync_WhenTaskHasNoQueuedRuns_DoesNotDispatch()
    {
        var repository = CreateRepository();
        var task = CreateTask(repository.Id);
        var completedRun = CreateRun("run-completed", task.Id, repository.Id, RunState.Running, new DateTime(2026, 2, 16, 12, 0, 0, DateTimeKind.Utc), workerId: "worker-1");

        var store = new Mock<IOrchestratorStore>(MockBehavior.Loose);
        store.Setup(s => s.AddRunLogAsync(It.IsAny<RunLogEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        store.Setup(s => s.MarkRunCompletedAsync(
                completedRun.Id,
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()))
            .ReturnsAsync(completedRun);
        store.Setup(s => s.GetTaskAsync(task.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);
        store.Setup(s => s.GetRepositoryAsync(repository.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(repository);
        store.Setup(s => s.ListRunsByTaskAsync(task.Id, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var lifecycleManager = new Mock<IWorkerLifecycleManager>(MockBehavior.Loose);
        lifecycleManager.Setup(l => l.RecycleWorkerAsync("worker-1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var publisher = new Mock<IRunEventPublisher>(MockBehavior.Loose);
        publisher.Setup(p => p.PublishLogAsync(It.IsAny<RunLogEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        publisher.Setup(p => p.PublishStatusAsync(It.IsAny<RunDocument>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var dispatcher = new RunDispatcher(
            Mock.Of<IMagicOnionClientFactory>(),
            store.Object,
            lifecycleManager.Object,
            Mock.Of<ISecretCryptoService>(),
            publisher.Object,
            new InMemoryYarpConfigProvider(),
            Options.Create(new OrchestratorOptions()),
            NullLogger<RunDispatcher>.Instance);

        var service = new WorkerEventListenerService(
            Mock.Of<IMagicOnionClientFactory>(),
            lifecycleManager.Object,
            store.Object,
            Mock.Of<IWorkerRegistryService>(),
            publisher.Object,
            new InMemoryYarpConfigProvider(),
            dispatcher,
            NullLogger<WorkerEventListenerService>.Instance);

        await InvokeHandleJobEventAsync(service, CreateCompletedMessage(completedRun.Id, succeeded: true));

        lifecycleManager.Verify(l => l.AcquireWorkerForDispatchAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static RepositoryDocument CreateRepository() => new()
    {
        Id = "repo-1",
        Name = "repo",
        GitUrl = "https://github.com/org/repo.git",
        DefaultBranch = "main"
    };

    private static TaskDocument CreateTask(string repositoryId) => new()
    {
        Id = "task-1",
        RepositoryId = repositoryId,
        Name = "Task",
        Harness = "codex"
    };

    private static RunDocument CreateRun(
        string id,
        string taskId,
        string repositoryId,
        RunState state,
        DateTime createdAtUtc,
        string workerId = "") => new()
    {
        Id = id,
        TaskId = taskId,
        RepositoryId = repositoryId,
        State = state,
        WorkerId = workerId,
        CreatedAtUtc = createdAtUtc
    };

    private static JobEventMessage CreateCompletedMessage(string runId, bool succeeded)
    {
        var status = succeeded ? "succeeded" : "failed";
        var payload = $"{{\"Status\":\"{status}\",\"Summary\":\"done\",\"Error\":\"\",\"Metadata\":{{}}}}";

        return new JobEventMessage
        {
            RunId = runId,
            EventType = "completed",
            Summary = "done",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Metadata = new Dictionary<string, string>
            {
                ["payload"] = payload
            }
        };
    }

    private static async Task InvokeHandleJobEventAsync(WorkerEventListenerService service, JobEventMessage message)
    {
        var method = typeof(WorkerEventListenerService).GetMethod(
            "HandleJobEventAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        method.Should().NotBeNull();

        var invocation = method!.Invoke(service, [message]);
        invocation.Should().BeAssignableTo<Task>();

        await ((Task)invocation!).WaitAsync(TimeSpan.FromSeconds(1));
    }
}
