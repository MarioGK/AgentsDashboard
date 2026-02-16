using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Services;
using MagicOnion;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class RunDispatcherTests
{
    [Test]
    public void ParseGitHubRepoSlug_ParsesHttpsUrl()
    {
        var result = RunDispatcherTestsHelper.ParseGitHubRepoSlug("https://github.com/org/repo.git");

        result.Should().Be("org/repo");
    }

    [Test]
    public void ParseGitHubRepoSlug_ParsesSshUrl()
    {
        var result = RunDispatcherTestsHelper.ParseGitHubRepoSlug("git@github.com:org/repo.git");

        result.Should().Be("org/repo");
    }

    [Test]
    public void ParseGitHubRepoSlug_ParsesUrlWithoutGitSuffix()
    {
        var result = RunDispatcherTestsHelper.ParseGitHubRepoSlug("https://github.com/org/repo");

        result.Should().Be("org/repo");
    }

    [Test]
    public void ParseGitHubRepoSlug_EmptyUrl_ReturnsEmpty()
    {
        var result = RunDispatcherTestsHelper.ParseGitHubRepoSlug("");

        result.Should().BeEmpty();
    }

    [Test]
    public void ParseGitHubRepoSlug_NullUrl_ReturnsEmpty()
    {
        var result = RunDispatcherTestsHelper.ParseGitHubRepoSlug(null!);

        result.Should().BeEmpty();
    }

    [Test]
    [Arguments("https://github.com/owner/repo.git", "owner/repo")]
    [Arguments("git@github.com:owner/repo.git", "owner/repo")]
    [Arguments("https://github.com/owner/repo", "owner/repo")]
    [Arguments("git@github.com:owner/repo", "owner/repo")]
    [Arguments("", "")]
    [Arguments("invalid", "invalid")]
    public void ParseGitHubRepoSlug_VariousInputs(string input, string expected)
    {
        var result = RunDispatcherTestsHelper.ParseGitHubRepoSlug(input);
        result.Should().Be(expected);
    }
}

public class RunDispatcherDispatchTests
{
    private readonly Mock<IMagicOnionClientFactory> _clientFactoryMock;
    private readonly Mock<IWorkerGatewayService> _workerClientMock;
    private readonly Mock<IOrchestratorStore> _storeMock;
    private readonly Mock<ISecretCryptoService> _secretCryptoMock;
    private readonly Mock<IRunEventPublisher> _publisherMock;
    private readonly OrchestratorOptions _options;

    public RunDispatcherDispatchTests()
    {
        _clientFactoryMock = new Mock<IMagicOnionClientFactory>();
        _workerClientMock = new Mock<IWorkerGatewayService>();
        _clientFactoryMock.Setup(f => f.CreateWorkerGatewayService()).Returns(_workerClientMock.Object);
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
            _clientFactoryMock.Object,
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

    private static RunDocument CreateRun(string? id = null, string taskId = "task-1") => new()
    {
        Id = id ?? Guid.NewGuid().ToString("N"),
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

    [Test]
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

    [Test]
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

    [Test]
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

    [Test]
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

    [Test]
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

    [Test]
    public async Task DispatchAsync_WorkerRejects_MarksRunFailed()
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
        _workerClientMock.Setup(c => c.DispatchJobAsync(It.IsAny<DispatchJobRequest>()))
            .Returns(UnaryResult.FromResult(new DispatchJobReply { Success = false, ErrorMessage = "Worker busy" }));
        _storeMock.Setup(s => s.MarkRunCompletedAsync(run.Id, false, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => WithState(CreateRun(run.Id, run.TaskId), RunState.Failed));

        var result = await dispatcher.DispatchAsync(project, repo, task, run, CancellationToken.None);

        result.Should().BeFalse();
        _storeMock.Verify(s => s.MarkRunCompletedAsync(run.Id, false, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task DispatchAsync_WorkerAccepts_MarksRunStarted()
    {
        var dispatcher = CreateDispatcher();
        var project = CreateProject();
        var repo = CreateRepository();
        var task = CreateTask();
        var run = CreateRun();

        SetupSuccessfulConcurrencyChecks();
        SetupSuccessfulInstructionRetrieval();
        _workerClientMock.Setup(c => c.DispatchJobAsync(It.IsAny<DispatchJobRequest>()))
            .Returns(UnaryResult.FromResult(new DispatchJobReply { Success = true }));
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

    [Test]
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
        _storeMock.Setup(s => s.MarkRunStartedAsync(run.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => WithState(CreateRun(run.Id, run.TaskId), RunState.Running));

        DispatchJobRequest? capturedRequest = null;
        _workerClientMock.Setup(c => c.DispatchJobAsync(It.IsAny<DispatchJobRequest>()))
            .Callback<DispatchJobRequest>(req => capturedRequest = req)
            .Returns(UnaryResult.FromResult(new DispatchJobReply { Success = true }));

        await dispatcher.DispatchAsync(project, repo, task, run, CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Secrets.Should().ContainKey("GH_TOKEN");
        capturedRequest.Secrets!["GH_TOKEN"].Should().Be("gh-token-123");
        capturedRequest.Secrets.Should().ContainKey("CODEX_API_KEY");
        capturedRequest.Secrets["CODEX_API_KEY"].Should().Be("codex-key-456");
    }

    [Test]
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
        _workerClientMock.Setup(c => c.DispatchJobAsync(It.IsAny<DispatchJobRequest>()))
            .Callback<DispatchJobRequest>(req => capturedRequest = req)
            .Returns(UnaryResult.FromResult(new DispatchJobReply { Success = true }));

        await dispatcher.DispatchAsync(project, repo, task, run, CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.EnvironmentVars.Should().ContainKey("HARNESS_MODEL");
        capturedRequest.EnvironmentVars!["HARNESS_MODEL"].Should().Be("gpt-4");
        capturedRequest.EnvironmentVars.Should().ContainKey("CODEX_MODEL");
        capturedRequest.EnvironmentVars["CODEX_MODEL"].Should().Be("gpt-4");
        capturedRequest.EnvironmentVars.Should().ContainKey("HARNESS_CUSTOM_SETTING");
        capturedRequest.EnvironmentVars["HARNESS_CUSTOM_SETTING"].Should().Be("custom-value");
    }

    [Test]
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
        _workerClientMock.Setup(c => c.DispatchJobAsync(It.IsAny<DispatchJobRequest>()))
            .Callback<DispatchJobRequest>(req => capturedRequest = req)
            .Returns(UnaryResult.FromResult(new DispatchJobReply { Success = true }));

        await dispatcher.DispatchAsync(project, repo, task, run, CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.Instruction.Should().Contain("coding-standards.md");
        capturedRequest.Instruction.Should().Contain("Use async/await");
        capturedRequest.Instruction.Should().Contain("Task Prompt");
    }

    [Test]
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
        _workerClientMock.Setup(c => c.DispatchJobAsync(It.IsAny<DispatchJobRequest>()))
            .Callback<DispatchJobRequest>(req => capturedRequest = req)
            .Returns(UnaryResult.FromResult(new DispatchJobReply { Success = true }));

        await dispatcher.DispatchAsync(project, repo, task, run, CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.EnvironmentVars.Should().ContainKey("GIT_URL");
        capturedRequest.EnvironmentVars!["GIT_URL"].Should().Be(repo.GitUrl);
        capturedRequest.EnvironmentVars.Should().ContainKey("DEFAULT_BRANCH");
        capturedRequest.EnvironmentVars["DEFAULT_BRANCH"].Should().Be("main");
        capturedRequest.EnvironmentVars.Should().ContainKey("GH_REPO");
        capturedRequest.EnvironmentVars["GH_REPO"].Should().Be("org/repo");
    }

    [Test]
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
        _workerClientMock.Setup(c => c.DispatchJobAsync(It.IsAny<DispatchJobRequest>()))
            .Callback<DispatchJobRequest>(req => capturedRequest = req)
            .Returns(UnaryResult.FromResult(new DispatchJobReply { Success = true }));

        await dispatcher.DispatchAsync(project, repo, task, run, CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.EnvironmentVars.Should().ContainKey("AUTO_CREATE_PR");
        capturedRequest.EnvironmentVars!["AUTO_CREATE_PR"].Should().Be("true");
        capturedRequest.EnvironmentVars.Should().ContainKey("PR_BRANCH");
        capturedRequest.EnvironmentVars.Should().ContainKey("PR_TITLE");
        capturedRequest.EnvironmentVars.Should().ContainKey("PR_BODY");
    }

    [Test]
    public async Task CancelAsync_SendsCancelRequestToWorker()
    {
        var dispatcher = CreateDispatcher();
        var runId = "run-to-cancel";

        _workerClientMock.Setup(c => c.CancelJobAsync(It.IsAny<CancelJobRequest>()))
            .Returns(UnaryResult.FromResult(new CancelJobReply { Success = true }));

        await dispatcher.CancelAsync(runId, CancellationToken.None);

        _workerClientMock.Verify(c => c.CancelJobAsync(
            It.Is<CancelJobRequest>(r => r.RunId == runId)), Times.Once);
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
    private readonly IMagicOnionClientFactory _clientFactory;
    private readonly IOrchestratorStore _store;
    private readonly ISecretCryptoService _secretCrypto;
    private readonly IRunEventPublisher _publisher;
    private readonly OrchestratorOptions _options;
    private readonly ILogger<TestableRunDispatcher> _logger;

    public TestableRunDispatcher(
        IMagicOnionClientFactory clientFactory,
        IOrchestratorStore store,
        ISecretCryptoService secretCrypto,
        IRunEventPublisher publisher,
        IOptions<OrchestratorOptions> orchestratorOptions,
        ILogger<TestableRunDispatcher> logger)
    {
        _clientFactory = clientFactory;
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

        var envVars = new Dictionary<string, string>
        {
            ["GIT_URL"] = repository.GitUrl,
            ["DEFAULT_BRANCH"] = repository.DefaultBranch,
            ["AUTO_CREATE_PR"] = task.AutoCreatePullRequest ? "true" : "false",
            ["HARNESS_NAME"] = task.Harness,
            ["GH_REPO"] = ParseGitHubRepoSlug(repository.GitUrl),
        };

        var runIdShort = string.IsNullOrEmpty(run.Id) ? "unknown" : run.Id.Length >= 8 ? run.Id[..8] : run.Id;
        envVars["PR_BRANCH"] = $"agent/{repository.Name}/{task.Name}/{runIdShort}".ToLowerInvariant().Replace(' ', '-');
        envVars["PR_TITLE"] = $"[{task.Harness}] {task.Name} automated update";
        envVars["PR_BODY"] = $"Automated change from run {run.Id} for task {task.Name}.";

        var secrets = await _store.ListProviderSecretsAsync(repository.Id, cancellationToken);
        var secretsDict = new Dictionary<string, string>();

        foreach (var secret in secrets)
        {
            try
            {
                var value = _secretCrypto.Decrypt(secret.EncryptedValue);
                AddMappedProviderEnvironmentVariables(envVars, secretsDict, secret.Provider, value);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to decrypt provider secret for repository {RepositoryId} and provider {Provider}", repository.Id, secret.Provider);
            }
        }

        var harnessSettings = await _store.GetHarnessProviderSettingsAsync(repository.Id, task.Harness, cancellationToken);
        if (harnessSettings is not null)
        {
            AddHarnessSettingsEnvironmentVariables(envVars, task.Harness, harnessSettings);
        }

        var artifactPatterns = task.ArtifactPatterns.Count > 0
            ? task.ArtifactPatterns.ToList()
            : null;

        var linkedFailureRuns = task.LinkedFailureRuns.Count > 0
            ? task.LinkedFailureRuns.ToList()
            : null;

        var request = new DispatchJobRequest
        {
            RunId = run.Id,
            ProjectId = project.Id,
            RepositoryId = repository.Id,
            TaskId = task.Id,
            HarnessType = task.Harness,
            ImageTag = $"harness-{task.Harness.ToLowerInvariant()}:latest",
            CloneUrl = repository.GitUrl,
            Branch = repository.DefaultBranch,
            WorkingDirectory = null,
            Instruction = layeredPrompt,
            EnvironmentVars = envVars,
            Secrets = secretsDict.Count > 0 ? secretsDict : null,
            ConcurrencyKey = null,
            TimeoutSeconds = task.Timeouts.ExecutionSeconds,
            RetryCount = run.Attempt - 1,
            ArtifactPatterns = artifactPatterns,
            LinkedFailureRuns = linkedFailureRuns,
            CustomArgs = task.Command,
            DispatchedAt = DateTimeOffset.UtcNow,
        };

        var workerClient = _clientFactory.CreateWorkerGatewayService();
        var response = await workerClient.DispatchJobAsync(request);

        if (!response.Success)
        {
            _logger.LogWarning("Worker rejected run {RunId}: {Reason}", run.Id, response.ErrorMessage);
            var failed = await _store.MarkRunCompletedAsync(run.Id, false, $"Dispatch failed: {response.ErrorMessage}", "{}", cancellationToken);
            if (failed is not null)
            {
                await _publisher.PublishStatusAsync(failed, cancellationToken);
                await _store.CreateFindingFromFailureAsync(failed, response.ErrorMessage ?? "Unknown error", cancellationToken);
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
            var workerClient = _clientFactory.CreateWorkerGatewayService();
            await workerClient.CancelJobAsync(new CancelJobRequest { RunId = runId });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send cancel to worker for run {RunId}", runId);
        }
    }

    private async Task<string> BuildLayeredPromptAsync(RepositoryDocument repository, TaskDocument task, CancellationToken cancellationToken)
    {
        var repoInstructionsFromCollection = await _store.GetInstructionsAsync(repository.Id, cancellationToken);
        var enabledRepoInstructions = repoInstructionsFromCollection.Where(i => i.Enabled).OrderBy(i => i.Priority).ToList();
        var embeddedRepoInstructions = repository.InstructionFiles?.OrderBy(f => f.Order).ToList() ?? [];
        var hasTaskInstructions = task.InstructionFiles.Count > 0;

        if (enabledRepoInstructions.Count == 0 && embeddedRepoInstructions.Count == 0 && !hasTaskInstructions)
            return task.Prompt;

        var sb = new System.Text.StringBuilder();

        if (enabledRepoInstructions.Count > 0)
        {
            foreach (var file in enabledRepoInstructions)
            {
                sb.AppendLine($"--- [Repository Collection] {file.Name} ---");
                sb.AppendLine(file.Content);
                sb.AppendLine();
            }
        }

        if (embeddedRepoInstructions.Count > 0)
        {
            foreach (var file in embeddedRepoInstructions)
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

    private static void AddMappedProviderEnvironmentVariables(
        Dictionary<string, string> envVars,
        Dictionary<string, string> secrets,
        string provider,
        string value)
    {
        var normalized = provider.Trim().ToLowerInvariant();

        switch (normalized)
        {
            case "github":
                secrets["GH_TOKEN"] = value;
                secrets["GITHUB_TOKEN"] = value;
                break;
            case "codex":
                secrets["CODEX_API_KEY"] = value;
                break;
            case "opencode":
                secrets["OPENCODE_API_KEY"] = value;
                break;
            case "claude-code":
            case "claude code":
                secrets["ANTHROPIC_API_KEY"] = value;
                break;
            case "zai":
                secrets["Z_AI_API_KEY"] = value;
                break;
            default:
                secrets[$"SECRET_{normalized.ToUpperInvariant().Replace('-', '_')}"] = value;
                break;
        }
    }

    private static void AddHarnessSettingsEnvironmentVariables(
        Dictionary<string, string> envVars,
        string harness,
        HarnessProviderSettingsDocument settings)
    {
        var normalized = harness.Trim().ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(settings.Model))
        {
            envVars["HARNESS_MODEL"] = settings.Model;
        }

        envVars["HARNESS_TEMPERATURE"] = settings.Temperature.ToString("F2");
        envVars["HARNESS_MAX_TOKENS"] = settings.MaxTokens.ToString();

        switch (normalized)
        {
            case "codex":
                if (!string.IsNullOrWhiteSpace(settings.Model))
                    envVars["CODEX_MODEL"] = settings.Model;
                envVars["CODEX_MAX_TOKENS"] = settings.MaxTokens.ToString();
                break;
            case "opencode":
                if (!string.IsNullOrWhiteSpace(settings.Model))
                    envVars["OPENCODE_MODEL"] = settings.Model;
                envVars["OPENCODE_TEMPERATURE"] = settings.Temperature.ToString("F2");
                break;
            case "claude-code":
                if (!string.IsNullOrWhiteSpace(settings.Model))
                {
                    envVars["CLAUDE_MODEL"] = settings.Model;
                    envVars["ANTHROPIC_MODEL"] = settings.Model;
                }
                break;
            case "zai":
                if (!string.IsNullOrWhiteSpace(settings.Model))
                    envVars["ZAI_MODEL"] = settings.Model;
                break;
        }

        foreach (var (key, value) in settings.AdditionalSettings)
        {
            var envKey = $"HARNESS_{key.ToUpperInvariant().Replace(' ', '_').Replace('-', '_')}";
            envVars[envKey] = value;
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
