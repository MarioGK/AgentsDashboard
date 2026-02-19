using System.Reflection;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.TaskRuntime;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Services;
using MagicOnion;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public partial class RunDispatcherTests
{
    private static readonly MethodInfo ResolveTaskModelOverrideMethod = typeof(RunDispatcher)
        .GetMethod("ResolveTaskModelOverride", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ApplyHarnessModelOverrideMethod = typeof(RunDispatcher)
        .GetMethod("ApplyHarnessModelOverride", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ApplyHarnessModeEnvironmentMethod = typeof(RunDispatcher)
        .GetMethod("ApplyHarnessModeEnvironment", BindingFlags.NonPublic | BindingFlags.Static)!;

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
    public async Task DispatchAsync_ForCodexDefaultMode_SetsCodexApprovalDefaults()
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
        dispatchedRequest.EnvironmentVars.Should().ContainKey("CODEX_TRANSPORT").WhoseValue.Should().Be("stdio");
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
    public void ResolveTaskModelOverride_WhenModelOverrideInstructionPresent_ReturnsTrimmedModel()
    {
        var task = CreateTask();
        task.InstructionFiles = [new InstructionFile("modeloverride.md", "  gpt-4o-mini ", 1)];

        var modelOverride = (string)ResolveTaskModelOverrideMethod.Invoke(null, [task])!;

        modelOverride.Should().Be("gpt-4o-mini");
    }

    [Test]
    public void ResolveTaskModelOverride_WhenHarnessModelInstructionPresent_ReturnsTrimmedModel()
    {
        var task = CreateTask();
        task.InstructionFiles =
        [
            new InstructionFile("modeloverride.md", "ignored"),
            new InstructionFile("hARNeSS-model.json", "  gpt-4 "),
        ];

        var modelOverride = (string)ResolveTaskModelOverrideMethod.Invoke(null, [task])!;

        modelOverride.Should().Be("ignored");
    }

    [Test]
    public void ApplyHarnessModelOverride_WhenCodex_HonorsCoreAndHarnessModelVariables()
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        ApplyHarnessModelOverrideMethod.Invoke(null, ["codex", environment, "  gpt-4o-mini  "]);

        environment.Should().ContainKey("HARNESS_MODEL").WhoseValue.Should().Be("gpt-4o-mini");
        environment.Should().ContainKey("CODEX_MODEL").WhoseValue.Should().Be("gpt-4o-mini");
    }

    [Test]
    public void ApplyHarnessModelOverride_WhenOpencode_SetsHarnessAndOpencodeModelVariables()
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        ApplyHarnessModelOverrideMethod.Invoke(null, ["opencode", environment, "provider/model  "]);

        environment.Should().ContainKey("HARNESS_MODEL").WhoseValue.Should().Be("provider/model");
        environment.Should().ContainKey("OPENCODE_MODEL").WhoseValue.Should().Be("provider/model");
    }

    [Test]
    public void ApplyHarnessModeEnvironment_ForCodex_UsesStdioTransportAndKeepsExplicitCodexMode()
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["CODEX_TRANSPORT"] = "command",
            ["CODEX_MODE"] = "command",
            ["TASK_MODE"] = "review",
        };

        ApplyHarnessModeEnvironmentMethod.Invoke(null, ["codex", HarnessExecutionMode.Plan, environment]);

        environment.Should().ContainKey("CODEX_TRANSPORT").WhoseValue.Should().Be("stdio");
        environment.Should().ContainKey("CODEX_MODE").WhoseValue.Should().Be("command");
        environment.Should().ContainKey("CODEX_APPROVAL_POLICY").WhoseValue.Should().Be("never");
        environment.Should().ContainKey("TASK_MODE").WhoseValue.Should().Be("plan");
        environment.Should().ContainKey("RUN_MODE").WhoseValue.Should().Be("plan");
    }

    [Test]
    public void ApplyHarnessModeEnvironment_ForOpencode_SetsModeOnlyWhenMissing()
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["OPENCODE_MODE"] = "custom-mode",
            ["TASK_MODE"] = "review",
        };

        ApplyHarnessModeEnvironmentMethod.Invoke(null, ["opencode", HarnessExecutionMode.Review, environment]);

        environment.Should().ContainKey("OPENCODE_MODE").WhoseValue.Should().Be("custom-mode");
        environment.Should().ContainKey("TASK_MODE").WhoseValue.Should().Be("review");
        environment.Should().ContainKey("RUN_MODE").WhoseValue.Should().Be("review");
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

}
