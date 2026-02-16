using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Proxy;
using AgentsDashboard.ControlPlane.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class WorkflowExecutorTests
{
    [Test]
    public async Task ExecuteWorkflowAsync_WithNoStages_CompletesImmediately()
    {
        var now = new DateTimeOffset(2026, 2, 16, 8, 0, 0, TimeSpan.Zero);
        var execution = CreateExecution();
        var workflow = new WorkflowDocument
        {
            Id = "workflow-1",
            RepositoryId = "repo-1",
            Stages = []
        };

        var store = new Mock<IOrchestratorStore>(MockBehavior.Strict);
        store.Setup(s => s.CreateWorkflowExecutionAsync(It.IsAny<WorkflowExecutionDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(execution);
        store.Setup(s => s.UpdateWorkflowExecutionAsync(It.IsAny<WorkflowExecutionDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowExecutionDocument e, CancellationToken _) => e);

        var completed = new TaskCompletionSource<WorkflowExecutionState>(TaskCreationOptions.RunContinuationsAsynchronously);
        store
            .Setup(s => s.MarkWorkflowExecutionCompletedAsync(execution.Id, WorkflowExecutionState.Succeeded, string.Empty, It.IsAny<CancellationToken>()))
            .Callback<string, WorkflowExecutionState, string, CancellationToken>((_, state, _, _) => completed.TrySetResult(state))
            .ReturnsAsync(execution);

        var options = Options.Create(new OrchestratorOptions());
        var executor = new WorkflowExecutor(
            store.Object,
            new RunDispatcher(Mock.Of<IMagicOnionClientFactory>(), store.Object, Mock.Of<IWorkerLifecycleManager>(), Mock.Of<ISecretCryptoService>(), Mock.Of<IRunEventPublisher>(), new InMemoryYarpConfigProvider(), options, NullLogger<RunDispatcher>.Instance),
            Mock.Of<IContainerReaper>(),
            options,
            NullLogger<WorkflowExecutor>.Instance,
            timeProvider: new StaticTimeProvider(now));

        await executor.ExecuteWorkflowAsync(workflow, CancellationToken.None);

        var state = await completed.Task.WaitAsync(TimeSpan.FromSeconds(1));
        state.Should().Be(WorkflowExecutionState.Succeeded);
    }

    [Test]
    public async Task ExecuteWorkflowAsync_TaskStageFailsWhenTaskMissing()
    {
        var now = new DateTimeOffset(2026, 2, 16, 8, 1, 0, TimeSpan.Zero);
        var execution = CreateExecution();
        var workflow = new WorkflowDocument
        {
            Id = "workflow-2",
            RepositoryId = "repo-1",
            Stages =
            [
                new WorkflowStageConfig
                {
                    Id = "stage-1",
                    Name = "Missing task",
                    Type = WorkflowStageType.Task,
                    TaskId = "task-missing",
                    Order = 0
                }
            ]
        };

        var failedExecution = CreateExecution();
        failedExecution.Id = execution.Id;
        failedExecution.State = WorkflowExecutionState.Failed;

        var store = new Mock<IOrchestratorStore>(MockBehavior.Strict);
        store.Setup(s => s.CreateWorkflowExecutionAsync(It.IsAny<WorkflowExecutionDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(execution);
        store.Setup(s => s.GetTaskAsync("task-missing", It.IsAny<CancellationToken>()))
            .ReturnsAsync((TaskDocument?)null);
        store.Setup(s => s.CreateFindingFromFailureAsync(
                It.IsAny<RunDocument>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FindingDocument());
        store.Setup(s => s.UpdateWorkflowExecutionAsync(It.IsAny<WorkflowExecutionDocument>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkflowExecutionDocument e, CancellationToken _) => e);

        var completed = new TaskCompletionSource<WorkflowExecutionState>(TaskCreationOptions.RunContinuationsAsynchronously);
        store.Setup(s => s.MarkWorkflowExecutionCompletedAsync(execution.Id, WorkflowExecutionState.Failed, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, WorkflowExecutionState, string, CancellationToken>((_, state, _, _) => completed.TrySetResult(state))
            .ReturnsAsync(failedExecution);

        var options = Options.Create(new OrchestratorOptions());
        var executor = new WorkflowExecutor(
            store.Object,
            new RunDispatcher(
                Mock.Of<IMagicOnionClientFactory>(),
                store.Object,
                Mock.Of<IWorkerLifecycleManager>(),
                Mock.Of<ISecretCryptoService>(),
                Mock.Of<IRunEventPublisher>(),
                new InMemoryYarpConfigProvider(),
                options,
                NullLogger<RunDispatcher>.Instance),
            Mock.Of<IContainerReaper>(),
            options,
            NullLogger<WorkflowExecutor>.Instance,
            new StaticTimeProvider(now));

        await executor.ExecuteWorkflowAsync(workflow, CancellationToken.None);

        var state = await completed.Task.WaitAsync(TimeSpan.FromSeconds(1));
        state.Should().Be(WorkflowExecutionState.Failed);
        store.Verify(s => s.MarkWorkflowExecutionCompletedAsync(execution.Id, WorkflowExecutionState.Failed, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static WorkflowExecutionDocument CreateExecution() => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        WorkflowId = "workflow-1",
        RepositoryId = "repo-1",
        State = WorkflowExecutionState.Running
    };

    private sealed class StaticTimeProvider(DateTimeOffset initialTime) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => initialTime;
    }
}
