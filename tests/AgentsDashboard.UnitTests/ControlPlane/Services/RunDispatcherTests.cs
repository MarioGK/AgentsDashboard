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

public class RunDispatcherTests
{
    [Test]
    public async Task DispatchAsync_WithApprovalRequirementOnly_MarksRunPendingApproval()
    {
        var service = new SutBuilder().Build();
        var run = CreateRun();
        var task = CreateTask("task-approve", requireApproval: true);
        var project = CreateProject();
        var repo = CreateRepository();

        service.Store
            .Setup(s => s.MarkRunPendingApprovalAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PendingRun(run));
        service.PublisherMock
            .Setup(p => p.PublishStatusAsync(It.IsAny<AgentsDashboard.Contracts.Domain.RunDocument>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await service.Dispatcher.DispatchAsync(project, repo, task, run, CancellationToken.None);

        result.Should().BeTrue();
        service.WorkerLifecycleManagerMock.Verify(
            x => x.AcquireWorkerForDispatchAsync(It.IsAny<CancellationToken>()),
            Times.Never);
        service.Store.Verify(
            x => x.MarkRunCompletedAsync(
                It.IsAny<string>(),
                It.IsAny<bool>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()),
            Times.Never);
    }

    [Test]
    public async Task DispatchAsync_WhenNoWorkerAvailable_ReturnsFalseWithoutDispatch()
    {
        var service = new SutBuilder().Build();
        var run = CreateRun();
        var task = CreateTask();
        var project = CreateProject();
        var repo = CreateRepository();
        service.Store.Setup(s => s.CountActiveRunsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
        service.Store.Setup(s => s.CountActiveRunsByProjectAsync(project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        service.Store.Setup(s => s.CountActiveRunsByRepoAsync(repo.Id, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        service.Store.Setup(s => s.CountActiveRunsByTaskAsync(task.Id, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        service.WorkerLifecycleManagerMock.Setup(x => x.AcquireWorkerForDispatchAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkerLease?)null);

        var result = await service.Dispatcher.DispatchAsync(project, repo, task, run, CancellationToken.None);

        result.Should().BeFalse();
        service.WorkerClientMock.Verify(
            c => c.DispatchJobAsync(It.IsAny<DispatchJobRequest>()),
            Times.Never);
        service.Store.Verify(
            s => s.MarkRunCompletedAsync(
                It.IsAny<string>(),
                false,
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(),
                It.IsAny<string?>()),
            Times.Never);
    }

    [Test]
    public async Task DispatchAsync_WhenWorkerRejectsRun_MarksRunAsFailed()
    {
        var service = new SutBuilder().WithActiveWorker().Build();
        var run = CreateRun();
        var task = CreateTask();
        var project = CreateProject();
        var repo = CreateRepository();
        var failedRun = new RunDocument { Id = run.Id, State = RunState.Failed };
        service.Store.Setup(s => s.CountActiveRunsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
        service.Store.Setup(s => s.CountActiveRunsByProjectAsync(project.Id, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        service.Store.Setup(s => s.CountActiveRunsByRepoAsync(repo.Id, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        service.Store.Setup(s => s.CountActiveRunsByTaskAsync(task.Id, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        service.Store.Setup(s => s.MarkRunCompletedAsync(run.Id, false, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(failedRun);
        service.Store.Setup(s => s.ListProviderSecretsAsync(repo.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        service.Store.Setup(s => s.GetHarnessProviderSettingsAsync(repo.Id, task.Harness, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HarnessProviderSettingsDocument?)null);
        service.Store.Setup(s => s.GetInstructionsAsync(repo.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        service.WorkerClientMock
            .Setup(c => c.DispatchJobAsync(It.IsAny<DispatchJobRequest>()))
            .Returns(UnaryResult.FromResult(new DispatchJobReply { Success = false, ErrorMessage = "worker unavailable" }));

        var result = await service.Dispatcher.DispatchAsync(project, repo, task, run, CancellationToken.None);

        result.Should().BeFalse();
        service.Store.Verify(s => s.MarkRunCompletedAsync(
            run.Id,
            false,
            "Dispatch failed: worker unavailable",
            "{}",
            It.IsAny<CancellationToken>(),
            It.IsAny<string?>(),
            It.IsAny<string?>()), Times.Once);
        service.Store.Verify(s => s.MarkRunStartedAsync(run.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ProjectDocument CreateProject() => new()
    {
        Id = "project-1",
        Name = "Project"
    };

    private static RepositoryDocument CreateRepository() => new()
    {
        Id = "repo-1",
        ProjectId = "project-1",
        Name = "repo",
        GitUrl = "https://github.com/org/repo.git",
        DefaultBranch = "main"
    };

    private static TaskDocument CreateTask(string id = "task-1", bool requireApproval = false, int? concurrencyLimit = null) => new()
    {
        Id = id,
        Name = "Task",
        Harness = "codex",
        ConcurrencyLimit = concurrencyLimit ?? 0,
        ApprovalProfile = new ApprovalProfileConfig(RequireApproval: requireApproval)
    };

    private static RunDocument CreateRun(string id = "run-1") => new()
    {
        Id = id,
        ProjectId = "project-1",
        RepositoryId = "repo-1",
        TaskId = "task-1",
        State = RunState.Queued
    };

    private static RunDocument PendingRun(RunDocument run) =>
        new()
        {
            Id = run.Id,
            ProjectId = run.ProjectId,
            RepositoryId = run.RepositoryId,
            TaskId = run.TaskId,
            State = RunState.PendingApproval
        };

    private sealed class SutBuilder
    {
        public Mock<IOrchestratorStore> Store { get; } = new();
        public Mock<IWorkerLifecycleManager> WorkerLifecycleManagerMock { get; } = new();
        public Mock<ISecretCryptoService> SecretCryptoMock { get; } = new();
        public Mock<IRunEventPublisher> PublisherMock { get; } = new();
        public Mock<IMagicOnionClientFactory> ClientFactoryMock { get; } = new();
        public Mock<IWorkerGatewayService> WorkerClientMock { get; } = new();
        public RunDispatcher Dispatcher { get; }

        private readonly OrchestratorOptions _options = new();
        private readonly InMemoryYarpConfigProvider _yarpProvider = new();

        public SutBuilder()
        {
            WorkerLifecycleManagerMock.Setup(x => x.AcquireWorkerForDispatchAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
                new WorkerLease("worker-1", "container-1", "http://worker.local:5201", "http://worker.local:8080"));
            ClientFactoryMock
                .Setup(f => f.CreateWorkerGatewayService(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(WorkerClientMock.Object);
            Store.Setup(s => s.CountActiveRunsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
            Store.Setup(s => s.CountActiveRunsByProjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
            Store.Setup(s => s.CountActiveRunsByRepoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
            Store.Setup(s => s.CountActiveRunsByTaskAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
            Store.Setup(s => s.ListProviderSecretsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Store.Setup(s => s.GetHarnessProviderSettingsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((HarnessProviderSettingsDocument?)null);
            Store.Setup(s => s.GetInstructionsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Store.Setup(s => s.MarkRunCompletedAsync(It.IsAny<string>(), false, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<string?>()))
                .ReturnsAsync((string runId, bool _, string _, string _, CancellationToken _, string? _, string? _) =>
                new RunDocument { Id = runId, State = RunState.Failed });
            Store.Setup(s => s.MarkRunStartedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string runId, string _, CancellationToken _) => new RunDocument { Id = runId, State = RunState.Running });
            Store.Setup(s => s.CreateFindingFromFailureAsync(It.IsAny<RunDocument>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FindingDocument());
            Store.Setup(s => s.ListAllRunIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Dispatcher = new RunDispatcher(
                ClientFactoryMock.Object,
                Store.Object,
                WorkerLifecycleManagerMock.Object,
                SecretCryptoMock.Object,
                PublisherMock.Object,
                _yarpProvider,
                Options.Create(_options),
                NullLogger<RunDispatcher>.Instance);
        }

        public SutBuilder WithActiveWorker()
        {
            WorkerLifecycleManagerMock
                .Setup(x => x.AcquireWorkerForDispatchAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new WorkerLease("worker-1", "container-1", "http://worker.local:5201", "http://worker.local:8080"));
            return this;
        }

        public SutBuilder Build()
        {
            return this;
        }
    }
}
