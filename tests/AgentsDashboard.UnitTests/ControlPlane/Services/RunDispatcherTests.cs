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

public class RunDispatcherTests
{
    [Test]
    public async Task DispatchAsync_WithApprovalRequirementOnly_MarksRunPendingApproval()
    {
        var service = new SutBuilder().Build();
        var run = CreateRun();
        var task = CreateTask("task-approve", requireApproval: true);
        var repo = CreateRepository();

        service.Store
            .Setup(s => s.MarkRunPendingApprovalAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PendingRun(run));
        service.PublisherMock
            .Setup(p => p.PublishStatusAsync(It.IsAny<AgentsDashboard.Contracts.Domain.RunDocument>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await service.Dispatcher.DispatchAsync(repo, task, run, CancellationToken.None);

        result.Should().BeTrue();
        service.WorkerLifecycleManagerMock.Verify(
            x => x.AcquireTaskRuntimeForDispatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
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
        var repo = CreateRepository();
        service.Store.Setup(s => s.CountActiveRunsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
        service.Store.Setup(s => s.CountActiveRunsByRepoAsync(repo.Id, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        service.Store.Setup(s => s.CountActiveRunsByTaskAsync(task.Id, It.IsAny<CancellationToken>())).ReturnsAsync(0);
        service.WorkerLifecycleManagerMock.Setup(x => x.AcquireTaskRuntimeForDispatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TaskRuntimeLease?)null);

        var result = await service.Dispatcher.DispatchAsync(repo, task, run, CancellationToken.None);

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
    [Arguments(RunState.Queued)]
    [Arguments(RunState.Running)]
    [Arguments(RunState.PendingApproval)]
    public async Task DispatchAsync_WhenOlderActiveRunExistsForTask_LeavesRunQueued(RunState blockingState)
    {
        var service = new SutBuilder().Build();
        var baseTime = new DateTime(2026, 2, 16, 12, 0, 0, DateTimeKind.Utc);
        var run = CreateRun("run-2", createdAtUtc: baseTime.AddMinutes(1));
        var blockingRun = CreateRun("run-1", blockingState, baseTime);
        var task = CreateTask();
        var repo = CreateRepository();

        service.Store.Setup(s => s.ListRunsByTaskAsync(task.Id, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([run, blockingRun]);

        var result = await service.Dispatcher.DispatchAsync(repo, task, run, CancellationToken.None);

        result.Should().BeFalse();
        service.WorkerLifecycleManagerMock.Verify(x => x.AcquireTaskRuntimeForDispatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        service.Store.Verify(s => s.MarkRunPendingApprovalAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task DispatchAsync_WhenRunIsTaskQueueHead_DispatchesRun()
    {
        var service = new SutBuilder().WithActiveWorker().Build();
        var baseTime = new DateTime(2026, 2, 16, 13, 0, 0, DateTimeKind.Utc);
        var run = CreateRun("run-1", createdAtUtc: baseTime);
        var queuedBehind = CreateRun("run-2", createdAtUtc: baseTime.AddMinutes(1));
        var task = CreateTask();
        var repo = CreateRepository();

        service.Store.Setup(s => s.ListRunsByTaskAsync(task.Id, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([queuedBehind, run]);
        service.WorkerClientMock
            .Setup(c => c.DispatchJobAsync(It.IsAny<DispatchJobRequest>()))
            .Returns(UnaryResult.FromResult(new DispatchJobReply { Success = true, DispatchedAt = DateTimeOffset.UtcNow }));

        var result = await service.Dispatcher.DispatchAsync(repo, task, run, CancellationToken.None);

        result.Should().BeTrue();
        service.Store.Verify(s => s.MarkRunStartedAsync(
            run.Id,
            "worker-1",
            It.IsAny<CancellationToken>(),
            It.IsAny<string?>(),
            It.IsAny<string?>(),
            It.IsAny<string?>()), Times.Once);
    }

    [Test]
    public async Task DispatchAsync_PersistsDeterministicTaskGitMetadata()
    {
        var service = new SutBuilder().WithActiveWorker().Build();
        var run = CreateRun();
        var task = CreateTask();
        var repo = CreateRepository();

        service.WorkerClientMock
            .Setup(c => c.DispatchJobAsync(It.IsAny<DispatchJobRequest>()))
            .Returns(UnaryResult.FromResult(new DispatchJobReply { Success = true, DispatchedAt = DateTimeOffset.UtcNow }));

        var result = await service.Dispatcher.DispatchAsync(repo, task, run, CancellationToken.None);

        result.Should().BeTrue();
        service.Store.Verify(s => s.UpdateTaskGitMetadataAsync(
            task.Id,
            null,
            string.Empty,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task DispatchAsync_ForCodexDefaultMode_SetsAppServerTransportAndApprovalDefaults()
    {
        var service = new SutBuilder().WithActiveWorker().Build();
        var run = CreateRun();
        run.ExecutionMode = HarnessExecutionMode.Default;
        var task = CreateTask();
        var repo = CreateRepository();
        DispatchJobRequest? dispatchedRequest = null;

        service.WorkerClientMock
            .Setup(c => c.DispatchJobAsync(It.IsAny<DispatchJobRequest>()))
            .Callback<DispatchJobRequest>(request => dispatchedRequest = request)
            .Returns(UnaryResult.FromResult(new DispatchJobReply { Success = true, DispatchedAt = DateTimeOffset.UtcNow }));

        var result = await service.Dispatcher.DispatchAsync(repo, task, run, CancellationToken.None);

        result.Should().BeTrue();
        dispatchedRequest.Should().NotBeNull();
        dispatchedRequest!.Mode.Should().Be(HarnessExecutionMode.Default);
        dispatchedRequest.EnvironmentVars.Should().ContainKey("CODEX_TRANSPORT").WhoseValue.Should().Be("app-server");
        dispatchedRequest.EnvironmentVars.Should().ContainKey("CODEX_APPROVAL_POLICY").WhoseValue.Should().Be("on-failure");
        dispatchedRequest.EnvironmentVars.Should().ContainKey("TASK_MODE").WhoseValue.Should().Be("default");
        dispatchedRequest.EnvironmentVars.Should().ContainKey("RUN_MODE").WhoseValue.Should().Be("default");
    }

    [Test]
    public async Task DispatchAsync_ForCodexReviewMode_UsesReadOnlyApprovalPolicy()
    {
        var service = new SutBuilder().WithActiveWorker().Build();
        var run = CreateRun();
        run.ExecutionMode = HarnessExecutionMode.Review;
        var task = CreateTask();
        var repo = CreateRepository();
        DispatchJobRequest? dispatchedRequest = null;

        service.WorkerClientMock
            .Setup(c => c.DispatchJobAsync(It.IsAny<DispatchJobRequest>()))
            .Callback<DispatchJobRequest>(request => dispatchedRequest = request)
            .Returns(UnaryResult.FromResult(new DispatchJobReply { Success = true, DispatchedAt = DateTimeOffset.UtcNow }));

        var result = await service.Dispatcher.DispatchAsync(repo, task, run, CancellationToken.None);

        result.Should().BeTrue();
        dispatchedRequest.Should().NotBeNull();
        dispatchedRequest!.Mode.Should().Be(HarnessExecutionMode.Review);
        dispatchedRequest.EnvironmentVars.Should().ContainKey("CODEX_APPROVAL_POLICY").WhoseValue.Should().Be("never");
        dispatchedRequest.EnvironmentVars.Should().ContainKey("TASK_MODE").WhoseValue.Should().Be("review");
        dispatchedRequest.EnvironmentVars.Should().ContainKey("RUN_MODE").WhoseValue.Should().Be("review");
    }

    [Test]
    public async Task DispatchAsync_ForZaiHarness_MapsRepositoryZaiSecretToExpectedEnvironmentVariables()
    {
        var service = new SutBuilder().WithActiveWorker().Build();
        var run = CreateRun();
        run.ExecutionMode = HarnessExecutionMode.Default;
        var task = CreateTask(harness: "zai");
        var repo = CreateRepository();
        DispatchJobRequest? dispatchedRequest = null;

        service.Store.Setup(s => s.ListProviderSecretsAsync(repo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ProviderSecretDocument
                {
                    RepositoryId = repo.Id,
                    Provider = "zai",
                    EncryptedValue = "enc-zai",
                },
            ]);
        service.SecretCryptoMock
            .Setup(s => s.Decrypt("enc-zai"))
            .Returns("zai-secret");
        service.WorkerClientMock
            .Setup(c => c.DispatchJobAsync(It.IsAny<DispatchJobRequest>()))
            .Callback<DispatchJobRequest>(request => dispatchedRequest = request)
            .Returns(UnaryResult.FromResult(new DispatchJobReply { Success = true, DispatchedAt = DateTimeOffset.UtcNow }));

        var result = await service.Dispatcher.DispatchAsync(repo, task, run, CancellationToken.None);

        result.Should().BeTrue();
        dispatchedRequest.Should().NotBeNull();
        dispatchedRequest!.EnvironmentVars.Should().ContainKey("Z_AI_API_KEY").WhoseValue.Should().Be("zai-secret");
        dispatchedRequest.EnvironmentVars.Should().ContainKey("ANTHROPIC_AUTH_TOKEN").WhoseValue.Should().Be("zai-secret");
        dispatchedRequest.EnvironmentVars.Should().ContainKey("ANTHROPIC_API_KEY").WhoseValue.Should().Be("zai-secret");
        dispatchedRequest.EnvironmentVars.Should().ContainKey("ANTHROPIC_BASE_URL").WhoseValue.Should().Be("https://api.z.ai/api/anthropic");
        dispatchedRequest.EnvironmentVars.Should().ContainKey("HARNESS_MODEL").WhoseValue.Should().Be("glm-5");
        dispatchedRequest.EnvironmentVars.Should().ContainKey("ZAI_MODEL").WhoseValue.Should().Be("glm-5");
    }

    [Test]
    public async Task DispatchAsync_ForZaiHarness_UsesGlobalLlmTornadoSecretFallbackWhenRepositorySecretMissing()
    {
        var service = new SutBuilder().WithActiveWorker().Build();
        var run = CreateRun();
        run.ExecutionMode = HarnessExecutionMode.Default;
        var task = CreateTask(harness: "zai");
        var repo = CreateRepository();
        DispatchJobRequest? dispatchedRequest = null;

        service.Store.Setup(s => s.GetProviderSecretAsync("global", "llmtornado", It.IsAny<CancellationToken>()))
            .ReturnsAsync(
                new ProviderSecretDocument
                {
                    RepositoryId = "global",
                    Provider = "llmtornado",
                    EncryptedValue = "enc-global-llmtornado",
                });
        service.SecretCryptoMock
            .Setup(s => s.Decrypt("enc-global-llmtornado"))
            .Returns("global-zai-secret");
        service.WorkerClientMock
            .Setup(c => c.DispatchJobAsync(It.IsAny<DispatchJobRequest>()))
            .Callback<DispatchJobRequest>(request => dispatchedRequest = request)
            .Returns(UnaryResult.FromResult(new DispatchJobReply { Success = true, DispatchedAt = DateTimeOffset.UtcNow }));

        var result = await service.Dispatcher.DispatchAsync(repo, task, run, CancellationToken.None);

        result.Should().BeTrue();
        dispatchedRequest.Should().NotBeNull();
        dispatchedRequest!.EnvironmentVars.Should().ContainKey("Z_AI_API_KEY").WhoseValue.Should().Be("global-zai-secret");
        dispatchedRequest.EnvironmentVars.Should().ContainKey("ANTHROPIC_AUTH_TOKEN").WhoseValue.Should().Be("global-zai-secret");
        dispatchedRequest.EnvironmentVars.Should().ContainKey("ANTHROPIC_API_KEY").WhoseValue.Should().Be("global-zai-secret");
        dispatchedRequest.EnvironmentVars.Should().ContainKey("ANTHROPIC_BASE_URL").WhoseValue.Should().Be("https://api.z.ai/api/anthropic");
        dispatchedRequest.EnvironmentVars.Should().ContainKey("HARNESS_MODEL").WhoseValue.Should().Be("glm-5");
        dispatchedRequest.EnvironmentVars.Should().ContainKey("ZAI_MODEL").WhoseValue.Should().Be("glm-5");
    }

    [Test]
    public async Task DispatchAsync_WhenWorkerRejectsRun_MarksRunAsFailed()
    {
        var service = new SutBuilder().WithActiveWorker().Build();
        var run = CreateRun();
        var task = CreateTask();
        var repo = CreateRepository();
        var failedRun = new RunDocument { Id = run.Id, State = RunState.Failed };
        service.Store.Setup(s => s.CountActiveRunsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
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

        var result = await service.Dispatcher.DispatchAsync(repo, task, run, CancellationToken.None);

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

    private static RepositoryDocument CreateRepository() => new()
    {
        Id = "repo-1",
        Name = "repo",
        GitUrl = "https://github.com/org/repo.git",
        DefaultBranch = "main"
    };

    private static TaskDocument CreateTask(
        string id = "task-1",
        bool requireApproval = false,
        int? concurrencyLimit = null,
        string harness = "codex") =>
        new()
        {
            Id = id,
            Name = "Task",
            Harness = harness,
            ConcurrencyLimit = concurrencyLimit ?? 0,
            ApprovalProfile = new ApprovalProfileConfig(RequireApproval: requireApproval)
        };

    private static RunDocument CreateRun(
        string id = "run-1",
        RunState state = RunState.Queued,
        DateTime? createdAtUtc = null)
    {
        return new RunDocument
        {
            Id = id,
            RepositoryId = "repo-1",
            TaskId = "task-1",
            State = state,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };
    }

    private static RunDocument PendingRun(RunDocument run) =>
        new()
        {
            Id = run.Id,
            RepositoryId = run.RepositoryId,
            TaskId = run.TaskId,
            State = RunState.PendingApproval
        };

    private sealed class SutBuilder
    {
        public Mock<IOrchestratorStore> Store { get; } = new();
        public Mock<ITaskRuntimeLifecycleManager> WorkerLifecycleManagerMock { get; } = new();
        public Mock<ISecretCryptoService> SecretCryptoMock { get; } = new();
        public Mock<IRunEventPublisher> PublisherMock { get; } = new();
        public Mock<IMagicOnionClientFactory> ClientFactoryMock { get; } = new();
        public Mock<ITaskRuntimeGatewayService> WorkerClientMock { get; } = new();
        public RunDispatcher Dispatcher { get; }

        private readonly OrchestratorOptions _options = new();
        private readonly InMemoryYarpConfigProvider _yarpProvider = new();

        public SutBuilder()
        {
            WorkerLifecycleManagerMock.Setup(x => x.AcquireTaskRuntimeForDispatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(
                new TaskRuntimeLease("worker-1", "container-1", "http://worker.local:5201", "http://worker.local:8080"));
            WorkerLifecycleManagerMock.Setup(x => x.RecordDispatchActivityAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            ClientFactoryMock
                .Setup(f => f.CreateTaskRuntimeGatewayService(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(WorkerClientMock.Object);
            Store.Setup(s => s.CountActiveRunsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(0);
            Store.Setup(s => s.CountActiveRunsByRepoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
            Store.Setup(s => s.CountActiveRunsByTaskAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(0);
            Store.Setup(s => s.ListRunsByTaskAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Store.Setup(s => s.ListProviderSecretsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Store.Setup(s => s.GetHarnessProviderSettingsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((HarnessProviderSettingsDocument?)null);
            Store.Setup(s => s.GetInstructionsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Store.Setup(s => s.UpdateTaskGitMetadataAsync(
                    It.IsAny<string>(),
                    It.IsAny<DateTime?>(),
                    It.IsAny<string?>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((TaskDocument?)null);
            Store.Setup(s => s.GetSettingsAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SystemSettingsDocument { Orchestrator = new OrchestratorSettings() });
            Store.Setup(s => s.MarkRunCompletedAsync(It.IsAny<string>(), false, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string?>(), It.IsAny<string?>()))
                .ReturnsAsync((string runId, bool _, string _, string _, CancellationToken _, string? _, string? _) =>
                new RunDocument { Id = runId, State = RunState.Failed });
            Store.Setup(s => s.MarkRunStartedAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>(),
                    It.IsAny<string?>()))
                .ReturnsAsync((string runId, string _, CancellationToken _, string? _, string? _, string? _) =>
                    new RunDocument { Id = runId, State = RunState.Running });
            Store.Setup(s => s.CreateFindingFromFailureAsync(It.IsAny<RunDocument>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FindingDocument());
            Store.Setup(s => s.ListAllRunIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
            PublisherMock.Setup(p => p.PublishStatusAsync(It.IsAny<RunDocument>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            PublisherMock.Setup(p => p.PublishRouteAvailableAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
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
                .Setup(x => x.AcquireTaskRuntimeForDispatchAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new TaskRuntimeLease("worker-1", "container-1", "http://worker.local:5201", "http://worker.local:8080"));
            return this;
        }

        public SutBuilder Build()
        {
            return this;
        }
    }
}
