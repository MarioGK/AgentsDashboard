using System.Reflection;
using System.Text.Json;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.TaskRuntime;
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
    public async Task HandleJobEventAsync_WhenStructuredDiffEvent_PersistsDiffSnapshotAndPublishesStructuredEvents()
    {
        const string runId = "run-structured-1";
        var timestamp = new DateTime(2026, 2, 17, 10, 30, 0, DateTimeKind.Utc);

        var store = new Mock<IOrchestratorStore>(MockBehavior.Loose);
        store.Setup(s => s.AddRunLogAsync(It.IsAny<RunLogEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        store.Setup(s => s.AppendRunStructuredEventAsync(It.IsAny<RunStructuredEventDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RunStructuredEventDocument document, CancellationToken _) =>
            {
                document.RepositoryId = "repo-structured";
                document.TaskId = "task-structured";
                document.TimestampUtc = timestamp;
                document.CreatedAtUtc = timestamp;
                return document;
            });
        store.Setup(s => s.UpsertRunDiffSnapshotAsync(It.IsAny<RunDiffSnapshotDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RunDiffSnapshotDocument snapshot, CancellationToken _) => snapshot);

        var publisher = new Mock<IRunEventPublisher>(MockBehavior.Loose);
        publisher.Setup(p => p.PublishLogAsync(It.IsAny<RunLogEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        publisher.Setup(p => p.PublishStructuredEventChangedAsync(
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        publisher.Setup(p => p.PublishDiffUpdatedAsync(
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var diff = new RunStructuredDiffSnapshot(
            Sequence: 42,
            Category: "diff.updated",
            Summary: "diff updated",
            DiffStat: "1 file changed, 1 insertion(+)",
            DiffPatch: "diff --git a/file.txt b/file.txt\n--- a/file.txt\n+++ b/file.txt\n@@ -1 +1 @@\n-old\n+new",
            Payload: "{\"diffPatch\":\"diff --git a/file.txt b/file.txt\"}",
            Schema: "harness-structured-event-v2",
            TimestampUtc: timestamp);
        var structuredViewService = new Mock<IRunStructuredViewService>(MockBehavior.Strict);
        structuredViewService.Setup(s => s.ApplyStructuredEventAsync(It.IsAny<RunStructuredEventDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RunStructuredProjectionDelta(
                new RunStructuredViewSnapshot(runId, 42, [], [], [], diff, timestamp),
                diff,
                null));
        structuredViewService.Setup(s => s.GetViewAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RunStructuredViewSnapshot(runId, 42, [], [], [], diff, timestamp));

        var dispatcher = new RunDispatcher(
            Mock.Of<IMagicOnionClientFactory>(),
            store.Object,
            Mock.Of<ITaskRuntimeLifecycleManager>(),
            Mock.Of<ISecretCryptoService>(),
            publisher.Object,
            new InMemoryYarpConfigProvider(),
            Options.Create(new OrchestratorOptions()),
            NullLogger<RunDispatcher>.Instance);

        var service = new TaskRuntimeEventListenerService(
            Mock.Of<IMagicOnionClientFactory>(),
            Mock.Of<ITaskRuntimeLifecycleManager>(),
            store.Object,
            Mock.Of<ITaskSemanticEmbeddingService>(),
            Mock.Of<ITaskRuntimeRegistryService>(),
            publisher.Object,
            new InMemoryYarpConfigProvider(),
            dispatcher,
            NullLogger<TaskRuntimeEventListenerService>.Instance,
            structuredViewService.Object);

        await InvokeHandleJobEventAsync(service, CreateStructuredMessage(runId, timestamp));

        store.Verify(s => s.AppendRunStructuredEventAsync(
                It.Is<RunStructuredEventDocument>(document =>
                    document.RunId == runId &&
                    document.Sequence == 42 &&
                    document.Category == "diff.updated"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        store.Verify(s => s.UpsertRunDiffSnapshotAsync(
                It.Is<RunDiffSnapshotDocument>(snapshot =>
                    snapshot.RunId == runId &&
                    snapshot.Sequence == 42 &&
                    snapshot.DiffStat == "1 file changed, 1 insertion(+)" &&
                    snapshot.DiffPatch.Contains("diff --git a/file.txt b/file.txt", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()),
            Times.Once);
        publisher.Verify(p => p.PublishStructuredEventChangedAsync(
                runId,
                It.IsAny<long>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        publisher.Verify(p => p.PublishDiffUpdatedAsync(
                runId,
                42,
                "diff.updated",
                It.IsAny<string>(),
                "harness-structured-event-v2",
                timestamp,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Test]
    public async Task HandleJobEventAsync_WhenMessageHasNoStructuredFields_DoesNotPersistStructuredState()
    {
        const string runId = "run-log-only";

        var store = new Mock<IOrchestratorStore>(MockBehavior.Loose);
        var publisher = new Mock<IRunEventPublisher>(MockBehavior.Loose);
        publisher.Setup(p => p.PublishLogAsync(It.IsAny<RunLogEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var dispatcher = new RunDispatcher(
            Mock.Of<IMagicOnionClientFactory>(),
            store.Object,
            Mock.Of<ITaskRuntimeLifecycleManager>(),
            Mock.Of<ISecretCryptoService>(),
            publisher.Object,
            new InMemoryYarpConfigProvider(),
            Options.Create(new OrchestratorOptions()),
            NullLogger<RunDispatcher>.Instance);

        var service = new TaskRuntimeEventListenerService(
            Mock.Of<IMagicOnionClientFactory>(),
            Mock.Of<ITaskRuntimeLifecycleManager>(),
            store.Object,
            Mock.Of<ITaskSemanticEmbeddingService>(),
            Mock.Of<ITaskRuntimeRegistryService>(),
            publisher.Object,
            new InMemoryYarpConfigProvider(),
            dispatcher,
            NullLogger<TaskRuntimeEventListenerService>.Instance);

        await InvokeHandleJobEventAsync(service, new JobEventMessage
        {
            RunId = runId,
            EventType = "log_chunk",
            Summary = "chunk line",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        });

        store.Verify(s => s.AppendRunStructuredEventAsync(It.IsAny<RunStructuredEventDocument>(), It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(s => s.UpsertRunDiffSnapshotAsync(It.IsAny<RunDiffSnapshotDocument>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task HandleJobEventAsync_WhenRunCompletes_DispatchesNextQueuedRunForTask()
    {
        var repository = CreateRepository();
        var task = CreateTask(repository.Id);
        var completedRun = CreateRun("run-completed", task.Id, repository.Id, RunState.Running, new DateTime(2026, 2, 16, 11, 0, 0, DateTimeKind.Utc), workerId: "worker-1");
        completedRun.OutputJson = "{\"status\":\"succeeded\"}";
        var nextQueuedRun = CreateRun("run-queued-1", task.Id, repository.Id, RunState.Queued, new DateTime(2026, 2, 16, 11, 1, 0, DateTimeKind.Utc));
        var queuedBehindRun = CreateRun("run-queued-2", task.Id, repository.Id, RunState.Queued, new DateTime(2026, 2, 16, 11, 2, 0, DateTimeKind.Utc));
        var startedRun = CreateRun(nextQueuedRun.Id, task.Id, repository.Id, RunState.Running, nextQueuedRun.CreatedAtUtc, workerId: "worker-2");

        var store = new Mock<IOrchestratorStore>(MockBehavior.Loose);
        store.Setup(s => s.AddRunLogAsync(It.IsAny<RunLogEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        store.Setup(s => s.UpdateTaskGitMetadataAsync(
                It.IsAny<string>(),
                It.IsAny<DateTime?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((TaskDocument?)null);
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

        var workerClient = new Mock<ITaskRuntimeGatewayService>(MockBehavior.Loose);
        workerClient.Setup(c => c.DispatchJobAsync(It.IsAny<DispatchJobRequest>()))
            .Returns(UnaryResult.FromResult(new DispatchJobReply { Success = true, DispatchedAt = DateTimeOffset.UtcNow }));

        var clientFactory = new Mock<IMagicOnionClientFactory>(MockBehavior.Loose);
        clientFactory.Setup(f => f.CreateTaskRuntimeGatewayService("worker-2", It.IsAny<string>()))
            .Returns(workerClient.Object);

        var lifecycleManager = new Mock<ITaskRuntimeLifecycleManager>(MockBehavior.Loose);
        lifecycleManager.Setup(l => l.RecycleTaskRuntimeAsync("worker-1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        lifecycleManager.Setup(l => l.AcquireTaskRuntimeForDispatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TaskRuntimeLease("worker-2", "container-2", "http://worker.local:6201", "http://worker.local:9080"));
        lifecycleManager.Setup(l => l.RecordDispatchActivityAsync("worker-2", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        lifecycleManager.Setup(l => l.GetTaskRuntimeAsync("worker-2", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TaskRuntimeInstance?)null);

        var publisher = new Mock<IRunEventPublisher>(MockBehavior.Loose);
        publisher.Setup(p => p.PublishLogAsync(It.IsAny<RunLogEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        publisher.Setup(p => p.PublishStatusAsync(It.IsAny<RunDocument>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        publisher.Setup(p => p.PublishRouteAvailableAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var semanticEmbedding = new Mock<ITaskSemanticEmbeddingService>(MockBehavior.Loose);

        var dispatcher = new RunDispatcher(
            clientFactory.Object,
            store.Object,
            lifecycleManager.Object,
            Mock.Of<ISecretCryptoService>(),
            publisher.Object,
            new InMemoryYarpConfigProvider(),
            Options.Create(new OrchestratorOptions()),
            NullLogger<RunDispatcher>.Instance);

        var service = new TaskRuntimeEventListenerService(
            clientFactory.Object,
            lifecycleManager.Object,
            store.Object,
            semanticEmbedding.Object,
            Mock.Of<ITaskRuntimeRegistryService>(),
            publisher.Object,
            new InMemoryYarpConfigProvider(),
            dispatcher,
            NullLogger<TaskRuntimeEventListenerService>.Instance);

        await InvokeHandleJobEventAsync(service, CreateCompletedMessage(completedRun.Id, succeeded: true));

        workerClient.Verify(c => c.DispatchJobAsync(It.Is<DispatchJobRequest>(request => request.RunId == nextQueuedRun.Id)), Times.Once);
        lifecycleManager.Verify(l => l.RecycleTaskRuntimeAsync("worker-1", It.IsAny<CancellationToken>()), Times.Never);
        store.Verify(
            s => s.UpdateTaskGitMetadataAsync(
                task.Id,
                It.Is<DateTime?>(value => value.HasValue),
                string.Empty,
                It.IsAny<CancellationToken>()),
            Times.Once);
        semanticEmbedding.Verify(
            x => x.QueueTaskEmbedding(repository.Id, task.Id, "run-output", completedRun.Id, null),
            Times.Once);
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
        store.Setup(s => s.UpdateTaskGitMetadataAsync(
                It.IsAny<string>(),
                It.IsAny<DateTime?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((TaskDocument?)null);
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

        var lifecycleManager = new Mock<ITaskRuntimeLifecycleManager>(MockBehavior.Loose);
        lifecycleManager.Setup(l => l.RecycleTaskRuntimeAsync("worker-1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var publisher = new Mock<IRunEventPublisher>(MockBehavior.Loose);
        publisher.Setup(p => p.PublishLogAsync(It.IsAny<RunLogEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        publisher.Setup(p => p.PublishStatusAsync(It.IsAny<RunDocument>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var semanticEmbedding = new Mock<ITaskSemanticEmbeddingService>(MockBehavior.Loose);

        var dispatcher = new RunDispatcher(
            Mock.Of<IMagicOnionClientFactory>(),
            store.Object,
            lifecycleManager.Object,
            Mock.Of<ISecretCryptoService>(),
            publisher.Object,
            new InMemoryYarpConfigProvider(),
            Options.Create(new OrchestratorOptions()),
            NullLogger<RunDispatcher>.Instance);

        var service = new TaskRuntimeEventListenerService(
            Mock.Of<IMagicOnionClientFactory>(),
            lifecycleManager.Object,
            store.Object,
            semanticEmbedding.Object,
            Mock.Of<ITaskRuntimeRegistryService>(),
            publisher.Object,
            new InMemoryYarpConfigProvider(),
            dispatcher,
            NullLogger<TaskRuntimeEventListenerService>.Instance);

        await InvokeHandleJobEventAsync(service, CreateCompletedMessage(completedRun.Id, succeeded: true));

        lifecycleManager.Verify(l => l.AcquireTaskRuntimeForDispatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task HandleJobEventAsync_WhenRunDispositionIsObsolete_MarksRunObsolete()
    {
        var repository = CreateRepository();
        var task = CreateTask(repository.Id);
        var runningRun = CreateRun("run-obsolete", task.Id, repository.Id, RunState.Running, new DateTime(2026, 2, 16, 13, 0, 0, DateTimeKind.Utc), workerId: "worker-1");
        var succeededRun = CreateRun(runningRun.Id, task.Id, repository.Id, RunState.Succeeded, runningRun.CreatedAtUtc, workerId: "worker-1");
        var obsoleteRun = CreateRun(runningRun.Id, task.Id, repository.Id, RunState.Obsolete, runningRun.CreatedAtUtc, workerId: "worker-1");

        var store = new Mock<IOrchestratorStore>(MockBehavior.Loose);
        store.Setup(s => s.AddRunLogAsync(It.IsAny<RunLogEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        store.Setup(s => s.UpdateTaskGitMetadataAsync(
                It.IsAny<string>(),
                It.IsAny<DateTime?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((TaskDocument?)null);
        store.Setup(s => s.MarkRunCompletedAsync(
                runningRun.Id,
                true,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()))
            .ReturnsAsync(succeededRun);
        store.Setup(s => s.MarkRunObsoleteAsync(runningRun.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(obsoleteRun);
        store.Setup(s => s.GetTaskAsync(task.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);
        store.Setup(s => s.ListRunsByTaskAsync(task.Id, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var lifecycleManager = new Mock<ITaskRuntimeLifecycleManager>(MockBehavior.Loose);
        lifecycleManager.Setup(l => l.RecycleTaskRuntimeAsync("worker-1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var publisher = new Mock<IRunEventPublisher>(MockBehavior.Loose);
        publisher.Setup(p => p.PublishLogAsync(It.IsAny<RunLogEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        publisher.Setup(p => p.PublishStatusAsync(It.IsAny<RunDocument>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var semanticEmbedding = new Mock<ITaskSemanticEmbeddingService>(MockBehavior.Loose);

        var dispatcher = new RunDispatcher(
            Mock.Of<IMagicOnionClientFactory>(),
            store.Object,
            lifecycleManager.Object,
            Mock.Of<ISecretCryptoService>(),
            publisher.Object,
            new InMemoryYarpConfigProvider(),
            Options.Create(new OrchestratorOptions()),
            NullLogger<RunDispatcher>.Instance);

        var service = new TaskRuntimeEventListenerService(
            Mock.Of<IMagicOnionClientFactory>(),
            lifecycleManager.Object,
            store.Object,
            semanticEmbedding.Object,
            Mock.Of<ITaskRuntimeRegistryService>(),
            publisher.Object,
            new InMemoryYarpConfigProvider(),
            dispatcher,
            NullLogger<TaskRuntimeEventListenerService>.Instance);

        await InvokeHandleJobEventAsync(
            service,
            CreateCompletedMessage(runningRun.Id, succeeded: true, metadata: new Dictionary<string, string>
            {
                ["runDisposition"] = "obsolete",
                ["obsoleteReason"] = "no-diff"
            }));

        store.Verify(s => s.MarkRunObsoleteAsync(runningRun.Id, It.IsAny<CancellationToken>()), Times.Once);
        publisher.Verify(
            p => p.PublishStatusAsync(It.Is<RunDocument>(run => run.State == RunState.Obsolete), It.IsAny<CancellationToken>()),
            Times.Once);
        store.Verify(
            s => s.UpdateTaskGitMetadataAsync(
                task.Id,
                It.Is<DateTime?>(value => value.HasValue),
                string.Empty,
                It.IsAny<CancellationToken>()),
            Times.Once);
        store.Verify(
            s => s.CreateFindingFromFailureAsync(It.IsAny<RunDocument>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Test]
    public async Task HandleJobEventAsync_WhenGitWorkflowFails_PersistsGitSyncError()
    {
        var repository = CreateRepository();
        var task = CreateTask(repository.Id);
        var runningRun = CreateRun("run-git-failed", task.Id, repository.Id, RunState.Running, new DateTime(2026, 2, 16, 14, 0, 0, DateTimeKind.Utc), workerId: "worker-1");
        var failedRun = CreateRun(runningRun.Id, task.Id, repository.Id, RunState.Failed, runningRun.CreatedAtUtc, workerId: "worker-1");

        var store = new Mock<IOrchestratorStore>(MockBehavior.Loose);
        store.Setup(s => s.AddRunLogAsync(It.IsAny<RunLogEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        store.Setup(s => s.UpdateTaskGitMetadataAsync(
                It.IsAny<string>(),
                It.IsAny<DateTime?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((TaskDocument?)null);
        store.Setup(s => s.MarkRunCompletedAsync(
                runningRun.Id,
                false,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()))
            .ReturnsAsync(failedRun);
        store.Setup(s => s.GetTaskAsync(task.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(task);
        store.Setup(s => s.ListRunsByTaskAsync(task.Id, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var lifecycleManager = new Mock<ITaskRuntimeLifecycleManager>(MockBehavior.Loose);
        lifecycleManager.Setup(l => l.RecycleTaskRuntimeAsync("worker-1", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var publisher = new Mock<IRunEventPublisher>(MockBehavior.Loose);
        publisher.Setup(p => p.PublishLogAsync(It.IsAny<RunLogEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        publisher.Setup(p => p.PublishStatusAsync(It.IsAny<RunDocument>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var semanticEmbedding = new Mock<ITaskSemanticEmbeddingService>(MockBehavior.Loose);

        var dispatcher = new RunDispatcher(
            Mock.Of<IMagicOnionClientFactory>(),
            store.Object,
            lifecycleManager.Object,
            Mock.Of<ISecretCryptoService>(),
            publisher.Object,
            new InMemoryYarpConfigProvider(),
            Options.Create(new OrchestratorOptions()),
            NullLogger<RunDispatcher>.Instance);

        var service = new TaskRuntimeEventListenerService(
            Mock.Of<IMagicOnionClientFactory>(),
            lifecycleManager.Object,
            store.Object,
            semanticEmbedding.Object,
            Mock.Of<ITaskRuntimeRegistryService>(),
            publisher.Object,
            new InMemoryYarpConfigProvider(),
            dispatcher,
            NullLogger<TaskRuntimeEventListenerService>.Instance);

        await InvokeHandleJobEventAsync(
            service,
            CreateCompletedMessage(runningRun.Id, succeeded: false, metadata: new Dictionary<string, string>
            {
                ["gitWorkflow"] = "failed",
                ["gitFailure"] = "push failed"
            }));

        store.Verify(
            s => s.UpdateTaskGitMetadataAsync(
                task.Id,
                It.Is<DateTime?>(value => value.HasValue),
                "push failed",
                It.IsAny<CancellationToken>()),
            Times.Once);
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
        string workerId = "")
    {
        return new RunDocument
        {
            Id = id,
            TaskId = taskId,
            RepositoryId = repositoryId,
            State = state,
            TaskRuntimeId = workerId,
            CreatedAtUtc = createdAtUtc
        };
    }

    private static JobEventMessage CreateCompletedMessage(string runId, bool succeeded, Dictionary<string, string>? metadata = null)
    {
        var status = succeeded ? "succeeded" : "failed";
        var payloadMetadata = metadata is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(metadata, StringComparer.Ordinal);
        var payload = JsonSerializer.Serialize(new HarnessResultEnvelope
        {
            Status = status,
            Summary = "done",
            Error = string.Empty,
            Metadata = payloadMetadata
        });
        var mergedMetadata = new Dictionary<string, string>(StringComparer.Ordinal);
        mergedMetadata["payload"] = payload;

        return new JobEventMessage
        {
            RunId = runId,
            EventType = "completed",
            Summary = "done",
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Metadata = mergedMetadata
        };
    }

    private static JobEventMessage CreateStructuredMessage(string runId, DateTime timestampUtc)
    {
        return new JobEventMessage
        {
            RunId = runId,
            EventType = "assistant.delta",
            Summary = "delta",
            Sequence = 42,
            Category = "diff.updated",
            PayloadJson = "{\"diffPatch\":\"diff --git a/file.txt b/file.txt\",\"diffStat\":\"1 file changed, 1 insertion(+)\",\"summary\":\"diff updated\"}",
            SchemaVersion = "harness-structured-event-v2",
            Timestamp = new DateTimeOffset(timestampUtc).ToUnixTimeMilliseconds(),
        };
    }

    private static async Task InvokeHandleJobEventAsync(TaskRuntimeEventListenerService service, JobEventMessage message)
    {
        var method = typeof(TaskRuntimeEventListenerService).GetMethod(
            "HandleJobEventAsync",
            BindingFlags.Instance | BindingFlags.NonPublic);

        method.Should().NotBeNull();

        var invocation = method!.Invoke(service, [message]);
        invocation.Should().BeAssignableTo<Task>();

        await ((Task)invocation!).WaitAsync(TimeSpan.FromSeconds(1));
    }
}
