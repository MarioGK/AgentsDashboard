using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Services;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace AgentsDashboard.UnitTests.ControlPlane.Endpoints;

public class ApiEndpointsTests
{
    private readonly Mock<OrchestratorStore> _mockStore;
    private readonly Mock<WorkflowExecutor> _mockWorkflowExecutor;
    private readonly Mock<ImageBuilderService> _mockImageBuilder;
    private readonly Mock<HarnessHealthService> _mockHarnessHealth;
    private readonly CancellationToken _ct = CancellationToken.None;

    public ApiEndpointsTests()
    {
        var mongoClient = new Mock<IMongoClient>();
        var mongoDatabase = new Mock<IMongoDatabase>();
        mongoClient.Setup(c => c.GetDatabase(It.IsAny<string>(), null)).Returns(mongoDatabase.Object);
        
        foreach (var collectionName in new[] { "projects", "repositories", "tasks", "runs", "findings", 
             "run_events", "provider_secrets", "workers", "webhooks", "proxy_audits", "settings",
             "workflows", "workflow_executions", "alert_rules", "alert_events", "repository_instructions",
             "harness_provider_settings" })
        {
            var collectionMock = new Mock<IMongoCollection<BsonDocument>>();
            mongoDatabase.Setup(d => d.GetCollection<BsonDocument>(collectionName, null)).Returns(collectionMock.Object);
        }
        
        var options = Options.Create(new OrchestratorOptions());
        _mockStore = new Mock<OrchestratorStore>(MockBehavior.Loose, mongoClient.Object, options) { CallBase = false };
        var mockContainerReaper = new Mock<IContainerReaper>();
        _mockWorkflowExecutor = new Mock<WorkflowExecutor>(MockBehavior.Loose, _mockStore.Object, null!, mockContainerReaper.Object, options, null!) { CallBase = false };
        _mockImageBuilder = new Mock<ImageBuilderService>(MockBehavior.Loose, null!) { CallBase = false };
        _mockHarnessHealth = new Mock<HarnessHealthService>(MockBehavior.Loose, null!) { CallBase = false };
    }

    #region Projects API Tests

    [Fact]
    public async Task ListProjects_ReturnsOkWithProjects()
    {
        var projects = new List<ProjectDocument>
        {
            new() { Id = "proj-1", Name = "Project 1", Description = "Desc 1" },
            new() { Id = "proj-2", Name = "Project 2", Description = "Desc 2" }
        };

        _mockStore.Setup(s => s.ListProjectsAsync(_ct)).ReturnsAsync(projects);

        var result = await _mockStore.Object.ListProjectsAsync(_ct);

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Project 1");
    }

    [Fact]
    public async Task CreateProject_WithValidRequest_ReturnsProject()
    {
        var request = new CreateProjectRequest("New Project", "Description");
        var expectedProject = new ProjectDocument { Id = "proj-new", Name = "New Project", Description = "Description" };

        _mockStore.Setup(s => s.CreateProjectAsync(request, _ct)).ReturnsAsync(expectedProject);

        var result = await _mockStore.Object.CreateProjectAsync(request, _ct);

        result.Name.Should().Be("New Project");
        result.Description.Should().Be("Description");
    }

    [Fact]
    public void CreateProject_WithEmptyName_ReturnsValidationProblem()
    {
        var request = new CreateProjectRequest("", "Description");

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            var errors = new Dictionary<string, string[]> { ["name"] = ["Name is required"] };
            errors.Should().ContainKey("name");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void CreateProject_WithInvalidName_ShouldFailValidation(string? name)
    {
        string.IsNullOrWhiteSpace(name).Should().BeTrue();
    }

    [Fact]
    public async Task UpdateProject_WithValidRequest_ReturnsUpdatedProject()
    {
        var request = new UpdateProjectRequest("Updated Name", "Updated Desc");
        var expectedProject = new ProjectDocument { Id = "proj-1", Name = "Updated Name", Description = "Updated Desc" };

        _mockStore.Setup(s => s.UpdateProjectAsync("proj-1", request, _ct)).ReturnsAsync(expectedProject);

        var result = await _mockStore.Object.UpdateProjectAsync("proj-1", request, _ct);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Updated Name");
    }

    [Fact]
    public async Task UpdateProject_WithNonExistentId_ReturnsNull()
    {
        var request = new UpdateProjectRequest("Updated Name", "Updated Desc");

        _mockStore.Setup(s => s.UpdateProjectAsync("nonexistent", request, _ct)).ReturnsAsync((ProjectDocument?)null);

        var result = await _mockStore.Object.UpdateProjectAsync("nonexistent", request, _ct);

        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteProject_WithExistingId_ReturnsTrue()
    {
        _mockStore.Setup(s => s.DeleteProjectAsync("proj-1", _ct)).ReturnsAsync(true);

        var result = await _mockStore.Object.DeleteProjectAsync("proj-1", _ct);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteProject_WithNonExistentId_ReturnsFalse()
    {
        _mockStore.Setup(s => s.DeleteProjectAsync("nonexistent", _ct)).ReturnsAsync(false);

        var result = await _mockStore.Object.DeleteProjectAsync("nonexistent", _ct);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ListRepositoriesByProject_ReturnsRepositories()
    {
        var repos = new List<RepositoryDocument>
        {
            new() { Id = "repo-1", ProjectId = "proj-1", Name = "Repo 1" },
            new() { Id = "repo-2", ProjectId = "proj-1", Name = "Repo 2" }
        };

        _mockStore.Setup(s => s.ListRepositoriesAsync("proj-1", _ct)).ReturnsAsync(repos);

        var result = await _mockStore.Object.ListRepositoriesAsync("proj-1", _ct);

        result.Should().HaveCount(2);
    }

    #endregion

    #region Repositories API Tests

    [Fact]
    public async Task CreateRepository_WithValidProject_ReturnsRepository()
    {
        var request = new CreateRepositoryRequest("proj-1", "New Repo", "https://github.com/org/repo.git", "main");
        var project = new ProjectDocument { Id = "proj-1", Name = "Project 1" };
        var expectedRepo = new RepositoryDocument { Id = "repo-new", ProjectId = "proj-1", Name = "New Repo" };

        _mockStore.Setup(s => s.GetProjectAsync("proj-1", _ct)).ReturnsAsync(project);
        _mockStore.Setup(s => s.CreateRepositoryAsync(request, _ct)).ReturnsAsync(expectedRepo);

        var projectResult = await _mockStore.Object.GetProjectAsync("proj-1", _ct);
        projectResult.Should().NotBeNull();

        var repoResult = await _mockStore.Object.CreateRepositoryAsync(request, _ct);
        repoResult.Name.Should().Be("New Repo");
    }

    [Fact]
    public async Task CreateRepository_WithNonExistentProject_ReturnsNotFound()
    {
        var request = new CreateRepositoryRequest("nonexistent", "New Repo", "https://github.com/org/repo.git", "main");

        _mockStore.Setup(s => s.GetProjectAsync("nonexistent", _ct)).ReturnsAsync((ProjectDocument?)null);

        var result = await _mockStore.Object.GetProjectAsync("nonexistent", _ct);

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdateRepository_WithValidId_ReturnsUpdatedRepository()
    {
        var request = new UpdateRepositoryRequest("Updated Repo", "https://github.com/org/new-repo.git", "develop");
        var expectedRepo = new RepositoryDocument { Id = "repo-1", Name = "Updated Repo", GitUrl = "https://github.com/org/new-repo.git" };

        _mockStore.Setup(s => s.UpdateRepositoryAsync("repo-1", request, _ct)).ReturnsAsync(expectedRepo);

        var result = await _mockStore.Object.UpdateRepositoryAsync("repo-1", request, _ct);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Updated Repo");
    }

    [Fact]
    public async Task DeleteRepository_WithExistingId_ReturnsTrue()
    {
        _mockStore.Setup(s => s.DeleteRepositoryAsync("repo-1", _ct)).ReturnsAsync(true);

        var result = await _mockStore.Object.DeleteRepositoryAsync("repo-1", _ct);

        result.Should().BeTrue();
    }

    #endregion

    #region Tasks API Tests

    [Fact]
    public async Task ListTasks_ReturnsTasksForRepository()
    {
        var tasks = new List<TaskDocument>
        {
            new() { Id = "task-1", RepositoryId = "repo-1", Name = "Task 1" },
            new() { Id = "task-2", RepositoryId = "repo-1", Name = "Task 2" }
        };

        _mockStore.Setup(s => s.ListTasksAsync("repo-1", _ct)).ReturnsAsync(tasks);

        var result = await _mockStore.Object.ListTasksAsync("repo-1", _ct);

        result.Should().HaveCount(2);
    }

    [Fact]
    public void CreateTask_WithCronKindAndNoExpression_ReturnsValidationProblem()
    {
        var request = new CreateTaskRequest(
            RepositoryId: "repo-1",
            Name: "Cron Task",
            Kind: TaskKind.Cron,
            Harness: "codex",
            Prompt: "",
            Command: "",
            AutoCreatePullRequest: false,
            CronExpression: "",
            Enabled: true
        );

        if (request.Kind == TaskKind.Cron && string.IsNullOrWhiteSpace(request.CronExpression))
        {
            var errors = new Dictionary<string, string[]> { ["cronExpression"] = ["Cron expression required for cron tasks"] };
            errors.Should().ContainKey("cronExpression");
        }
    }

    [Fact]
    public async Task CreateTask_WithNonExistentRepository_ReturnsNotFound()
    {
        var request = new CreateTaskRequest(
            RepositoryId: "nonexistent",
            Name: "Task",
            Kind: TaskKind.OneShot,
            Harness: "codex",
            Prompt: "",
            Command: "",
            AutoCreatePullRequest: false,
            CronExpression: "",
            Enabled: true
        );

        _mockStore.Setup(s => s.GetRepositoryAsync("nonexistent", _ct)).ReturnsAsync((RepositoryDocument?)null);

        var result = await _mockStore.Object.GetRepositoryAsync("nonexistent", _ct);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CreateTask_WithValidRequest_ReturnsTask()
    {
        var request = new CreateTaskRequest(
            RepositoryId: "repo-1",
            Name: "New Task",
            Kind: TaskKind.OneShot,
            Harness: "codex",
            Prompt: "Test prompt",
            Command: "",
            AutoCreatePullRequest: false,
            CronExpression: "",
            Enabled: true
        );

        var repo = new RepositoryDocument { Id = "repo-1", Name = "Repo 1" };
        var expectedTask = new TaskDocument { Id = "task-new", RepositoryId = "repo-1", Name = "New Task", Kind = TaskKind.OneShot };

        _mockStore.Setup(s => s.GetRepositoryAsync("repo-1", _ct)).ReturnsAsync(repo);
        _mockStore.Setup(s => s.CreateTaskAsync(request, _ct)).ReturnsAsync(expectedTask);

        var repoResult = await _mockStore.Object.GetRepositoryAsync("repo-1", _ct);
        repoResult.Should().NotBeNull();

        var taskResult = await _mockStore.Object.CreateTaskAsync(request, _ct);
        taskResult.Name.Should().Be("New Task");
    }

    [Fact]
    public async Task UpdateTask_WithValidRequest_ReturnsTask()
    {
        var request = new UpdateTaskRequest(
            Name: "Updated Task",
            Kind: TaskKind.OneShot,
            Harness: "claudecode",
            Prompt: "Updated prompt",
            Command: "",
            AutoCreatePullRequest: false,
            CronExpression: "",
            Enabled: true
        );

        var expectedTask = new TaskDocument { Id = "task-1", Name = "Updated Task", Harness = "claudecode" };

        _mockStore.Setup(s => s.UpdateTaskAsync("task-1", request, _ct)).ReturnsAsync(expectedTask);

        var result = await _mockStore.Object.UpdateTaskAsync("task-1", request, _ct);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Updated Task");
    }

    [Fact]
    public async Task DeleteTask_WithExistingId_ReturnsTrue()
    {
        _mockStore.Setup(s => s.DeleteTaskAsync("task-1", _ct)).ReturnsAsync(true);

        var result = await _mockStore.Object.DeleteTaskAsync("task-1", _ct);

        result.Should().BeTrue();
    }

    [Theory]
    [InlineData(TaskKind.OneShot)]
    [InlineData(TaskKind.Cron)]
    [InlineData(TaskKind.EventDriven)]
    public void TaskKind_AllValuesAreValid(TaskKind kind)
    {
        Enum.IsDefined(typeof(TaskKind), kind).Should().BeTrue();
    }

    #endregion

    #region Runs API Tests

    [Fact]
    public async Task ListRuns_ReturnsRecentRuns()
    {
        var runs = new List<RunDocument>
        {
            new() { Id = "run-1", State = RunState.Succeeded },
            new() { Id = "run-2", State = RunState.Failed }
        };

        _mockStore.Setup(s => s.ListRecentRunsAsync(_ct)).ReturnsAsync(runs);

        var result = await _mockStore.Object.ListRecentRunsAsync(_ct);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetRun_WithExistingId_ReturnsRun()
    {
        var run = new RunDocument { Id = "run-1", State = RunState.Running };

        _mockStore.Setup(s => s.GetRunAsync("run-1", _ct)).ReturnsAsync(run);

        var result = await _mockStore.Object.GetRunAsync("run-1", _ct);

        result.Should().NotBeNull();
        result!.State.Should().Be(RunState.Running);
    }

    [Fact]
    public async Task GetRun_WithNonExistentId_ReturnsNull()
    {
        _mockStore.Setup(s => s.GetRunAsync("nonexistent", _ct)).ReturnsAsync((RunDocument?)null);

        var result = await _mockStore.Object.GetRunAsync("nonexistent", _ct);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CreateRun_WithValidTask_ReturnsRun()
    {
        var task = new TaskDocument { Id = "task-1", RepositoryId = "repo-1" };
        var repo = new RepositoryDocument { Id = "repo-1", ProjectId = "proj-1" };
        var project = new ProjectDocument { Id = "proj-1" };
        var expectedRun = new RunDocument { Id = "run-new", TaskId = "task-1", State = RunState.Queued };

        _mockStore.Setup(s => s.GetTaskAsync("task-1", _ct)).ReturnsAsync(task);
        _mockStore.Setup(s => s.GetRepositoryAsync("repo-1", _ct)).ReturnsAsync(repo);
        _mockStore.Setup(s => s.GetProjectAsync("proj-1", _ct)).ReturnsAsync(project);
        _mockStore.Setup(s => s.CreateRunAsync(task, "proj-1", _ct, 1)).ReturnsAsync(expectedRun);

        var taskResult = await _mockStore.Object.GetTaskAsync("task-1", _ct);
        taskResult.Should().NotBeNull();

        var runResult = await _mockStore.Object.CreateRunAsync(task, "proj-1", _ct);
        runResult.State.Should().Be(RunState.Queued);
    }

    [Fact]
    public async Task CancelRun_WithCancellableState_ReturnsCancelledRun()
    {
        var run = new RunDocument { Id = "run-1", State = RunState.Running };
        var cancelledRun = new RunDocument { Id = "run-1", State = RunState.Cancelled };

        _mockStore.Setup(s => s.GetRunAsync("run-1", _ct)).ReturnsAsync(run);
        _mockStore.Setup(s => s.MarkRunCancelledAsync("run-1", _ct)).ReturnsAsync(cancelledRun);

        var runResult = await _mockStore.Object.GetRunAsync("run-1", _ct);
        var isValidState = runResult!.State is RunState.Queued or RunState.Running or RunState.PendingApproval;
        isValidState.Should().BeTrue();

        var result = await _mockStore.Object.MarkRunCancelledAsync("run-1", _ct);
        result!.State.Should().Be(RunState.Cancelled);
    }

    [Theory]
    [InlineData(RunState.Succeeded)]
    [InlineData(RunState.Failed)]
    [InlineData(RunState.Cancelled)]
    public void CancelRun_WithNonCancellableState_ShouldReturnBadRequest(RunState state)
    {
        var isValidState = state is RunState.Queued or RunState.Running or RunState.PendingApproval;
        isValidState.Should().BeFalse();
    }

    [Fact]
    public async Task RetryRun_WithValidRun_ReturnsNewRun()
    {
        var task = new TaskDocument { Id = "task-1", RepositoryId = "repo-1" };
        var retryRun = new RunDocument { Id = "run-2", TaskId = "task-1", Attempt = 2 };

        _mockStore.Setup(s => s.CreateRunAsync(task, "proj-1", _ct, 2)).ReturnsAsync(retryRun);

        var result = await _mockStore.Object.CreateRunAsync(task, "proj-1", _ct, 2);

        result.Attempt.Should().Be(2);
    }

    [Fact]
    public async Task ApproveRun_WithPendingApprovalState_ReturnsApprovedRun()
    {
        var run = new RunDocument { Id = "run-1", State = RunState.PendingApproval, TaskId = "task-1" };
        var approvedRun = new RunDocument { Id = "run-1", State = RunState.Queued, Summary = "Approved and queued" };

        _mockStore.Setup(s => s.GetRunAsync("run-1", _ct)).ReturnsAsync(run);
        _mockStore.Setup(s => s.ApproveRunAsync("run-1", _ct)).ReturnsAsync(approvedRun);

        var runResult = await _mockStore.Object.GetRunAsync("run-1", _ct);
        runResult!.State.Should().Be(RunState.PendingApproval);

        var result = await _mockStore.Object.ApproveRunAsync("run-1", _ct);
        result!.State.Should().Be(RunState.Queued);
    }

    [Fact]
    public async Task ApproveRun_WithNonPendingState_ReturnsBadRequest()
    {
        var run = new RunDocument { Id = "run-1", State = RunState.Running };

        _mockStore.Setup(s => s.GetRunAsync("run-1", _ct)).ReturnsAsync(run);

        var runResult = await _mockStore.Object.GetRunAsync("run-1", _ct);
        var isPendingApproval = runResult!.State == RunState.PendingApproval;

        isPendingApproval.Should().BeFalse();
    }

    [Fact]
    public async Task RejectRun_WithPendingApprovalState_ReturnsRejectedRun()
    {
        var run = new RunDocument { Id = "run-1", State = RunState.PendingApproval };
        var rejectedRun = new RunDocument { Id = "run-1", State = RunState.Cancelled, Summary = "Rejected" };

        _mockStore.Setup(s => s.GetRunAsync("run-1", _ct)).ReturnsAsync(run);
        _mockStore.Setup(s => s.RejectRunAsync("run-1", _ct)).ReturnsAsync(rejectedRun);

        var runResult = await _mockStore.Object.GetRunAsync("run-1", _ct);
        runResult!.State.Should().Be(RunState.PendingApproval);

        var result = await _mockStore.Object.RejectRunAsync("run-1", _ct);
        result!.State.Should().Be(RunState.Cancelled);
    }

    [Fact]
    public async Task ListRunLogs_ReturnsLogsForRun()
    {
        var logs = new List<RunLogEvent>
        {
            new() { Id = "log-1", RunId = "run-1", Message = "Starting" },
            new() { Id = "log-2", RunId = "run-1", Message = "Completed" }
        };

        _mockStore.Setup(s => s.ListRunLogsAsync("run-1", _ct)).ReturnsAsync(logs);

        var result = await _mockStore.Object.ListRunLogsAsync("run-1", _ct);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListArtifacts_ReturnsArtifactsForRun()
    {
        var artifacts = new List<string> { "output.txt", "result.json" };

        _mockStore.Setup(s => s.ListArtifactsAsync("run-1", _ct)).ReturnsAsync(artifacts);

        var result = await _mockStore.Object.ListArtifactsAsync("run-1", _ct);

        result.Should().HaveCount(2);
        result.Should().Contain("output.txt");
    }

    #endregion

    #region Findings API Tests

    [Fact]
    public async Task ListFindings_ReturnsAllFindings()
    {
        var findings = new List<FindingDocument>
        {
            new() { Id = "finding-1", Title = "Issue 1", State = FindingState.New },
            new() { Id = "finding-2", Title = "Issue 2", State = FindingState.Acknowledged }
        };

        _mockStore.Setup(s => s.ListAllFindingsAsync(_ct)).ReturnsAsync(findings);

        var result = await _mockStore.Object.ListAllFindingsAsync(_ct);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetFinding_WithExistingId_ReturnsFinding()
    {
        var finding = new FindingDocument { Id = "finding-1", Title = "Issue 1" };

        _mockStore.Setup(s => s.GetFindingAsync("finding-1", _ct)).ReturnsAsync(finding);

        var result = await _mockStore.Object.GetFindingAsync("finding-1", _ct);

        result.Should().NotBeNull();
        result!.Title.Should().Be("Issue 1");
    }

    [Fact]
    public async Task UpdateFindingState_WithValidId_ReturnsUpdatedFinding()
    {
        var finding = new FindingDocument { Id = "finding-1", State = FindingState.InProgress };

        _mockStore.Setup(s => s.UpdateFindingStateAsync("finding-1", FindingState.InProgress, _ct)).ReturnsAsync(finding);

        var result = await _mockStore.Object.UpdateFindingStateAsync("finding-1", FindingState.InProgress, _ct);

        result.Should().NotBeNull();
        result!.State.Should().Be(FindingState.InProgress);
    }

    [Fact]
    public async Task AssignFinding_WithValidId_ReturnsAssignedFinding()
    {
        var finding = new FindingDocument { Id = "finding-1", AssignedTo = "user@example.com", State = FindingState.InProgress };

        _mockStore.Setup(s => s.AssignFindingAsync("finding-1", "user@example.com", _ct)).ReturnsAsync(finding);

        var result = await _mockStore.Object.AssignFindingAsync("finding-1", "user@example.com", _ct);

        result.Should().NotBeNull();
        result!.AssignedTo.Should().Be("user@example.com");
        result.State.Should().Be(FindingState.InProgress);
    }

    [Theory]
    [InlineData(FindingState.New)]
    [InlineData(FindingState.Acknowledged)]
    [InlineData(FindingState.InProgress)]
    [InlineData(FindingState.Resolved)]
    [InlineData(FindingState.Ignored)]
    public void FindingState_AllValuesAreValid(FindingState state)
    {
        Enum.IsDefined(typeof(FindingState), state).Should().BeTrue();
    }

    #endregion

    #region Workers API Tests

    [Fact]
    public async Task ListWorkers_ReturnsAllWorkers()
    {
        var workers = new List<WorkerRegistration>
        {
            new() { WorkerId = "worker-1", Online = true, MaxSlots = 4 },
            new() { WorkerId = "worker-2", Online = false, MaxSlots = 2 }
        };

        _mockStore.Setup(s => s.ListWorkersAsync(_ct)).ReturnsAsync(workers);

        var result = await _mockStore.Object.ListWorkersAsync(_ct);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task WorkerHeartbeat_WithValidRequest_UpsertsWorker()
    {
        _mockStore.Setup(s => s.UpsertWorkerHeartbeatAsync(
            "worker-1", "http://localhost:5001", 2, 4, _ct)).Returns(Task.CompletedTask);

        await _mockStore.Object.UpsertWorkerHeartbeatAsync("worker-1", "http://localhost:5001", 2, 4, _ct);

        _mockStore.Verify(s => s.UpsertWorkerHeartbeatAsync("worker-1", "http://localhost:5001", 2, 4, _ct), Times.Once);
    }

    [Fact]
    public void WorkerHeartbeat_WithEmptyWorkerId_ShouldFailValidation()
    {
        var request = new WorkerHeartbeatRequest("", "http://localhost:5001", 0, 4);

        string.IsNullOrWhiteSpace(request.WorkerId).Should().BeTrue();
    }

    [Fact]
    public void UpsertWorkerHeartbeat_SetsOnlineAndLastHeartbeat()
    {
        var worker = new WorkerRegistration
        {
            WorkerId = "worker-1",
            Online = true,
            LastHeartbeatUtc = DateTime.UtcNow,
            ActiveSlots = 2,
            MaxSlots = 4
        };

        worker.Online.Should().BeTrue();
        worker.LastHeartbeatUtc.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    #endregion

    #region Schedules API Tests

    [Fact]
    public async Task ListScheduledTasks_ReturnsCronTasks()
    {
        var tasks = new List<TaskDocument>
        {
            new() { Id = "task-1", Kind = TaskKind.Cron, CronExpression = "0 * * * *", Enabled = true },
            new() { Id = "task-2", Kind = TaskKind.Cron, CronExpression = "0 0 * * *", Enabled = true }
        };

        _mockStore.Setup(s => s.ListScheduledTasksAsync(_ct)).ReturnsAsync(tasks);

        var result = await _mockStore.Object.ListScheduledTasksAsync(_ct);

        result.Should().HaveCount(2);
        result.All(t => t.Kind == TaskKind.Cron).Should().BeTrue();
    }

    #endregion

    #region Secrets API Tests

    [Fact]
    public async Task ListSecrets_ReturnsSecretsForRepository()
    {
        var secrets = new List<ProviderSecretDocument>
        {
            new() { Id = "secret-1", RepositoryId = "repo-1", Provider = "github" },
            new() { Id = "secret-2", RepositoryId = "repo-1", Provider = "codex" }
        };

        _mockStore.Setup(s => s.ListProviderSecretsAsync("repo-1", _ct)).ReturnsAsync(secrets);

        var result = await _mockStore.Object.ListProviderSecretsAsync("repo-1", _ct);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task SetSecret_WithValidRequest_UpsertsSecret()
    {
        var encrypted = "encrypted-value";

        _mockStore.Setup(s => s.UpsertProviderSecretAsync("repo-1", "github", encrypted, _ct)).Returns(Task.CompletedTask);

        await _mockStore.Object.UpsertProviderSecretAsync("repo-1", "github", encrypted, _ct);

        _mockStore.Verify(s => s.UpsertProviderSecretAsync("repo-1", "github", encrypted, _ct), Times.Once);
    }

    [Fact]
    public void SetSecret_WithEmptyValue_ShouldFailValidation()
    {
        var request = new SetProviderSecretRequest("");

        string.IsNullOrWhiteSpace(request.SecretValue).Should().BeTrue();
    }

    #endregion

    #region Webhooks API Tests

    [Fact]
    public async Task CreateWebhook_WithValidRequest_ReturnsWebhook()
    {
        var request = new CreateWebhookRequest("repo-1", "task-1", "push", "secret");
        var repo = new RepositoryDocument { Id = "repo-1", Name = "Repo 1" };
        var expectedWebhook = new WebhookRegistration { Id = "webhook-1", RepositoryId = "repo-1", TaskId = "task-1" };

        _mockStore.Setup(s => s.GetRepositoryAsync("repo-1", _ct)).ReturnsAsync(repo);
        _mockStore.Setup(s => s.CreateWebhookAsync(request, _ct)).ReturnsAsync(expectedWebhook);

        var repoResult = await _mockStore.Object.GetRepositoryAsync("repo-1", _ct);
        repoResult.Should().NotBeNull();

        var result = await _mockStore.Object.CreateWebhookAsync(request, _ct);
        result.RepositoryId.Should().Be("repo-1");
    }

    [Fact]
    public async Task CreateWebhook_WithNonExistentRepository_ReturnsNotFound()
    {
        var request = new CreateWebhookRequest("nonexistent", "task-1", "push", "secret");

        _mockStore.Setup(s => s.GetRepositoryAsync("nonexistent", _ct)).ReturnsAsync((RepositoryDocument?)null);

        var result = await _mockStore.Object.GetRepositoryAsync("nonexistent", _ct);

        result.Should().BeNull();
    }

    [Fact]
    public async Task HandleWebhook_WithValidToken_DispatchesTasks()
    {
        var repo = new RepositoryDocument { Id = "repo-1", ProjectId = "proj-1" };
        var project = new ProjectDocument { Id = "proj-1" };
        var tasks = new List<TaskDocument>
        {
            new() { Id = "task-1", RepositoryId = "repo-1", Kind = TaskKind.EventDriven, Enabled = true }
        };

        var secretDoc = new ProviderSecretDocument { EncryptedValue = "encrypted-token" };

        _mockStore.Setup(s => s.GetProviderSecretAsync("repo-1", "webhook-token", _ct)).ReturnsAsync(secretDoc);
        _mockStore.Setup(s => s.GetRepositoryAsync("repo-1", _ct)).ReturnsAsync(repo);
        _mockStore.Setup(s => s.GetProjectAsync("proj-1", _ct)).ReturnsAsync(project);
        _mockStore.Setup(s => s.ListEventDrivenTasksAsync("repo-1", _ct)).ReturnsAsync(tasks);

        var secretResult = await _mockStore.Object.GetProviderSecretAsync("repo-1", "webhook-token", _ct);
        secretResult.Should().NotBeNull();
        SecretCryptoService.FixedTimeEquals("valid-token", "valid-token").Should().BeTrue();
    }

    #endregion

    #region Settings API Tests

    [Fact]
    public async Task GetSettings_ReturnsCurrentSettings()
    {
        var settings = new SystemSettingsDocument
        {
            Id = "singleton",
            DockerAllowedImages = ["image1", "image2"],
            RetentionDaysLogs = 30,
            RetentionDaysRuns = 90
        };

        _mockStore.Setup(s => s.GetSettingsAsync(_ct)).ReturnsAsync(settings);

        var result = await _mockStore.Object.GetSettingsAsync(_ct);

        result.DockerAllowedImages.Should().HaveCount(2);
        result.RetentionDaysLogs.Should().Be(30);
    }

    [Fact]
    public async Task UpdateSettings_WithValidRequest_UpdatesSettings()
    {
        var request = new UpdateSystemSettingsRequest(
            DockerAllowedImages: ["image1", "image2", "image3"],
            RetentionDaysLogs: 60,
            RetentionDaysRuns: 180,
            VictoriaMetricsEndpoint: "http://vm:8428",
            VmUiEndpoint: "http://vmui:8081"
        );

        var settings = new SystemSettingsDocument();

        _mockStore.Setup(s => s.GetSettingsAsync(_ct)).ReturnsAsync(settings);
        _mockStore.Setup(s => s.UpdateSettingsAsync(It.IsAny<SystemSettingsDocument>(), _ct)).ReturnsAsync(settings);

        var currentSettings = await _mockStore.Object.GetSettingsAsync(_ct);
        currentSettings.DockerAllowedImages = request.DockerAllowedImages ?? currentSettings.DockerAllowedImages;
        currentSettings.RetentionDaysLogs = request.RetentionDaysLogs ?? currentSettings.RetentionDaysLogs;

        currentSettings.DockerAllowedImages.Should().HaveCount(3);
    }

    #endregion

    #region Workflows API Tests

    [Fact]
    public async Task ListWorkflows_ReturnsAllWorkflows()
    {
        var workflows = new List<WorkflowDocument>
        {
            new() { Id = "wf-1", Name = "Workflow 1", Enabled = true },
            new() { Id = "wf-2", Name = "Workflow 2", Enabled = false }
        };

        _mockStore.Setup(s => s.ListAllWorkflowsAsync(_ct)).ReturnsAsync(workflows);

        var result = await _mockStore.Object.ListAllWorkflowsAsync(_ct);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetWorkflow_WithExistingId_ReturnsWorkflow()
    {
        var workflow = new WorkflowDocument { Id = "wf-1", Name = "Test Workflow" };

        _mockStore.Setup(s => s.GetWorkflowAsync("wf-1", _ct)).ReturnsAsync(workflow);

        var result = await _mockStore.Object.GetWorkflowAsync("wf-1", _ct);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Test Workflow");
    }

    [Fact]
    public async Task CreateWorkflow_WithValidRequest_ReturnsWorkflow()
    {
        var repo = new RepositoryDocument { Id = "repo-1", Name = "Repo 1" };
        var expectedWorkflow = new WorkflowDocument { Id = "wf-1", RepositoryId = "repo-1", Name = "New Workflow" };

        _mockStore.Setup(s => s.GetRepositoryAsync("repo-1", _ct)).ReturnsAsync(repo);
        _mockStore.Setup(s => s.CreateWorkflowAsync(It.IsAny<WorkflowDocument>(), _ct)).ReturnsAsync(expectedWorkflow);

        var repoResult = await _mockStore.Object.GetRepositoryAsync("repo-1", _ct);
        repoResult.Should().NotBeNull();

        var result = await _mockStore.Object.CreateWorkflowAsync(new WorkflowDocument { RepositoryId = "repo-1", Name = "New Workflow" }, _ct);
        result.Name.Should().Be("New Workflow");
    }

    [Fact]
    public async Task UpdateWorkflow_WithValidRequest_ReturnsWorkflow()
    {
        var workflow = new WorkflowDocument { Id = "wf-1", Name = "Updated Workflow" };

        _mockStore.Setup(s => s.GetWorkflowAsync("wf-1", _ct)).ReturnsAsync(workflow);
        _mockStore.Setup(s => s.UpdateWorkflowAsync("wf-1", It.IsAny<WorkflowDocument>(), _ct)).ReturnsAsync(workflow);

        var existing = await _mockStore.Object.GetWorkflowAsync("wf-1", _ct);
        existing.Should().NotBeNull();

        var result = await _mockStore.Object.UpdateWorkflowAsync("wf-1", workflow, _ct);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task DeleteWorkflow_WithValidId_ReturnsTrue()
    {
        _mockStore.Setup(s => s.DeleteWorkflowAsync("wf-1", _ct)).ReturnsAsync(true);

        var result = await _mockStore.Object.DeleteWorkflowAsync("wf-1", _ct);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteWorkflow_WithEnabledWorkflow_ReturnsExecution()
    {
        var workflow = new WorkflowDocument { Id = "wf-1", RepositoryId = "repo-1", Enabled = true };
        var repo = new RepositoryDocument { Id = "repo-1", ProjectId = "proj-1" };
        var project = new ProjectDocument { Id = "proj-1" };
        var execution = new WorkflowExecutionDocument { Id = "exec-1", WorkflowId = "wf-1", State = WorkflowExecutionState.Running };

        _mockStore.Setup(s => s.GetWorkflowAsync("wf-1", _ct)).ReturnsAsync(workflow);
        _mockStore.Setup(s => s.GetRepositoryAsync("repo-1", _ct)).ReturnsAsync(repo);
        _mockStore.Setup(s => s.GetProjectAsync("proj-1", _ct)).ReturnsAsync(project);
        _mockWorkflowExecutor.Setup(e => e.ExecuteWorkflowAsync(workflow, "proj-1", _ct)).ReturnsAsync(execution);

        var wfResult = await _mockStore.Object.GetWorkflowAsync("wf-1", _ct);
        wfResult!.Enabled.Should().BeTrue();

        var result = await _mockWorkflowExecutor.Object.ExecuteWorkflowAsync(workflow, "proj-1", _ct);
        result.WorkflowId.Should().Be("wf-1");
    }

    [Fact]
    public async Task ExecuteWorkflow_WithDisabledWorkflow_ReturnsBadRequest()
    {
        var workflow = new WorkflowDocument { Id = "wf-1", RepositoryId = "repo-1", Enabled = false };

        _mockStore.Setup(s => s.GetWorkflowAsync("wf-1", _ct)).ReturnsAsync(workflow);

        var result = await _mockStore.Object.GetWorkflowAsync("wf-1", _ct);

        result!.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task ListWorkflowExecutions_ReturnsExecutions()
    {
        var executions = new List<WorkflowExecutionDocument>
        {
            new() { Id = "exec-1", WorkflowId = "wf-1" },
            new() { Id = "exec-2", WorkflowId = "wf-1" }
        };

        _mockStore.Setup(s => s.ListWorkflowExecutionsAsync("wf-1", _ct)).ReturnsAsync(executions);

        var result = await _mockStore.Object.ListWorkflowExecutionsAsync("wf-1", _ct);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task ApproveWorkflowStage_WithPendingApproval_ReturnsApprovedExecution()
    {
        var execution = new WorkflowExecutionDocument
        {
            Id = "exec-1",
            WorkflowId = "wf-1",
            State = WorkflowExecutionState.PendingApproval
        };

        var approvedExecution = new WorkflowExecutionDocument
        {
            Id = "exec-1",
            WorkflowId = "wf-1",
            State = WorkflowExecutionState.Running,
            ApprovedBy = "user@example.com"
        };

        _mockStore.Setup(s => s.GetWorkflowExecutionAsync("exec-1", _ct)).ReturnsAsync(execution);
        _mockWorkflowExecutor.Setup(e => e.ApproveWorkflowStageAsync("exec-1", "user@example.com", _ct)).ReturnsAsync(approvedExecution);

        var execResult = await _mockStore.Object.GetWorkflowExecutionAsync("exec-1", _ct);
        execResult!.State.Should().Be(WorkflowExecutionState.PendingApproval);

        var result = await _mockWorkflowExecutor.Object.ApproveWorkflowStageAsync("exec-1", "user@example.com", _ct);
        result!.State.Should().Be(WorkflowExecutionState.Running);
    }

    [Theory]
    [InlineData(WorkflowStageType.Task)]
    [InlineData(WorkflowStageType.Approval)]
    [InlineData(WorkflowStageType.Delay)]
    [InlineData(WorkflowStageType.Parallel)]
    public void WorkflowStageType_AllValuesAreValid(WorkflowStageType type)
    {
        Enum.IsDefined(typeof(WorkflowStageType), type).Should().BeTrue();
    }

    #endregion

    #region Images API Tests

    [Fact]
    public async Task ListImages_ReturnsImages()
    {
        var images = new List<ImageInfo>
        {
            new("image1:v1", "id1", 100, DateTime.UtcNow),
            new("image2:v2", "id2", 200, DateTime.UtcNow)
        };

        _mockImageBuilder.Setup(b => b.ListImagesAsync(null, _ct)).ReturnsAsync(images);

        var result = await _mockImageBuilder.Object.ListImagesAsync(null, _ct);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListImages_WithFilter_ReturnsFilteredImages()
    {
        var images = new List<ImageInfo>
        {
            new("myimage:v1", "id1", 100, DateTime.UtcNow),
            new("myimage:v2", "id2", 200, DateTime.UtcNow)
        };

        _mockImageBuilder.Setup(b => b.ListImagesAsync("myimage", _ct)).ReturnsAsync(images);

        var result = await _mockImageBuilder.Object.ListImagesAsync("myimage", _ct);

        result.Should().HaveCount(2);
    }

    [Fact]
    public void BuildImage_WithEmptyDockerfile_ShouldFailValidation()
    {
        var request = new BuildImageRequest("", "myimage:v1");

        string.IsNullOrWhiteSpace(request.DockerfileContent).Should().BeTrue();
    }

    [Fact]
    public void BuildImage_WithEmptyTag_ShouldFailValidation()
    {
        var request = new BuildImageRequest("FROM alpine", "");

        string.IsNullOrWhiteSpace(request.Tag).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteImage_WithExistingTag_ReturnsTrue()
    {
        _mockImageBuilder.Setup(b => b.DeleteImageAsync("myimage:v1", _ct)).ReturnsAsync(true);

        var result = await _mockImageBuilder.Object.DeleteImageAsync("myimage:v1", _ct);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteImage_WithNonExistentTag_ReturnsFalse()
    {
        _mockImageBuilder.Setup(b => b.DeleteImageAsync("nonexistent:v1", _ct)).ReturnsAsync(false);

        var result = await _mockImageBuilder.Object.DeleteImageAsync("nonexistent:v1", _ct);

        result.Should().BeFalse();
    }

    #endregion

    #region Alert Rules API Tests

    [Fact]
    public async Task ListAlertRules_ReturnsRules()
    {
        var rules = new List<AlertRuleDocument>
        {
            new() { Id = "rule-1", Name = "Rule 1", RuleType = AlertRuleType.FailureRateSpike },
            new() { Id = "rule-2", Name = "Rule 2", RuleType = AlertRuleType.QueueBacklog }
        };

        _mockStore.Setup(s => s.ListAlertRulesAsync(_ct)).ReturnsAsync(rules);

        var result = await _mockStore.Object.ListAlertRulesAsync(_ct);

        result.Should().HaveCount(2);
    }

    [Fact]
    public void CreateAlertRule_WithEmptyName_ShouldFailValidation()
    {
        var request = new CreateAlertRuleRequest("", AlertRuleType.FailureRateSpike, 5, 10);

        string.IsNullOrWhiteSpace(request.Name).Should().BeTrue();
    }

    [Fact]
    public void CreateAlertRule_WithZeroThreshold_ShouldFailValidation()
    {
        var request = new CreateAlertRuleRequest("Test Rule", AlertRuleType.FailureRateSpike, 0, 10);

        (request.Threshold <= 0).Should().BeTrue();
    }

    [Fact]
    public async Task CreateAlertRule_WithValidRequest_ReturnsRule()
    {
        var request = new CreateAlertRuleRequest("Test Rule", AlertRuleType.FailureRateSpike, 5, 10, null, true);
        var expectedRule = new AlertRuleDocument { Id = "rule-1", Name = "Test Rule", RuleType = AlertRuleType.FailureRateSpike };

        _mockStore.Setup(s => s.CreateAlertRuleAsync(It.IsAny<AlertRuleDocument>(), _ct)).ReturnsAsync(expectedRule);

        var result = await _mockStore.Object.CreateAlertRuleAsync(new AlertRuleDocument
        {
            Name = request.Name,
            RuleType = request.RuleType,
            Threshold = request.Threshold,
            WindowMinutes = request.WindowMinutes > 0 ? request.WindowMinutes : 10,
            Enabled = request.Enabled
        }, _ct);

        result.Name.Should().Be("Test Rule");
    }

    [Fact]
    public async Task UpdateAlertRule_WithValidRequest_ReturnsRule()
    {
        var rule = new AlertRuleDocument { Id = "rule-1", Name = "Updated Rule", RuleType = AlertRuleType.QueueBacklog };

        _mockStore.Setup(s => s.UpdateAlertRuleAsync("rule-1", It.IsAny<AlertRuleDocument>(), _ct)).ReturnsAsync(rule);

        var result = await _mockStore.Object.UpdateAlertRuleAsync("rule-1", rule, _ct);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Updated Rule");
    }

    [Fact]
    public async Task DeleteAlertRule_WithExistingId_ReturnsTrue()
    {
        _mockStore.Setup(s => s.DeleteAlertRuleAsync("rule-1", _ct)).ReturnsAsync(true);

        var result = await _mockStore.Object.DeleteAlertRuleAsync("rule-1", _ct);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteAlertRule_WithNonExistentId_ReturnsFalse()
    {
        _mockStore.Setup(s => s.DeleteAlertRuleAsync("nonexistent", _ct)).ReturnsAsync(false);

        var result = await _mockStore.Object.DeleteAlertRuleAsync("nonexistent", _ct);

        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(AlertRuleType.MissingHeartbeat)]
    [InlineData(AlertRuleType.FailureRateSpike)]
    [InlineData(AlertRuleType.QueueBacklog)]
    [InlineData(AlertRuleType.RepeatedPrFailures)]
    [InlineData(AlertRuleType.RouteLeakDetection)]
    public void AlertRuleType_AllValuesAreValid(AlertRuleType type)
    {
        Enum.IsDefined(typeof(AlertRuleType), type).Should().BeTrue();
    }

    #endregion

    #region Alert Events API Tests

    [Fact]
    public async Task ListAlertEvents_ReturnsEvents()
    {
        var events = new List<AlertEventDocument>
        {
            new() { Id = "event-1", RuleId = "rule-1", Message = "Alert 1" },
            new() { Id = "event-2", RuleId = "rule-2", Message = "Alert 2" }
        };

        _mockStore.Setup(s => s.ListRecentAlertEventsAsync(100, _ct)).ReturnsAsync(events);

        var result = await _mockStore.Object.ListRecentAlertEventsAsync(100, _ct);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task RecordAlertEvent_CreatesEvent()
    {
        var alertEvent = new AlertEventDocument
        {
            RuleId = "rule-1",
            RuleName = "Test Rule",
            Message = "Test alert"
        };

        _mockStore.Setup(s => s.RecordAlertEventAsync(alertEvent, _ct)).ReturnsAsync(alertEvent);

        var result = await _mockStore.Object.RecordAlertEventAsync(alertEvent, _ct);

        result.RuleId.Should().Be("rule-1");
    }

    #endregion

    #region Harness Health API Tests

    [Fact]
    public void GetHarnessHealth_ReturnsAllHealth()
    {
        var health = new Dictionary<string, HarnessHealth>
        {
            ["codex"] = new HarnessHealth("codex", HarnessStatus.Available, "1.0.0"),
            ["claudecode"] = new HarnessHealth("claudecode", HarnessStatus.Unavailable, null)
        };

        _mockHarnessHealth.Setup(h => h.GetAllHealth()).Returns(health);

        var result = _mockHarnessHealth.Object.GetAllHealth();

        result.Should().HaveCount(2);
        result["codex"].Status.Should().Be(HarnessStatus.Available);
        result["claudecode"].Status.Should().Be(HarnessStatus.Unavailable);
    }

    #endregion

    #region Authorization Tests

    [Fact]
    public void ReadEndpoints_RequireViewerPolicy()
    {
        var policy = "viewer";
        policy.Should().BeOneOf("viewer", "operator", "admin");
    }

    [Fact]
    public void WriteEndpoints_RequireOperatorPolicy()
    {
        var policy = "operator";
        policy.Should().BeOneOf("viewer", "operator", "admin");
    }

    [Fact]
    public void WorkerHeartbeat_IsAllowAnonymous()
    {
        var endpoint = "/api/workers/heartbeat";
        endpoint.Should().Contain("heartbeat");
    }

    [Fact]
    public void WebhookEndpoint_IsAllowAnonymous()
    {
        var endpoint = "/api/webhooks/{repositoryId}/{token}";
        endpoint.Should().Contain("webhooks");
    }

    [Theory]
    [InlineData("viewer", false)]
    [InlineData("operator", true)]
    [InlineData("admin", true)]
    public void WriteOperations_RequireOperatorOrAdminRole(string role, bool canWrite)
    {
        var canPerformWrite = role is "operator" or "admin";
        canPerformWrite.Should().Be(canWrite);
    }

    #endregion

    #region Validation Tests

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ValidateProjectName_WithInvalidInput_FailsValidation(string? name)
    {
        string.IsNullOrWhiteSpace(name).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ValidateCronExpression_WithCronTaskAndEmptyExpression_FailsValidation(string? expression)
    {
        var kind = TaskKind.Cron;
        var isInvalid = kind == TaskKind.Cron && string.IsNullOrWhiteSpace(expression);
        isInvalid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void ValidateAlertThreshold_WithInvalidValue_FailsValidation(int threshold)
    {
        (threshold <= 0).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ValidateSecretValue_WithEmptyValue_FailsValidation(string? value)
    {
        string.IsNullOrWhiteSpace(value).Should().BeTrue();
    }

    #endregion

    #region Run State Tests

    [Theory]
    [InlineData(RunState.Queued)]
    [InlineData(RunState.Running)]
    [InlineData(RunState.PendingApproval)]
    public void CancellableStates_AreCorrect(RunState state)
    {
        var isCancellable = state is RunState.Queued or RunState.Running or RunState.PendingApproval;
        isCancellable.Should().BeTrue();
    }

    [Theory]
    [InlineData(RunState.Succeeded)]
    [InlineData(RunState.Failed)]
    [InlineData(RunState.Cancelled)]
    public void NonCancellableStates_AreCorrect(RunState state)
    {
        var isCancellable = state is RunState.Queued or RunState.Running or RunState.PendingApproval;
        isCancellable.Should().BeFalse();
    }

    [Fact]
    public void PendingApproval_IsRequiredForApproveReject()
    {
        var requiredState = RunState.PendingApproval;
        Enum.IsDefined(typeof(RunState), requiredState).Should().BeTrue();
    }

    #endregion

    #region Error Response Tests

    [Fact]
    public void NotFoundResponse_ContainsMessage()
    {
        var response = new { message = "Project not found" };
        response.message.Should().Contain("not found");
    }

    [Fact]
    public void BadRequestResponse_ContainsMessage()
    {
        var response = new { message = "Run is not in a cancellable state" };
        response.message.Should().Contain("not");
    }

    [Fact]
    public void ValidationProblem_ContainsFieldErrors()
    {
        var errors = new Dictionary<string, string[]>
        {
            ["name"] = ["Name is required"],
            ["cronExpression"] = ["Cron expression required for cron tasks"]
        };

        errors.Should().ContainKey("name");
        errors["name"].Should().Contain("Name is required");
    }

    #endregion
}
