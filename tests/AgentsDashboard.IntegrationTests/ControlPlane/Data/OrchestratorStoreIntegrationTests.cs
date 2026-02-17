using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.IntegrationTests.ControlPlane.Data;

public sealed class OrchestratorStoreIntegrationTests
{
    [Test]
    public async Task ListPromptSkillsAsync_WhenIncludingGlobalScope_ReturnsRepositoryThenGlobalSkills()
    {
        await using var fixture = await OrchestratorStoreIntegrationFixture.CreateAsync();

        const string repositoryId = "repo-a";

        var repositorySkill = await fixture.Store.CreatePromptSkillAsync(
            new CreatePromptSkillRequest(
                RepositoryId: repositoryId,
                Name: "Repository Skill",
                Trigger: "repo-skill",
                Content: "Use repository context",
                Description: "repository scoped"),
            CancellationToken.None);

        var globalSkill = await fixture.Store.CreatePromptSkillAsync(
            new CreatePromptSkillRequest(
                RepositoryId: "global",
                Name: "Global Skill",
                Trigger: "GLOBAL-SKILL",
                Content: "Use global context",
                Description: "global scoped"),
            CancellationToken.None);

        repositorySkill.RepositoryId.Should().Be(repositoryId);
        globalSkill.RepositoryId.Should().Be("global");
        globalSkill.Trigger.Should().Be("global-skill");

        var withGlobal = await fixture.Store.ListPromptSkillsAsync(repositoryId, includeGlobal: true, CancellationToken.None);

        withGlobal.Select(skill => skill.RepositoryId).Should().Equal(repositoryId, "global");
        withGlobal.Select(skill => skill.Trigger).Should().Equal("repo-skill", "global-skill");

        var repositoryOnly = await fixture.Store.ListPromptSkillsAsync(repositoryId, includeGlobal: false, CancellationToken.None);

        repositoryOnly.Should().ContainSingle();
        repositoryOnly[0].Trigger.Should().Be("repo-skill");
    }

    [Test]
    public async Task TaskAndRunStateOperations_WhenPersisted_ReflectDueTasksAndLatestRunState()
    {
        await using var fixture = await OrchestratorStoreIntegrationFixture.CreateAsync();

        var repository = await fixture.Store.CreateRepositoryAsync(
            new CreateRepositoryRequest(
                Name: "Test Repository",
                GitUrl: "https://example.com/org/repo.git",
                LocalPath: "/tmp/test-repository",
                DefaultBranch: "main"),
            CancellationToken.None);

        var oneShotTask = await fixture.Store.CreateTaskAsync(
            new CreateTaskRequest(
                RepositoryId: repository.Id,
                Name: "One-shot task",
                Kind: TaskKind.OneShot,
                Harness: "Codex",
                Prompt: "Do one thing",
                Command: "echo one-shot",
                AutoCreatePullRequest: false,
                CronExpression: string.Empty,
                Enabled: true),
            CancellationToken.None);

        var cronTask = await fixture.Store.CreateTaskAsync(
            new CreateTaskRequest(
                RepositoryId: repository.Id,
                Name: "Cron task",
                Kind: TaskKind.Cron,
                Harness: "Codex",
                Prompt: "Do recurring thing",
                Command: "echo cron",
                AutoCreatePullRequest: false,
                CronExpression: "* * * * *",
                Enabled: true),
            CancellationToken.None);

        var disabledTask = await fixture.Store.CreateTaskAsync(
            new CreateTaskRequest(
                RepositoryId: repository.Id,
                Name: "Disabled task",
                Kind: TaskKind.OneShot,
                Harness: "Codex",
                Prompt: "Disabled",
                Command: "echo disabled",
                AutoCreatePullRequest: false,
                CronExpression: string.Empty,
                Enabled: false),
            CancellationToken.None);

        await fixture.Store.UpdateTaskNextRunAsync(cronTask.Id, DateTime.UtcNow.AddMinutes(-1), CancellationToken.None);

        var dueTaskIds = (await fixture.Store.ListDueTasksAsync(DateTime.UtcNow, limit: 10, CancellationToken.None))
            .Select(task => task.Id)
            .ToList();

        dueTaskIds.Should().Contain(oneShotTask.Id);
        dueTaskIds.Should().Contain(cronTask.Id);
        dueTaskIds.Should().NotContain(disabledTask.Id);

        await fixture.Store.MarkOneShotTaskConsumedAsync(oneShotTask.Id, CancellationToken.None);

        var dueTaskIdsAfterConsume = (await fixture.Store.ListDueTasksAsync(DateTime.UtcNow, limit: 10, CancellationToken.None))
            .Select(task => task.Id)
            .ToList();

        dueTaskIdsAfterConsume.Should().NotContain(oneShotTask.Id);

        var firstRun = await fixture.Store.CreateRunAsync(cronTask, CancellationToken.None);
        await fixture.Store.MarkRunStartedAsync(firstRun.Id, "worker-1", CancellationToken.None);
        await fixture.Store.MarkRunCompletedAsync(firstRun.Id, succeeded: false, summary: "failed", outputJson: "{}", CancellationToken.None);

        var secondRun = await fixture.Store.CreateRunAsync(cronTask, CancellationToken.None);
        await fixture.Store.MarkRunStartedAsync(secondRun.Id, "worker-2", CancellationToken.None);

        var latestStates = await fixture.Store.GetLatestRunStatesByTaskIdsAsync([cronTask.Id, oneShotTask.Id], CancellationToken.None);

        latestStates.Should().ContainKey(cronTask.Id);
        latestStates[cronTask.Id].Should().Be(RunState.Running);
        latestStates.Should().NotContainKey(oneShotTask.Id);

        var activeRunCount = await fixture.Store.CountActiveRunsByTaskAsync(cronTask.Id, CancellationToken.None);
        activeRunCount.Should().Be(1);
    }

    [Test]
    public async Task StructuredRunPersistence_WhenAppendingEventsAndDiffSnapshots_PersistsReplayState()
    {
        await using var fixture = await OrchestratorStoreIntegrationFixture.CreateAsync();

        var repository = await fixture.Store.CreateRepositoryAsync(
            new CreateRepositoryRequest(
                Name: "Structured Repository",
                GitUrl: "https://example.com/org/structured.git",
                LocalPath: "/tmp/structured-repository",
                DefaultBranch: "main"),
            CancellationToken.None);

        var task = await fixture.Store.CreateTaskAsync(
            new CreateTaskRequest(
                RepositoryId: repository.Id,
                Name: "Structured task",
                Kind: TaskKind.OneShot,
                Harness: "codex",
                Prompt: "stream structured output",
                Command: "echo structured",
                AutoCreatePullRequest: false,
                CronExpression: string.Empty,
                Enabled: true),
            CancellationToken.None);

        var run = await fixture.Store.CreateRunAsync(task, CancellationToken.None);

        await fixture.Store.AppendRunStructuredEventAsync(
            new RunStructuredEventDocument
            {
                RunId = run.Id,
                RepositoryId = repository.Id,
                TaskId = task.Id,
                Sequence = 10,
                EventType = "tool.lifecycle",
                Category = "tool.lifecycle",
                PayloadJson = "{\"toolCallId\":\"call-1\",\"toolName\":\"bash\",\"status\":\"running\",\"input\":{\"cmd\":\"dotnet build\"}}",
                SchemaVersion = "harness-structured-event-v2",
                TimestampUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
            },
            CancellationToken.None);

        await fixture.Store.UpsertRunDiffSnapshotAsync(
            new RunDiffSnapshotDocument
            {
                RunId = run.Id,
                RepositoryId = repository.Id,
                TaskId = task.Id,
                Sequence = 20,
                Summary = "first diff",
                DiffStat = "1 file changed, 1 insertion(+)",
                DiffPatch = "diff --git a/file.txt b/file.txt\n--- a/file.txt\n+++ b/file.txt\n@@ -1 +1 @@\n-old\n+new",
                SchemaVersion = "harness-structured-event-v2",
                TimestampUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
            },
            CancellationToken.None);

        await fixture.Store.UpsertRunDiffSnapshotAsync(
            new RunDiffSnapshotDocument
            {
                RunId = run.Id,
                RepositoryId = repository.Id,
                TaskId = task.Id,
                Sequence = 25,
                Summary = "latest diff",
                DiffStat = "1 file changed, 2 insertions(+)",
                DiffPatch = "diff --git a/file.txt b/file.txt\n--- a/file.txt\n+++ b/file.txt\n@@ -1 +1,2 @@\n old\n+new",
                SchemaVersion = "harness-structured-event-v2",
                TimestampUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
            },
            CancellationToken.None);

        var events = await fixture.Store.ListRunStructuredEventsAsync(run.Id, 50, CancellationToken.None);
        events.Should().ContainSingle();
        events[0].Category.Should().Be("tool.lifecycle");

        var toolProjections = await fixture.Store.ListRunToolProjectionsAsync(run.Id, CancellationToken.None);
        toolProjections.Should().ContainSingle();
        toolProjections[0].ToolCallId.Should().Be("call-1");
        toolProjections[0].ToolName.Should().Be("bash");

        var latestDiff = await fixture.Store.GetLatestRunDiffSnapshotAsync(run.Id, CancellationToken.None);
        latestDiff.Should().NotBeNull();
        latestDiff!.Sequence.Should().Be(25);
        latestDiff.Summary.Should().Be("latest diff");
        latestDiff.DiffStat.Should().Be("1 file changed, 2 insertions(+)");
    }

    [Test]
    public async Task TaskModePersistence_WhenCreatingAndUpdatingTask_PersistsExecutionModeDefault()
    {
        await using var fixture = await OrchestratorStoreIntegrationFixture.CreateAsync();

        var repository = await fixture.Store.CreateRepositoryAsync(
            new CreateRepositoryRequest(
                Name: "Mode Repository",
                GitUrl: "https://example.com/org/mode.git",
                LocalPath: "/tmp/mode-repository",
                DefaultBranch: "main"),
            CancellationToken.None);

        var task = await fixture.Store.CreateTaskAsync(
            new CreateTaskRequest(
                RepositoryId: repository.Id,
                Name: "Mode task",
                Kind: TaskKind.OneShot,
                Harness: "codex",
                Prompt: "mode prompt",
                Command: "echo mode",
                AutoCreatePullRequest: false,
                CronExpression: string.Empty,
                Enabled: true,
                ExecutionModeDefault: HarnessExecutionMode.Plan),
            CancellationToken.None);

        task.ExecutionModeDefault.Should().Be(HarnessExecutionMode.Plan);

        var updatedTask = await fixture.Store.UpdateTaskAsync(
            task.Id,
            new UpdateTaskRequest(
                Name: task.Name,
                Kind: task.Kind,
                Harness: task.Harness,
                Prompt: task.Prompt,
                Command: task.Command,
                AutoCreatePullRequest: task.AutoCreatePullRequest,
                CronExpression: task.CronExpression,
                Enabled: task.Enabled,
                ExecutionModeDefault: HarnessExecutionMode.Review),
            CancellationToken.None);

        updatedTask.Should().NotBeNull();
        updatedTask!.ExecutionModeDefault.Should().Be(HarnessExecutionMode.Review);
    }

    [Test]
    public async Task CreateRunAsync_WhenExecutionModeOverrideProvided_PersistsEffectiveModeAndProtocol()
    {
        await using var fixture = await OrchestratorStoreIntegrationFixture.CreateAsync();

        var repository = await fixture.Store.CreateRepositoryAsync(
            new CreateRepositoryRequest(
                Name: "Run Mode Repository",
                GitUrl: "https://example.com/org/run-mode.git",
                LocalPath: "/tmp/run-mode-repository",
                DefaultBranch: "main"),
            CancellationToken.None);

        var task = await fixture.Store.CreateTaskAsync(
            new CreateTaskRequest(
                RepositoryId: repository.Id,
                Name: "Run mode task",
                Kind: TaskKind.OneShot,
                Harness: "codex",
                Prompt: "mode prompt",
                Command: "echo mode",
                AutoCreatePullRequest: false,
                CronExpression: string.Empty,
                Enabled: true,
                ExecutionModeDefault: HarnessExecutionMode.Plan),
            CancellationToken.None);

        var defaultRun = await fixture.Store.CreateRunAsync(task, CancellationToken.None);
        defaultRun.ExecutionMode.Should().Be(HarnessExecutionMode.Plan);
        defaultRun.StructuredProtocol.Should().Be("harness-structured-event-v2");

        var overrideRun = await fixture.Store.CreateRunAsync(
            task,
            CancellationToken.None,
            executionModeOverride: HarnessExecutionMode.Review);
        overrideRun.ExecutionMode.Should().Be(HarnessExecutionMode.Review);
        overrideRun.StructuredProtocol.Should().Be("harness-structured-event-v2");
    }

    [Test]
    public async Task PruneStructuredRunDataAsync_WhenPruningTerminalRuns_DeletesOnlyEligibleStructuredRows()
    {
        await using var fixture = await OrchestratorStoreIntegrationFixture.CreateAsync();

        var repository = await fixture.Store.CreateRepositoryAsync(
            new CreateRepositoryRequest(
                Name: "Prune Repository",
                GitUrl: "https://example.com/org/prune.git",
                LocalPath: "/tmp/prune-repository",
                DefaultBranch: "main"),
            CancellationToken.None);

        var task = await fixture.Store.CreateTaskAsync(
            new CreateTaskRequest(
                RepositoryId: repository.Id,
                Name: "Prune task",
                Kind: TaskKind.OneShot,
                Harness: "codex",
                Prompt: "prune structured rows",
                Command: "echo prune",
                AutoCreatePullRequest: false,
                CronExpression: string.Empty,
                Enabled: true),
            CancellationToken.None);

        var terminalRun = await fixture.Store.CreateRunAsync(task, CancellationToken.None);
        await fixture.Store.MarkRunStartedAsync(terminalRun.Id, "worker-terminal", CancellationToken.None);
        await fixture.Store.MarkRunCompletedAsync(terminalRun.Id, succeeded: true, summary: "done", outputJson: "{}", CancellationToken.None);

        var activeRun = await fixture.Store.CreateRunAsync(task, CancellationToken.None);
        await fixture.Store.MarkRunStartedAsync(activeRun.Id, "worker-active", CancellationToken.None);

        await fixture.Store.AppendRunStructuredEventAsync(
            new RunStructuredEventDocument
            {
                RunId = terminalRun.Id,
                RepositoryId = repository.Id,
                TaskId = task.Id,
                Sequence = 1,
                EventType = "tool.lifecycle",
                Category = "tool.lifecycle",
                PayloadJson = "{\"toolCallId\":\"terminal-call\",\"toolName\":\"bash\",\"state\":\"completed\"}",
                SchemaVersion = "harness-structured-event-v2",
                TimestampUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
            },
            CancellationToken.None);
        await fixture.Store.UpsertRunDiffSnapshotAsync(
            new RunDiffSnapshotDocument
            {
                RunId = terminalRun.Id,
                RepositoryId = repository.Id,
                TaskId = task.Id,
                Sequence = 1,
                Summary = "terminal diff",
                DiffStat = "1 file changed",
                DiffPatch = "diff --git a/a.txt b/a.txt\n--- a/a.txt\n+++ b/a.txt\n@@ -1 +1 @@\n-old\n+new",
                SchemaVersion = "harness-structured-event-v2",
                TimestampUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
            },
            CancellationToken.None);

        await fixture.Store.AppendRunStructuredEventAsync(
            new RunStructuredEventDocument
            {
                RunId = activeRun.Id,
                RepositoryId = repository.Id,
                TaskId = task.Id,
                Sequence = 1,
                EventType = "tool.lifecycle",
                Category = "tool.lifecycle",
                PayloadJson = "{\"toolCallId\":\"active-call\",\"toolName\":\"bash\",\"state\":\"running\"}",
                SchemaVersion = "harness-structured-event-v2",
                TimestampUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
            },
            CancellationToken.None);
        await fixture.Store.UpsertRunDiffSnapshotAsync(
            new RunDiffSnapshotDocument
            {
                RunId = activeRun.Id,
                RepositoryId = repository.Id,
                TaskId = task.Id,
                Sequence = 1,
                Summary = "active diff",
                DiffStat = "1 file changed",
                DiffPatch = "diff --git a/b.txt b/b.txt\n--- a/b.txt\n+++ b/b.txt\n@@ -1 +1 @@\n-old\n+new",
                SchemaVersion = "harness-structured-event-v2",
                TimestampUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
            },
            CancellationToken.None);

        var prune = await fixture.Store.PruneStructuredRunDataAsync(
            DateTime.UtcNow.AddDays(1),
            maxRuns: 100,
            excludeWorkflowReferencedTasks: false,
            excludeTasksWithOpenFindings: false,
            CancellationToken.None);

        prune.RunsScanned.Should().Be(1);
        prune.DeletedStructuredEvents.Should().BeGreaterThan(0);
        prune.DeletedDiffSnapshots.Should().BeGreaterThan(0);
        prune.DeletedToolProjections.Should().BeGreaterThan(0);

        var terminalEvents = await fixture.Store.ListRunStructuredEventsAsync(terminalRun.Id, 50, CancellationToken.None);
        var terminalDiff = await fixture.Store.GetLatestRunDiffSnapshotAsync(terminalRun.Id, CancellationToken.None);
        var terminalTools = await fixture.Store.ListRunToolProjectionsAsync(terminalRun.Id, CancellationToken.None);

        terminalEvents.Should().BeEmpty();
        terminalDiff.Should().BeNull();
        terminalTools.Should().BeEmpty();

        var activeEvents = await fixture.Store.ListRunStructuredEventsAsync(activeRun.Id, 50, CancellationToken.None);
        var activeDiff = await fixture.Store.GetLatestRunDiffSnapshotAsync(activeRun.Id, CancellationToken.None);
        var activeTools = await fixture.Store.ListRunToolProjectionsAsync(activeRun.Id, CancellationToken.None);

        activeEvents.Should().ContainSingle();
        activeDiff.Should().NotBeNull();
        activeTools.Should().ContainSingle();
    }

    [Test]
    public async Task PruneStructuredRunDataAsync_WhenTaskIsWorkflowReferencedAndExcluded_PreservesStructuredRows()
    {
        await using var fixture = await OrchestratorStoreIntegrationFixture.CreateAsync();

        var repository = await fixture.Store.CreateRepositoryAsync(
            new CreateRepositoryRequest(
                Name: "Prune Exclusion Repository",
                GitUrl: "https://example.com/org/prune-exclusion.git",
                LocalPath: "/tmp/prune-exclusion-repository",
                DefaultBranch: "main"),
            CancellationToken.None);

        var task = await fixture.Store.CreateTaskAsync(
            new CreateTaskRequest(
                RepositoryId: repository.Id,
                Name: "Referenced task",
                Kind: TaskKind.OneShot,
                Harness: "codex",
                Prompt: "prune exclusion",
                Command: "echo prune",
                AutoCreatePullRequest: false,
                CronExpression: string.Empty,
                Enabled: true),
            CancellationToken.None);

        await fixture.Store.CreateWorkflowAsync(
            new WorkflowDocument
            {
                RepositoryId = repository.Id,
                Name = "Workflow referencing task",
                Description = "exclude referenced task",
                Enabled = true,
                Stages =
                [
                    new WorkflowStageConfig
                    {
                        Name = "Referenced stage",
                        Type = WorkflowStageType.Task,
                        TaskId = task.Id,
                        Order = 0,
                    }
                ],
            },
            CancellationToken.None);

        var run = await fixture.Store.CreateRunAsync(task, CancellationToken.None);
        await fixture.Store.MarkRunStartedAsync(run.Id, "worker-1", CancellationToken.None);
        await fixture.Store.MarkRunCompletedAsync(run.Id, succeeded: true, summary: "done", outputJson: "{}", CancellationToken.None);

        await fixture.Store.AppendRunStructuredEventAsync(
            new RunStructuredEventDocument
            {
                RunId = run.Id,
                RepositoryId = repository.Id,
                TaskId = task.Id,
                Sequence = 1,
                EventType = "tool.lifecycle",
                Category = "tool.lifecycle",
                PayloadJson = "{\"toolCallId\":\"call-1\",\"toolName\":\"bash\",\"state\":\"completed\"}",
                SchemaVersion = "harness-structured-event-v2",
                TimestampUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
            },
            CancellationToken.None);
        await fixture.Store.UpsertRunDiffSnapshotAsync(
            new RunDiffSnapshotDocument
            {
                RunId = run.Id,
                RepositoryId = repository.Id,
                TaskId = task.Id,
                Sequence = 1,
                Summary = "diff",
                DiffStat = "1 file changed",
                DiffPatch = "diff --git a/a.txt b/a.txt\n--- a/a.txt\n+++ b/a.txt\n@@ -1 +1 @@\n-old\n+new",
                SchemaVersion = "harness-structured-event-v2",
                TimestampUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
            },
            CancellationToken.None);

        var prune = await fixture.Store.PruneStructuredRunDataAsync(
            DateTime.UtcNow.AddDays(1),
            maxRuns: 100,
            excludeWorkflowReferencedTasks: true,
            excludeTasksWithOpenFindings: false,
            CancellationToken.None);

        prune.RunsScanned.Should().Be(0);
        prune.DeletedStructuredEvents.Should().Be(0);
        prune.DeletedDiffSnapshots.Should().Be(0);
        prune.DeletedToolProjections.Should().Be(0);

        var events = await fixture.Store.ListRunStructuredEventsAsync(run.Id, 50, CancellationToken.None);
        var diff = await fixture.Store.GetLatestRunDiffSnapshotAsync(run.Id, CancellationToken.None);
        var tools = await fixture.Store.ListRunToolProjectionsAsync(run.Id, CancellationToken.None);

        events.Should().ContainSingle();
        diff.Should().NotBeNull();
        tools.Should().ContainSingle();
    }
}
