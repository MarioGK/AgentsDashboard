using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Services;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using WorkerGatewayClient = AgentsDashboard.Contracts.Worker.WorkerGateway.WorkerGatewayClient;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class RunDispatcherTests
{
    [Fact]
    public void ParseGitHubRepoSlug_ParsesHttpsUrl()
    {
        var result = RunDispatcherTestsHelper.ParseGitHubRepoSlug("https://github.com/org/repo.git");

        result.Should().Be("org/repo");
    }

    [Fact]
    public void ParseGitHubRepoSlug_ParsesSshUrl()
    {
        var result = RunDispatcherTestsHelper.ParseGitHubRepoSlug("git@github.com:org/repo.git");

        result.Should().Be("org/repo");
    }

    [Fact]
    public void ParseGitHubRepoSlug_ParsesUrlWithoutGitSuffix()
    {
        var result = RunDispatcherTestsHelper.ParseGitHubRepoSlug("https://github.com/org/repo");

        result.Should().Be("org/repo");
    }

    [Fact]
    public void ParseGitHubRepoSlug_EmptyUrl_ReturnsEmpty()
    {
        var result = RunDispatcherTestsHelper.ParseGitHubRepoSlug("");

        result.Should().BeEmpty();
    }

    [Fact]
    public void ParseGitHubRepoSlug_NullUrl_ReturnsEmpty()
    {
        var result = RunDispatcherTestsHelper.ParseGitHubRepoSlug(null!);

        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://github.com/owner/repo.git", "owner/repo")]
    [InlineData("git@github.com:owner/repo.git", "owner/repo")]
    [InlineData("https://github.com/owner/repo", "owner/repo")]
    [InlineData("git@github.com:owner/repo", "owner/repo")]
    [InlineData("", "")]
    [InlineData("invalid", "invalid")]
    public void ParseGitHubRepoSlug_VariousInputs(string input, string expected)
    {
        var result = RunDispatcherTestsHelper.ParseGitHubRepoSlug(input);
        result.Should().Be(expected);
    }
}

public class RunDispatcherDispatchTests
{
    private readonly Mock<WorkerGatewayClient> _workerClientMock;
    private readonly Mock<IOrchestratorStore> _storeMock;
    private readonly Mock<ISecretCryptoService> _secretCryptoMock;
    private readonly Mock<IRunEventPublisher> _publisherMock;
    private readonly OrchestratorOptions _options;

    public RunDispatcherDispatchTests()
    {
        _workerClientMock = new Mock<WorkerGatewayClient>();
        _storeMock = new Mock<IOrchestratorStore>(MockBehavior.Loose);
        _secretCryptoMock = new Mock<ISecretCryptoService>(MockBehavior.Loose);
        _publisherMock = new Mock<IRunEventPublisher>();
        _options = new OrchestratorOptions
        {
            MaxGlobalConcurrentRuns = 50,
            PerProjectConcurrencyLimit = 10,
            PerRepoConcurrencyLimit = 5
        };
    }

    private TestableRunDispatcher CreateDispatcher()
    {
        return new TestableRunDispatcher(
            _workerClientMock.Object,
            _storeMock.Object,
            _secretCryptoMock.Object,
            _publisherMock.Object,
            Options.Create(_options),
            NullLogger<TestableRunDispatcher>.Instance);
    }

    private static ProjectDocument CreateProject(string id = "proj-1") => new()
    {
        Id = id,
        Name = "Test Project"
    };

    private static RepositoryDocument CreateRepository(string id = "repo-1", string projectId = "proj-1") => new()
    {
        Id = id,
        ProjectId = projectId,
        Name = "test-repo",
        GitUrl = "https://github.com/org/repo.git",
        DefaultBranch = "main"
    };

    private static TaskDocument CreateTask(string id = "task-1", bool requireApproval = false, int concurrencyLimit = 0) => new()
    {
        Id = id,
        Name = "Test Task",
        Harness = "codex",
        Command = "codex run",
        Prompt = "Test prompt",
        ApprovalProfile = new ApprovalProfileConfig(RequireApproval: requireApproval),
        ConcurrencyLimit = concurrencyLimit,
        Timeouts = new TimeoutConfig(ExecutionSeconds: 300),
        SandboxProfile = new SandboxProfileConfig(),
        ArtifactPolicy = new ArtifactPolicyConfig()
    };

    private static RunDocument CreateRun(string id = "run-1", string taskId = "task-1") => new()
    {
        Id = id,
        TaskId = taskId,
        State = RunState.Queued,
        Attempt = 1
    };

    private static RunDocument WithState(RunDocument run, RunState state)
    {
        run.State = state;
        return run;
    }

    private static TaskDocument WithAutoCreatePullRequest(TaskDocument task, bool autoCreate)
    {
        task.AutoCreatePullRequest = autoCreate;
        return task;
    }

    private static TaskDocument WithSandboxProfile(TaskDocument task, SandboxProfileConfig sandboxProfile)
    {
        task.SandboxProfile = sandboxProfile;
        return task;
    }

    [Fact]
    public async Task DispatchAsync_WithRequireApproval_MarksRunPendingApproval()
    {
        var dispatcher = CreateDispatcher();
        var project = CreateProject();
        var repo = CreateRepository();
        var task = CreateTask(requireApproval: true);
        var run = CreateRun();

        _storeMock.Setup(s => s.MarkRunPendingApprovalAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => WithState(CreateRun(run.Id, run.TaskId), RunState.PendingApproval));
        _publisherMock.Setup(p => p.PublishStatusAsync(It.IsAny<RunDocument>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await dispatcher.DispatchAsync(project, repo, task, run, CancellationToken.None);

        result.Should().BeTrue();
        _storeMock.Verify(s => s.MarkRunPendingApprovalAsync(run.Id, It.IsAny<CancellationToken>()), Times.Once);
        _publisherMock.Verify(p => p.PublishStatusAsync(It.IsAny<RunDocument>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_GlobalConcurrencyLimitReached_ReturnsFalse()
    {
        var dispatcher = CreateDispatcher();
        var project = CreateProject();
        var repo = CreateRepository();
        var task = CreateTask();
        var run = CreateRun();

        _options.MaxGlobalConcurrentRuns = 10;
        _storeMock.Setup(s => s.CountActiveRunsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(10);

        var result = await dispatcher.DispatchAsync(project, repo, task, run, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DispatchAsync_ProjectConcurrencyLimitReached_ReturnsFalse()
    {
        var dispatcher = CreateDispatcher();
        var project = CreateProject();
        var repo = CreateRepository();
        var task = CreateTask();
        var run = CreateRun();

        _options.PerProjectConcurrencyLimit = 5;
        _storeMock.Setup(s => s.CountActiveRunsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
        _storeMock.Setup(s => s.CountActiveRunsByProjectAsync(project.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        var result = await dispatcher.DispatchAsync(project, repo, task, run, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DispatchAsync_RepoConcurrencyLimitReached_ReturnsFalse()
    {
        var dispatcher = CreateDispatcher();
        var project = CreateProject();
        var repo = CreateRepository();
        var task = CreateTask();
        var run = CreateRun();

        _options.PerRepoConcurrencyLimit = 2;
        _storeMock.Setup(s => s.CountActiveRunsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
        _storeMock.Setup(s => s.CountActiveRunsByProjectAsync(project.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
        _storeMock.Setup(s => s.CountActiveRunsByRepoAsync(repo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);

        var result = await dispatcher.DispatchAsync(project, repo, task, run, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DispatchAsync_TaskConcurrencyLimitReached_ReturnsFalse()
    {
        var dispatcher = CreateDispatcher();
        var project = CreateProject();
        var repo = CreateRepository();
        var task = CreateTask(concurrencyLimit: 3);
        var run = CreateRun();

        _storeMock.Setup(s => s.CountActiveRunsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
        _storeMock.Setup(s => s.CountActiveRunsByProjectAsync(project.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
        _storeMock.Setup(s => s.CountActiveRunsByRepoAsync(repo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        _storeMock.Setup(s => s.CountActiveRunsByTaskAsync(task.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);

        var result = await dispatcher.DispatchAsync(project, repo, task, run, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DispatchAsync_WorkerRejects_MarksRunFailed()
    {
        var dispatcher = CreateDispatcher();
        var project = CreateProject();
        var repo = CreateRepository();
        var task = CreateTask();
        var run = CreateRun();

        SetupSuccessfulConcurrencyChecks();
        SetupSuccessfulInstructionRetrieval();
        _workerClientMock.Setup(c => c.DispatchJobAsync(It.IsAny<DispatchJobRequest>(), It.IsAny<CallOptions>()))
            .Returns(new AsyncUnaryCall<DispatchJobReply>(
                Task.FromResult(new DispatchJobReply { Accepted = false, Reason = "Worker busy" }),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => new Metadata()));
        _storeMock.Setup(s => s.MarkRunCompletedAsync(run.Id, false, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => WithState(CreateRun(run.Id, run.TaskId), RunState.Failed));

        var result = await dispatcher.DispatchAsync(project, repo, task, run, CancellationToken.None);

        result.Should().BeFalse();
        _storeMock.Verify(s => s.MarkRunCompletedAsync(run.Id, false, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_WorkerAccepts_MarksRunStarted()
    {
        var dispatcher = CreateDispatcher();
        var project = CreateProject();
        var repo = CreateRepository();
        var task = CreateTask();
        var run = CreateRun();

        SetupSuccessfulConcurrencyChecks();
        SetupSuccessfulInstructionRetrieval();
        _workerClientMock.Setup(c => c.DispatchJobAsync(It.IsAny<DispatchJobRequest>(), It.IsAny<CallOptions>()))
            .Returns(new AsyncUnaryCall<DispatchJobReply>(
                Task.FromResult(new DispatchJobReply { Accepted = true }),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => new Metadata()));
        _storeMock.Setup(s => s.MarkRunStartedAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => WithState(CreateRun(run.Id, run.TaskId), RunState.Running));
        _storeMock.Setup(s => s.ListProviderSecretsAsync(repo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _storeMock.Setup(s => s.GetHarnessProviderSettingsAsync(repo.Id, task.Harness, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HarnessProviderSettingsDocument?)null);

        var result = await dispatcher.DispatchAsync(project, repo, task, run, CancellationToken.None);

        result.Should().BeTrue();
        _storeMock.Verify(s => s.MarkRunStartedAsync(run.Id, It.IsAny<CancellationToken>()), Times.Once);
        _publisherMock.Verify(p => p.PublishStatusAsync(It.IsAny<RunDocument>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DispatchAsync_DecryptsSecretsAndAddsToEnvironment()
    {
        var dispatcher = CreateDispatcher();
        var project = CreateProject();
        var repo = CreateRepository();
        var task = CreateTask();
        var run = CreateRun();

        SetupSuccessfulConcurrencyChecks();
        SetupSuccessfulInstructionRetrieval();
        _storeMock.Setup(s => s.ListProviderSecretsAsync(repo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new ProviderSecretDocument { Provider = "github", EncryptedValue = "encrypted-gh" },
                new ProviderSecretDocument { Provider = "codex", EncryptedValue = "encrypted-codex" }
            ]);
        _secretCryptoMock.Setup(s => s.Decrypt("encrypted-gh")).Returns("gh-token-123");
        _secretCryptoMock.Setup(s => s.Decrypt("encrypted-codex")).Returns("codex-key-456");
        _storeMock.Setup(s => s.GetHarnessProviderSettingsAsync(repo.Id, task.Harness, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HarnessProviderSettingsDocument?)null);
        _workerClientMock.Setup(c => c.DispatchJobAsync(It.IsAny<DispatchJobRequest>(), It.IsAny<CallOptions>()))
            .Returns(new AsyncUnaryCall<DispatchJobReply>(
                Task.FromResult(new DispatchJobReply { Accepted = true }),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => new Metadata()));
        _storeMock.Setup(s => s.MarkRunStartedAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => WithState(CreateRun(run.Id, run.TaskId), RunState.Running));

        DispatchJobRequest? capturedRequest = null;
        _workerClientMock.Setup(c => c.DispatchJobAsync(It.IsAny<DispatchJobRequest>(), It.IsAny<CallOptions>()))
            .Callback<DispatchJobRequest, CallOptions>((req, _) => capturedRequest = req)
            .Returns(new AsyncUnaryCall<DispatchJobReply>(
                Task.FromResult(new DispatchJobReply { Accepted = true }),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => new Metadata()));

        await dispatcher.DispatchAsync(project, repo, task, run, CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Env.Should().ContainKey("GH_TOKEN");
        capturedRequest.Env["GH_TOKEN"].Should().Be("gh-token-123");
        capturedRequest.Env.Should().ContainKey("CODEX_API_KEY");
        capturedRequest.Env["CODEX_API_KEY"].Should().Be("codex-key-456");
    }

    [Fact]
    public async Task DispatchAsync_AddsHarnessSettingsToEnvironment()
    {
        var dispatcher = CreateDispatcher();
        var project = CreateProject();
        var repo = CreateRepository();
        var task = CreateTask();
        var run = CreateRun();

        SetupSuccessfulConcurrencyChecks();
        SetupSuccessfulInstructionRetrieval();
        _storeMock.Setup(s => s.ListProviderSecretsAsync(repo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _storeMock.Setup(s => s.GetHarnessProviderSettingsAsync(repo.Id, task.Harness, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HarnessProviderSettingsDocument
            {
                Model = "gpt-4",
                Temperature = 0.7,
                MaxTokens = 4096,
                AdditionalSettings = { ["custom-setting"] = "custom-value" }
            });
        _storeMock.Setup(s => s.MarkRunStartedAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => WithState(CreateRun(run.Id, run.TaskId), RunState.Running));

        DispatchJobRequest? capturedRequest = null;
        _workerClientMock.Setup(c => c.DispatchJobAsync(It.IsAny<DispatchJobRequest>(), It.IsAny<CallOptions>()))
            .Callback<DispatchJobRequest, CallOptions>((req, _) => capturedRequest = req)
            .Returns(new AsyncUnaryCall<DispatchJobReply>(
                Task.FromResult(new DispatchJobReply { Accepted = true }),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => new Metadata()));

        await dispatcher.DispatchAsync(project, repo, task, run, CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Env.Should().ContainKey("HARNESS_MODEL");
        capturedRequest.Env["HARNESS_MODEL"].Should().Be("gpt-4");
        capturedRequest.Env.Should().ContainKey("CODEX_MODEL");
        capturedRequest.Env["CODEX_MODEL"].Should().Be("gpt-4");
        capturedRequest.Env.Should().ContainKey("HARNESS_CUSTOM_SETTING");
        capturedRequest.Env["HARNESS_CUSTOM_SETTING"].Should().Be("custom-value");
    }

    [Fact]
    public async Task DispatchAsync_BuildsLayeredPrompt_WithRepoInstructions()
    {
        var dispatcher = CreateDispatcher();
        var project = CreateProject();
        var repo = CreateRepository();
        var task = CreateTask();
        var run = CreateRun();

        SetupSuccessfulConcurrencyChecks();
        _storeMock.Setup(s => s.GetInstructionsAsync(repo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new RepositoryInstructionDocument { Name = "coding-standards.md", Content = "Use async/await", Enabled = true, Priority = 1 }
            ]);
        _storeMock.Setup(s => s.ListProviderSecretsAsync(repo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _storeMock.Setup(s => s.GetHarnessProviderSettingsAsync(repo.Id, task.Harness, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HarnessProviderSettingsDocument?)null);
        _storeMock.Setup(s => s.MarkRunStartedAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => WithState(CreateRun(run.Id, run.TaskId), RunState.Running));

        DispatchJobRequest? capturedRequest = null;
        _workerClientMock.Setup(c => c.DispatchJobAsync(It.IsAny<DispatchJobRequest>(), It.IsAny<CallOptions>()))
            .Callback<DispatchJobRequest, CallOptions>((req, _) => capturedRequest = req)
            .Returns(new AsyncUnaryCall<DispatchJobReply>(
                Task.FromResult(new DispatchJobReply { Accepted = true }),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => new Metadata()));

        await dispatcher.DispatchAsync(project, repo, task, run, CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Prompt.Should().Contain("coding-standards.md");
        capturedRequest.Prompt.Should().Contain("Use async/await");
        capturedRequest.Prompt.Should().Contain("Task Prompt");
    }

    [Fact]
    public async Task DispatchAsync_SetsContainerLabels()
    {
        var dispatcher = CreateDispatcher();
        var project = CreateProject();
        var repo = CreateRepository();
        var task = CreateTask();
        var run = CreateRun();

        SetupSuccessfulConcurrencyChecks();
        SetupSuccessfulInstructionRetrieval();
        _storeMock.Setup(s => s.ListProviderSecretsAsync(repo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _storeMock.Setup(s => s.GetHarnessProviderSettingsAsync(repo.Id, task.Harness, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HarnessProviderSettingsDocument?)null);
        _storeMock.Setup(s => s.MarkRunStartedAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => WithState(CreateRun(run.Id, run.TaskId), RunState.Running));

        DispatchJobRequest? capturedRequest = null;
        _workerClientMock.Setup(c => c.DispatchJobAsync(It.IsAny<DispatchJobRequest>(), It.IsAny<CallOptions>()))
            .Callback<DispatchJobRequest, CallOptions>((req, _) => capturedRequest = req)
            .Returns(new AsyncUnaryCall<DispatchJobReply>(
                Task.FromResult(new DispatchJobReply { Accepted = true }),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => new Metadata()));

        await dispatcher.DispatchAsync(project, repo, task, run, CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.ContainerLabels.Should().ContainKey("orchestrator.run-id");
        capturedRequest.ContainerLabels["orchestrator.run-id"].Should().Be(run.Id);
        capturedRequest.ContainerLabels.Should().ContainKey("orchestrator.task-id");
        capturedRequest.ContainerLabels.Should().ContainKey("orchestrator.repo-id");
        capturedRequest.ContainerLabels.Should().ContainKey("orchestrator.project-id");
    }

    [Fact]
    public async Task DispatchAsync_SetsGitEnvironmentVariables()
    {
        var dispatcher = CreateDispatcher();
        var project = CreateProject();
        var repo = CreateRepository();
        var task = CreateTask();
        var run = CreateRun();

        SetupSuccessfulConcurrencyChecks();
        SetupSuccessfulInstructionRetrieval();
        _storeMock.Setup(s => s.ListProviderSecretsAsync(repo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _storeMock.Setup(s => s.GetHarnessProviderSettingsAsync(repo.Id, task.Harness, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HarnessProviderSettingsDocument?)null);
        _storeMock.Setup(s => s.MarkRunStartedAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => WithState(CreateRun(run.Id, run.TaskId), RunState.Running));

        DispatchJobRequest? capturedRequest = null;
        _workerClientMock.Setup(c => c.DispatchJobAsync(It.IsAny<DispatchJobRequest>(), It.IsAny<CallOptions>()))
            .Callback<DispatchJobRequest, CallOptions>((req, _) => capturedRequest = req)
            .Returns(new AsyncUnaryCall<DispatchJobReply>(
                Task.FromResult(new DispatchJobReply { Accepted = true }),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => new Metadata()));

        await dispatcher.DispatchAsync(project, repo, task, run, CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Env.Should().ContainKey("GIT_URL");
        capturedRequest.Env["GIT_URL"].Should().Be(repo.GitUrl);
        capturedRequest.Env.Should().ContainKey("DEFAULT_BRANCH");
        capturedRequest.Env["DEFAULT_BRANCH"].Should().Be("main");
        capturedRequest.Env.Should().ContainKey("GH_REPO");
        capturedRequest.Env["GH_REPO"].Should().Be("org/repo");
    }

    [Fact]
    public async Task DispatchAsync_SetsPrEnvironmentVariables_WhenAutoCreatePrEnabled()
    {
        var dispatcher = CreateDispatcher();
        var project = CreateProject();
        var repo = CreateRepository();
        var task = WithAutoCreatePullRequest(CreateTask(), true);
        var run = CreateRun();

        SetupSuccessfulConcurrencyChecks();
        SetupSuccessfulInstructionRetrieval();
        _storeMock.Setup(s => s.ListProviderSecretsAsync(repo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _storeMock.Setup(s => s.GetHarnessProviderSettingsAsync(repo.Id, task.Harness, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HarnessProviderSettingsDocument?)null);
        _storeMock.Setup(s => s.MarkRunStartedAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => WithState(CreateRun(run.Id, run.TaskId), RunState.Running));

        DispatchJobRequest? capturedRequest = null;
        _workerClientMock.Setup(c => c.DispatchJobAsync(It.IsAny<DispatchJobRequest>(), It.IsAny<CallOptions>()))
            .Callback<DispatchJobRequest, CallOptions>((req, _) => capturedRequest = req)
            .Returns(new AsyncUnaryCall<DispatchJobReply>(
                Task.FromResult(new DispatchJobReply { Accepted = true }),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => new Metadata()));

        await dispatcher.DispatchAsync(project, repo, task, run, CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Env.Should().ContainKey("AUTO_CREATE_PR");
        capturedRequest.Env["AUTO_CREATE_PR"].Should().Be("true");
        capturedRequest.Env.Should().ContainKey("PR_BRANCH");
        capturedRequest.Env.Should().ContainKey("PR_TITLE");
        capturedRequest.Env.Should().ContainKey("PR_BODY");
    }

    [Fact]
    public async Task DispatchAsync_SetsSandboxProfileSettings()
    {
        var dispatcher = CreateDispatcher();
        var project = CreateProject();
        var repo = CreateRepository();
        var task = WithSandboxProfile(CreateTask(), new SandboxProfileConfig
        {
            CpuLimit = 2.0,
            MemoryLimit = "4096",
            NetworkDisabled = true,
            ReadOnlyRootFs = true
        });
        var run = CreateRun();

        SetupSuccessfulConcurrencyChecks();
        SetupSuccessfulInstructionRetrieval();
        _storeMock.Setup(s => s.ListProviderSecretsAsync(repo.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _storeMock.Setup(s => s.GetHarnessProviderSettingsAsync(repo.Id, task.Harness, It.IsAny<CancellationToken>()))
            .ReturnsAsync((HarnessProviderSettingsDocument?)null);
        _storeMock.Setup(s => s.MarkRunStartedAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => WithState(CreateRun(run.Id, run.TaskId), RunState.Running));

        DispatchJobRequest? capturedRequest = null;
        _workerClientMock.Setup(c => c.DispatchJobAsync(It.IsAny<DispatchJobRequest>(), It.IsAny<CallOptions>()))
            .Callback<DispatchJobRequest, CallOptions>((req, _) => capturedRequest = req)
            .Returns(new AsyncUnaryCall<DispatchJobReply>(
                Task.FromResult(new DispatchJobReply { Accepted = true }),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => new Metadata()));

        await dispatcher.DispatchAsync(project, repo, task, run, CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.SandboxProfileCpuLimit.Should().Be(2.0);
        capturedRequest.SandboxProfileMemoryLimit.Should().Be("4096");
        capturedRequest.SandboxProfileNetworkDisabled.Should().BeTrue();
        capturedRequest.SandboxProfileReadOnlyRootFs.Should().BeTrue();
    }

    [Fact]
    public async Task CancelAsync_SendsCancelRequestToWorker()
    {
        var dispatcher = CreateDispatcher();
        var runId = "run-to-cancel";

        _workerClientMock.Setup(c => c.CancelJobAsync(It.IsAny<CancelJobRequest>(), It.IsAny<CallOptions>()))
            .Returns(new AsyncUnaryCall<CancelJobReply>(
                Task.FromResult(new CancelJobReply()),
                Task.FromResult(new Metadata()),
                () => Status.DefaultSuccess,
                () => new Metadata(),
                () => new Metadata()));

        await dispatcher.CancelAsync(runId, CancellationToken.None);

        _workerClientMock.Verify(c => c.CancelJobAsync(
            It.Is<CancelJobRequest>(r => r.RunId == runId),
            It.IsAny<CallOptions>()), Times.Once);
    }

    private void SetupSuccessfulConcurrencyChecks()
    {
        _storeMock.Setup(s => s.CountActiveRunsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _storeMock.Setup(s => s.CountActiveRunsByProjectAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _storeMock.Setup(s => s.CountActiveRunsByRepoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _storeMock.Setup(s => s.CountActiveRunsByTaskAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
    }

    private void SetupSuccessfulInstructionRetrieval()
    {
        _storeMock.Setup(s => s.GetInstructionsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }
}

public class TestableRunDispatcher
{
    private readonly WorkerGatewayClient _workerClient;
    private readonly IOrchestratorStore _store;
    private readonly SecretCryptoService _secretCrypto;
    private readonly IRunEventPublisher _publisher;
    private readonly OrchestratorOptions _options;
    private readonly ILogger<TestableRunDispatcher> _logger;

    public TestableRunDispatcher(
        WorkerGatewayClient workerClient,
        IOrchestratorStore store,
        SecretCryptoService secretCrypto,
        IRunEventPublisher publisher,
        IOptions<OrchestratorOptions> orchestratorOptions,
        ILogger<TestableRunDispatcher> logger)
    {
        _workerClient = workerClient;
        _store = store;
        _secretCrypto = secretCrypto;
        _publisher = publisher;
        _options = orchestratorOptions.Value;
        _logger = logger;
    }

    public async Task<bool> DispatchAsync(
        ProjectDocument project,
        RepositoryDocument repository,
        TaskDocument task,
        RunDocument run,
        CancellationToken cancellationToken)
    {
        if (task.ApprovalProfile.RequireApproval)
        {
            var pendingRun = await _store.MarkRunPendingApprovalAsync(run.Id, cancellationToken);
            if (pendingRun is not null)
            {
                await _publisher.PublishStatusAsync(pendingRun, cancellationToken);
                _logger.LogInformation("Run {RunId} marked as pending approval", run.Id);
            }
            return true;
        }

        var globalActive = await _store.CountActiveRunsAsync(cancellationToken);
        if (globalActive >= _options.MaxGlobalConcurrentRuns)
        {
            _logger.LogWarning("Global concurrency limit reached ({Limit}), leaving run {RunId} queued", _options.MaxGlobalConcurrentRuns, run.Id);
            return false;
        }

        var projectActive = await _store.CountActiveRunsByProjectAsync(project.Id, cancellationToken);
        if (projectActive >= _options.PerProjectConcurrencyLimit)
        {
            _logger.LogWarning("Project concurrency limit reached for {ProjectId}, leaving run {RunId} queued", project.Id, run.Id);
            return false;
        }

        var repoActive = await _store.CountActiveRunsByRepoAsync(repository.Id, cancellationToken);
        if (repoActive >= _options.PerRepoConcurrencyLimit)
        {
            _logger.LogWarning("Repo concurrency limit reached for {RepositoryId}, leaving run {RunId} queued", repository.Id, run.Id);
            return false;
        }

        if (task.ConcurrencyLimit > 0)
        {
            var taskActive = await _store.CountActiveRunsByTaskAsync(task.Id, cancellationToken);
            if (taskActive >= task.ConcurrencyLimit)
            {
                _logger.LogWarning("Task concurrency limit reached for {TaskId} ({Limit}), leaving run {RunId} queued", task.Id, task.ConcurrencyLimit, run.Id);
                return false;
            }
        }

        var layeredPrompt = await BuildLayeredPromptAsync(repository, task, cancellationToken);

        var request = new DispatchJobRequest
        {
            RunId = run.Id,
            ProjectId = project.Id,
            RepositoryId = repository.Id,
            TaskId = task.Id,
            Harness = task.Harness,
            Command = task.Command,
            Prompt = layeredPrompt,
            TimeoutSeconds = task.Timeouts.ExecutionSeconds,
            Attempt = run.Attempt,
            SandboxProfileCpuLimit = task.SandboxProfile.CpuLimit,
            SandboxProfileMemoryLimit = task.SandboxProfile.MemoryLimit,
            SandboxProfileNetworkDisabled = task.SandboxProfile.NetworkDisabled,
            SandboxProfileReadOnlyRootFs = task.SandboxProfile.ReadOnlyRootFs,
            GitUrl = repository.GitUrl,
            ArtifactPolicyMaxArtifacts = task.ArtifactPolicy.MaxArtifacts,
            ArtifactPolicyMaxTotalSizeBytes = task.ArtifactPolicy.MaxTotalSizeBytes,
        };

        request.ContainerLabels.Add("orchestrator.run-id", run.Id);
        request.ContainerLabels.Add("orchestrator.task-id", task.Id);
        request.ContainerLabels.Add("orchestrator.repo-id", repository.Id);
        request.ContainerLabels.Add("orchestrator.project-id", project.Id);

        request.Env.Add("GIT_URL", repository.GitUrl);
        request.Env.Add("DEFAULT_BRANCH", repository.DefaultBranch);
        request.Env.Add("AUTO_CREATE_PR", task.AutoCreatePullRequest ? "true" : "false");
        request.Env.Add("HARNESS_NAME", task.Harness);
        request.Env.Add("GH_REPO", ParseGitHubRepoSlug(repository.GitUrl));
        request.Env.Add("PR_BRANCH", $"agent/{repository.Name}/{task.Name}/{run.Id[..8]}".ToLowerInvariant().Replace(' ', '-'));
        request.Env.Add("PR_TITLE", $"[{task.Harness}] {task.Name} automated update");
        request.Env.Add("PR_BODY", $"Automated change from run {run.Id} for task {task.Name}.");

        var secrets = await _store.ListProviderSecretsAsync(repository.Id, cancellationToken);
        foreach (var secret in secrets)
        {
            try
            {
                var value = _secretCrypto.Decrypt(secret.EncryptedValue);
                AddMappedProviderEnvironmentVariables(request, secret.Provider, value);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to decrypt provider secret for repository {RepositoryId} and provider {Provider}", repository.Id, secret.Provider);
            }
        }

        var harnessSettings = await _store.GetHarnessProviderSettingsAsync(repository.Id, task.Harness, cancellationToken);
        if (harnessSettings is not null)
        {
            AddHarnessSettingsEnvironmentVariables(request, task.Harness, harnessSettings);
        }

        var response = await _workerClient.DispatchJobAsync(request);

        if (!response.Accepted)
        {
            _logger.LogWarning("Worker rejected run {RunId}: {Reason}", run.Id, response.Reason);
            var failed = await _store.MarkRunCompletedAsync(run.Id, false, $"Dispatch failed: {response.Reason}", "{}", cancellationToken);
            if (failed is not null)
            {
                await _publisher.PublishStatusAsync(failed, cancellationToken);
                await _store.CreateFindingFromFailureAsync(failed, response.Reason, cancellationToken);
            }
            return false;
        }

        var started = await _store.MarkRunStartedAsync(run.Id, cancellationToken);
        if (started is not null)
        {
            await _publisher.PublishStatusAsync(started, cancellationToken);
        }

        return true;
    }

    public async Task CancelAsync(string runId, CancellationToken cancellationToken)
    {
        try
        {
            await _workerClient.CancelJobAsync(new CancelJobRequest { RunId = runId });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send cancel to worker for run {RunId}", runId);
        }
    }

    private async Task<string> BuildLayeredPromptAsync(RepositoryDocument repository, TaskDocument task, CancellationToken cancellationToken)
    {
        var repoInstructions = await _store.GetInstructionsAsync(repository.Id, cancellationToken);
        var enabledRepoInstructions = repoInstructions.Where(i => i.Enabled).OrderBy(i => i.Priority).ToList();
        var hasTaskInstructions = task.InstructionFiles.Count > 0;

        if (enabledRepoInstructions.Count == 0 && !hasTaskInstructions)
            return task.Prompt;

        var sb = new System.Text.StringBuilder();

        if (enabledRepoInstructions.Count > 0)
        {
            foreach (var file in enabledRepoInstructions)
            {
                sb.AppendLine($"--- [Repository] {file.Name} ---");
                sb.AppendLine(file.Content);
                sb.AppendLine();
            }
        }

        if (hasTaskInstructions)
        {
            foreach (var file in task.InstructionFiles.OrderBy(f => f.Order))
            {
                sb.AppendLine($"--- [Task] {file.Name} ---");
                sb.AppendLine(file.Content);
                sb.AppendLine();
            }
        }

        sb.AppendLine("--- Task Prompt ---");
        sb.AppendLine(task.Prompt);
        return sb.ToString();
    }

    private static void AddMappedProviderEnvironmentVariables(DispatchJobRequest request, string provider, string value)
    {
        var normalized = provider.Trim().ToLowerInvariant();

        switch (normalized)
        {
            case "github":
                request.Env["GH_TOKEN"] = value;
                request.Env["GITHUB_TOKEN"] = value;
                break;
            case "codex":
                request.Env["CODEX_API_KEY"] = value;
                break;
            case "opencode":
                request.Env["OPENCODE_API_KEY"] = value;
                break;
            case "claude-code":
            case "claude code":
                request.Env["ANTHROPIC_API_KEY"] = value;
                break;
            case "zai":
                request.Env["Z_AI_API_KEY"] = value;
                break;
            default:
                request.Env[$"SECRET_{normalized.ToUpperInvariant().Replace('-', '_')}"] = value;
                break;
        }
    }

    private static void AddHarnessSettingsEnvironmentVariables(DispatchJobRequest request, string harness, HarnessProviderSettingsDocument settings)
    {
        var normalized = harness.Trim().ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(settings.Model))
        {
            request.Env["HARNESS_MODEL"] = settings.Model;
        }

        request.Env["HARNESS_TEMPERATURE"] = settings.Temperature.ToString("F2");
        request.Env["HARNESS_MAX_TOKENS"] = settings.MaxTokens.ToString();

        switch (normalized)
        {
            case "codex":
                if (!string.IsNullOrWhiteSpace(settings.Model))
                    request.Env["CODEX_MODEL"] = settings.Model;
                request.Env["CODEX_MAX_TOKENS"] = settings.MaxTokens.ToString();
                break;
            case "opencode":
                if (!string.IsNullOrWhiteSpace(settings.Model))
                    request.Env["OPENCODE_MODEL"] = settings.Model;
                request.Env["OPENCODE_TEMPERATURE"] = settings.Temperature.ToString("F2");
                break;
            case "claude-code":
                if (!string.IsNullOrWhiteSpace(settings.Model))
                {
                    request.Env["CLAUDE_MODEL"] = settings.Model;
                    request.Env["ANTHROPIC_MODEL"] = settings.Model;
                }
                break;
            case "zai":
                if (!string.IsNullOrWhiteSpace(settings.Model))
                    request.Env["ZAI_MODEL"] = settings.Model;
                break;
        }

        foreach (var (key, value) in settings.AdditionalSettings)
        {
            var envKey = $"HARNESS_{key.ToUpperInvariant().Replace(' ', '_')}";
            request.Env[envKey] = value;
        }
    }

    private static string ParseGitHubRepoSlug(string gitUrl)
    {
        if (string.IsNullOrWhiteSpace(gitUrl))
            return string.Empty;

        var normalized = gitUrl.Trim();
        if (normalized.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Replace("git@github.com:", string.Empty, StringComparison.OrdinalIgnoreCase);
        else if (normalized.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Replace("https://github.com/", string.Empty, StringComparison.OrdinalIgnoreCase);

        if (normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^4];

        return normalized.Trim('/');
    }
}

public static class RunDispatcherTestsHelper
{
    public static string ParseGitHubRepoSlug(string gitUrl)
    {
        if (string.IsNullOrWhiteSpace(gitUrl))
            return string.Empty;

        var normalized = gitUrl.Trim();
        if (normalized.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Replace("git@github.com:", string.Empty, StringComparison.OrdinalIgnoreCase);
        else if (normalized.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Replace("https://github.com/", string.Empty, StringComparison.OrdinalIgnoreCase);

        if (normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^4];

        return normalized.Trim('/');
    }
}
