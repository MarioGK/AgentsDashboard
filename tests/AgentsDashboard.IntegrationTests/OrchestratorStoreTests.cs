using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.IntegrationTests.Infrastructure;
using FluentAssertions;

namespace AgentsDashboard.IntegrationTests;

[Collection("Sqlite")]
public class OrchestratorStoreTests(SqliteFixture sqlite)
{
    private OrchestratorStore CreateStore() => TestOrchestratorStore.Create(sqlite.ConnectionString);

    [Fact]
    public async Task CreateProject_RoundTrips()
    {
        var store = CreateStore();
        await store.InitializeAsync(CancellationToken.None);

        var project = await store.CreateProjectAsync(new CreateProjectRequest("TestProject", "desc"), CancellationToken.None);

        project.Name.Should().Be("TestProject");
        project.Id.Should().NotBeNullOrEmpty();

        var fetched = await store.GetProjectAsync(project.Id, CancellationToken.None);
        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("TestProject");
    }

    [Fact]
    public async Task CreateRepository_RoundTrips()
    {
        var store = CreateStore();
        await store.InitializeAsync(CancellationToken.None);

        var project = await store.CreateProjectAsync(new CreateProjectRequest("P", "d"), CancellationToken.None);
        var repo = await store.CreateRepositoryAsync(new CreateRepositoryRequest(project.Id, "MyRepo", "https://github.com/test/repo.git", "main"), CancellationToken.None);

        repo.Name.Should().Be("MyRepo");
        repo.ProjectId.Should().Be(project.Id);

        var list = await store.ListRepositoriesAsync(project.Id, CancellationToken.None);
        list.Should().ContainSingle(r => r.Id == repo.Id);
    }

    [Fact]
    public async Task CreateTask_WithRetryPolicy_Persists()
    {
        var store = CreateStore();
        await store.InitializeAsync(CancellationToken.None);

        var project = await store.CreateProjectAsync(new CreateProjectRequest("P", "d"), CancellationToken.None);
        var repo = await store.CreateRepositoryAsync(new CreateRepositoryRequest(project.Id, "R", "https://github.com/x/y.git", "main"), CancellationToken.None);

        var task = await store.CreateTaskAsync(new CreateTaskRequest(
            repo.Id, "MyTask", TaskKind.OneShot, "codex", "fix bugs", "npm test", false, "",
            Enabled: true,
            RetryPolicy: new RetryPolicyConfig(3, 5, 1.5)),
            CancellationToken.None);

        task.RetryPolicy.MaxAttempts.Should().Be(3);
        task.RetryPolicy.BackoffBaseSeconds.Should().Be(5);

        var fetched = await store.GetTaskAsync(task.Id, CancellationToken.None);
        fetched.Should().NotBeNull();
        fetched!.RetryPolicy.MaxAttempts.Should().Be(3);
    }

    [Fact]
    public async Task RunLifecycle_CreateStartComplete()
    {
        var store = CreateStore();
        await store.InitializeAsync(CancellationToken.None);

        var project = await store.CreateProjectAsync(new CreateProjectRequest("P", "d"), CancellationToken.None);
        var repo = await store.CreateRepositoryAsync(new CreateRepositoryRequest(project.Id, "R", "https://github.com/x/y.git", "main"), CancellationToken.None);
        var task = await store.CreateTaskAsync(new CreateTaskRequest(repo.Id, "T", TaskKind.OneShot, "codex", "p", "cmd", false, "", true), CancellationToken.None);

        var run = await store.CreateRunAsync(task, project.Id, CancellationToken.None);
        run.State.Should().Be(RunState.Queued);
        run.Attempt.Should().Be(1);

        var started = await store.MarkRunStartedAsync(run.Id, CancellationToken.None);
        started.Should().NotBeNull();
        started!.State.Should().Be(RunState.Running);
        started.StartedAtUtc.Should().NotBeNull();

        var completed = await store.MarkRunCompletedAsync(run.Id, true, "Done", "{}", CancellationToken.None);
        completed.Should().NotBeNull();
        completed!.State.Should().Be(RunState.Succeeded);
        completed.EndedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task RunLifecycle_CancelRun()
    {
        var store = CreateStore();
        await store.InitializeAsync(CancellationToken.None);

        var project = await store.CreateProjectAsync(new CreateProjectRequest("P", "d"), CancellationToken.None);
        var repo = await store.CreateRepositoryAsync(new CreateRepositoryRequest(project.Id, "R", "https://github.com/x/y.git", "main"), CancellationToken.None);
        var task = await store.CreateTaskAsync(new CreateTaskRequest(repo.Id, "T", TaskKind.OneShot, "codex", "p", "cmd", false, "", true), CancellationToken.None);

        var run = await store.CreateRunAsync(task, project.Id, CancellationToken.None);
        var cancelled = await store.MarkRunCancelledAsync(run.Id, CancellationToken.None);

        cancelled.Should().NotBeNull();
        cancelled!.State.Should().Be(RunState.Cancelled);
    }

    [Fact]
    public async Task RunWithAttempt_CreatesWithCorrectAttempt()
    {
        var store = CreateStore();
        await store.InitializeAsync(CancellationToken.None);

        var project = await store.CreateProjectAsync(new CreateProjectRequest("P", "d"), CancellationToken.None);
        var repo = await store.CreateRepositoryAsync(new CreateRepositoryRequest(project.Id, "R", "https://github.com/x/y.git", "main"), CancellationToken.None);
        var task = await store.CreateTaskAsync(new CreateTaskRequest(repo.Id, "T", TaskKind.OneShot, "codex", "p", "cmd", false, "", true), CancellationToken.None);

        var run = await store.CreateRunAsync(task, project.Id, CancellationToken.None, attempt: 3);
        run.Attempt.Should().Be(3);
    }

    [Fact]
    public async Task ConcurrencyCounting_Works()
    {
        var store = CreateStore();
        await store.InitializeAsync(CancellationToken.None);

        var project = await store.CreateProjectAsync(new CreateProjectRequest("P", "d"), CancellationToken.None);
        var repo = await store.CreateRepositoryAsync(new CreateRepositoryRequest(project.Id, "R", "https://github.com/x/y.git", "main"), CancellationToken.None);
        var task = await store.CreateTaskAsync(new CreateTaskRequest(repo.Id, "T", TaskKind.OneShot, "codex", "p", "cmd", false, "", true), CancellationToken.None);

        var run1 = await store.CreateRunAsync(task, project.Id, CancellationToken.None);
        var run2 = await store.CreateRunAsync(task, project.Id, CancellationToken.None);

        var globalCount = await store.CountActiveRunsAsync(CancellationToken.None);
        globalCount.Should().Be(2);

        var projectCount = await store.CountActiveRunsByProjectAsync(project.Id, CancellationToken.None);
        projectCount.Should().Be(2);

        await store.MarkRunCompletedAsync(run1.Id, true, "done", "{}", CancellationToken.None);

        globalCount = await store.CountActiveRunsAsync(CancellationToken.None);
        globalCount.Should().Be(1);
    }

    [Fact]
    public async Task FindingsLifecycle_CreateUpdateQuery()
    {
        var store = CreateStore();
        await store.InitializeAsync(CancellationToken.None);

        var project = await store.CreateProjectAsync(new CreateProjectRequest("P", "d"), CancellationToken.None);
        var repo = await store.CreateRepositoryAsync(new CreateRepositoryRequest(project.Id, "R", "https://github.com/x/y.git", "main"), CancellationToken.None);
        var task = await store.CreateTaskAsync(new CreateTaskRequest(repo.Id, "T", TaskKind.OneShot, "codex", "p", "cmd", false, "", true), CancellationToken.None);

        var run = await store.CreateRunAsync(task, project.Id, CancellationToken.None);
        await store.MarkRunCompletedAsync(run.Id, false, "failed", "{}", CancellationToken.None, failureClass: "TestFailure");

        var finding = await store.CreateFindingFromFailureAsync(run, "Something broke", CancellationToken.None);
        finding.State.Should().Be(FindingState.New);
        finding.Severity.Should().Be(FindingSeverity.High);

        var updated = await store.UpdateFindingStateAsync(finding.Id, FindingState.Acknowledged, CancellationToken.None);
        updated.Should().NotBeNull();
        updated!.State.Should().Be(FindingState.Acknowledged);

        var all = await store.ListAllFindingsAsync(CancellationToken.None);
        all.Should().ContainSingle(f => f.Id == finding.Id);
    }

    [Fact]
    public async Task RunLogs_PersistAndQuery()
    {
        var store = CreateStore();
        await store.InitializeAsync(CancellationToken.None);

        var logEvent = new RunLogEvent { RunId = "test-run-id", Level = "info", Message = "Hello" };
        await store.AddRunLogAsync(logEvent, CancellationToken.None);

        var logs = await store.ListRunLogsAsync("test-run-id", CancellationToken.None);
        logs.Should().ContainSingle(l => l.Message == "Hello");
    }

    [Fact]
    public async Task Workers_UpsertAndList()
    {
        var store = CreateStore();
        await store.InitializeAsync(CancellationToken.None);

        await store.UpsertWorkerHeartbeatAsync("w1", "http://localhost:5201", 2, 4, CancellationToken.None);
        await store.UpsertWorkerHeartbeatAsync("w1", "http://localhost:5201", 3, 4, CancellationToken.None);

        var workers = await store.ListWorkersAsync(CancellationToken.None);
        workers.Should().ContainSingle(w => w.WorkerId == "w1");
        workers[0].ActiveSlots.Should().Be(3);
    }

    [Fact]
    public async Task ScheduledTasks_ListCronTasks()
    {
        var store = CreateStore();
        await store.InitializeAsync(CancellationToken.None);

        var project = await store.CreateProjectAsync(new CreateProjectRequest("P", "d"), CancellationToken.None);
        var repo = await store.CreateRepositoryAsync(new CreateRepositoryRequest(project.Id, "R", "https://github.com/x/y.git", "main"), CancellationToken.None);

        await store.CreateTaskAsync(new CreateTaskRequest(repo.Id, "Cron Task", TaskKind.Cron, "codex", "p", "cmd", false, "0 * * * *", true), CancellationToken.None);
        await store.CreateTaskAsync(new CreateTaskRequest(repo.Id, "OneShot Task", TaskKind.OneShot, "codex", "p", "cmd", false, "", true), CancellationToken.None);

        var scheduled = await store.ListScheduledTasksAsync(CancellationToken.None);
        scheduled.Should().ContainSingle(t => t.Name == "Cron Task");
    }

    [Fact]
    public async Task Webhooks_CreateAndList()
    {
        var store = CreateStore();
        await store.InitializeAsync(CancellationToken.None);

        var project = await store.CreateProjectAsync(new CreateProjectRequest("P", "d"), CancellationToken.None);
        var repo = await store.CreateRepositoryAsync(new CreateRepositoryRequest(project.Id, "R", "https://github.com/x/y.git", "main"), CancellationToken.None);

        var webhook = await store.CreateWebhookAsync(new CreateWebhookRequest(repo.Id, "task1", "push", "secret123"), CancellationToken.None);
        webhook.RepositoryId.Should().Be(repo.Id);

        var list = await store.ListWebhooksAsync(repo.Id, CancellationToken.None);
        list.Should().ContainSingle(w => w.Id == webhook.Id);
    }

    [Fact]
    public void ComputeNextRun_CronTask_ReturnsNextOccurrence()
    {
        var task = new TaskDocument { Kind = TaskKind.Cron, CronExpression = "0 * * * *", Enabled = true };
        var next = OrchestratorStore.ComputeNextRun(task, new DateTime(2026, 1, 1, 12, 30, 0, DateTimeKind.Utc));

        next.Should().NotBeNull();
        next!.Value.Should().Be(new DateTime(2026, 1, 1, 13, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void ComputeNextRun_DisabledTask_ReturnsNull()
    {
        var task = new TaskDocument { Kind = TaskKind.Cron, CronExpression = "0 * * * *", Enabled = false };
        var next = OrchestratorStore.ComputeNextRun(task, DateTime.UtcNow);
        next.Should().BeNull();
    }
}
