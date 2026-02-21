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

        await Assert.That(repositorySkill.RepositoryId).IsEqualTo(repositoryId);
        await Assert.That(globalSkill.RepositoryId).IsEqualTo("global");
        await Assert.That(globalSkill.Trigger).IsEqualTo("global-skill");

        var withGlobal = await fixture.Store.ListPromptSkillsAsync(repositoryId, includeGlobal: true, CancellationToken.None);

        await Assert.That(withGlobal.Select(skill => skill.RepositoryId).SequenceEqual(new[] { repositoryId, "global" })).IsTrue();
        await Assert.That(withGlobal.Select(skill => skill.Trigger).SequenceEqual(new[] { "repo-skill", "global-skill" })).IsTrue();

        var repositoryOnly = await fixture.Store.ListPromptSkillsAsync(repositoryId, includeGlobal: false, CancellationToken.None);

        await Assert.That(repositoryOnly.Count()).IsEqualTo(1);
        await Assert.That(repositoryOnly[0].Trigger).IsEqualTo("repo-skill");
    }

    [Test]
    public async Task RepositoryTaskDefaults_WhenUpdated_AppliedToNewTasks()
    {
        await using var fixture = await OrchestratorStoreIntegrationFixture.CreateAsync();

        var repository = await fixture.Store.CreateRepositoryAsync(
            new CreateRepositoryRequest(
                Name: "Defaults Repository",
                GitUrl: "https://example.com/org/defaults.git",
                LocalPath: "/tmp/defaults-repository",
                DefaultBranch: "main"),
            CancellationToken.None);

        var updatedRepository = await fixture.Store.UpdateRepositoryTaskDefaultsAsync(
            repository.Id,
            new UpdateRepositoryTaskDefaultsRequest(
                Kind: TaskKind.Cron,
                Harness: "OpenCode",
                ExecutionModeDefault: HarnessExecutionMode.Plan,
                Command: "echo defaults",
                CronExpression: "*/5 * * * *",
                AutoCreatePullRequest: true,
                Enabled: false,
                SessionProfileId: "profile-1"),
            CancellationToken.None);

        await Assert.That(updatedRepository).IsNotNull();
        if (updatedRepository is null)
        {
            return;
        }

        await Assert.That(updatedRepository.TaskDefaults.Kind).IsEqualTo(TaskKind.Cron);
        await Assert.That(updatedRepository.TaskDefaults.Harness).IsEqualTo("opencode");
        await Assert.That(updatedRepository.TaskDefaults.ExecutionModeDefault).IsEqualTo(HarnessExecutionMode.Plan);
        await Assert.That(updatedRepository.TaskDefaults.Command).IsEqualTo("echo defaults");
        await Assert.That(updatedRepository.TaskDefaults.CronExpression).IsEqualTo("*/5 * * * *");
        await Assert.That(updatedRepository.TaskDefaults.AutoCreatePullRequest).IsTrue();
        await Assert.That(updatedRepository.TaskDefaults.Enabled).IsFalse();
        await Assert.That(updatedRepository.TaskDefaults.SessionProfileId).IsEqualTo("profile-1");

        var task = await fixture.Store.CreateTaskAsync(
            new CreateTaskRequest(
                RepositoryId: repository.Id,
                Prompt: "run defaults",
                Name: "defaults task"),
            CancellationToken.None);

        await Assert.That(task.Kind).IsEqualTo(TaskKind.Cron);
        await Assert.That(task.Harness).IsEqualTo("opencode");
        await Assert.That(task.ExecutionModeDefault).IsEqualTo(HarnessExecutionMode.Plan);
        await Assert.That(task.Command).IsEqualTo("echo defaults");
        await Assert.That(task.CronExpression).IsEqualTo("*/5 * * * *");
        await Assert.That(task.AutoCreatePullRequest).IsTrue();
        await Assert.That(task.Enabled).IsFalse();
        await Assert.That(task.SessionProfileId).IsEqualTo("profile-1");
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

        await fixture.Store.UpdateRepositoryTaskDefaultsAsync(
            repository.Id,
            new UpdateRepositoryTaskDefaultsRequest(
                Kind: TaskKind.OneShot,
                Harness: "codex",
                ExecutionModeDefault: HarnessExecutionMode.Default,
                Command: "echo one-shot",
                CronExpression: string.Empty,
                AutoCreatePullRequest: false,
                Enabled: true),
            CancellationToken.None);

        var oneShotTask = await fixture.Store.CreateTaskAsync(
            new CreateTaskRequest(
                RepositoryId: repository.Id,
                Prompt: "Do one thing",
                Name: "One-shot task"),
            CancellationToken.None);

        await fixture.Store.UpdateRepositoryTaskDefaultsAsync(
            repository.Id,
            new UpdateRepositoryTaskDefaultsRequest(
                Kind: TaskKind.Cron,
                Harness: "codex",
                ExecutionModeDefault: HarnessExecutionMode.Default,
                Command: "echo cron",
                CronExpression: "* * * * *",
                AutoCreatePullRequest: false,
                Enabled: true),
            CancellationToken.None);

        var cronTask = await fixture.Store.CreateTaskAsync(
            new CreateTaskRequest(
                RepositoryId: repository.Id,
                Prompt: "Do recurring thing",
                Name: "Cron task"),
            CancellationToken.None);

        await fixture.Store.UpdateRepositoryTaskDefaultsAsync(
            repository.Id,
            new UpdateRepositoryTaskDefaultsRequest(
                Kind: TaskKind.OneShot,
                Harness: "codex",
                ExecutionModeDefault: HarnessExecutionMode.Default,
                Command: "echo disabled",
                CronExpression: string.Empty,
                AutoCreatePullRequest: false,
                Enabled: false),
            CancellationToken.None);

        var disabledTask = await fixture.Store.CreateTaskAsync(
            new CreateTaskRequest(
                RepositoryId: repository.Id,
                Prompt: "Disabled",
                Name: "Disabled task"),
            CancellationToken.None);

        await fixture.Store.UpdateTaskNextRunAsync(cronTask.Id, DateTime.UtcNow.AddMinutes(-1), CancellationToken.None);

        var dueTaskIds = (await fixture.Store.ListDueTasksAsync(DateTime.UtcNow, limit: 10, CancellationToken.None))
            .Select(task => task.Id)
            .ToList();

        await Assert.That(dueTaskIds).Contains(oneShotTask.Id);
        await Assert.That(dueTaskIds).Contains(cronTask.Id);
        await Assert.That(dueTaskIds).DoesNotContain(disabledTask.Id);

        await fixture.Store.MarkOneShotTaskConsumedAsync(oneShotTask.Id, CancellationToken.None);

        var dueTaskIdsAfterConsume = (await fixture.Store.ListDueTasksAsync(DateTime.UtcNow, limit: 10, CancellationToken.None))
            .Select(task => task.Id)
            .ToList();

        await Assert.That(dueTaskIdsAfterConsume).DoesNotContain(oneShotTask.Id);

        var firstRun = await fixture.Store.CreateRunAsync(cronTask, CancellationToken.None);
        await fixture.Store.MarkRunStartedAsync(firstRun.Id, "worker-1", CancellationToken.None);
        await fixture.Store.MarkRunCompletedAsync(firstRun.Id, succeeded: false, summary: "failed", outputJson: "{}", CancellationToken.None);

        var secondRun = await fixture.Store.CreateRunAsync(cronTask, CancellationToken.None);
        await fixture.Store.MarkRunStartedAsync(secondRun.Id, "worker-2", CancellationToken.None);

        var latestStates = await fixture.Store.GetLatestRunStatesByTaskIdsAsync([cronTask.Id, oneShotTask.Id], CancellationToken.None);

        await Assert.That(latestStates.ContainsKey(cronTask.Id)).IsTrue();
        await Assert.That(latestStates[cronTask.Id]).IsEqualTo(RunState.Running);
        await Assert.That(latestStates.ContainsKey(oneShotTask.Id)).IsFalse();

        var activeRunCount = await fixture.Store.CountActiveRunsByTaskAsync(cronTask.Id, CancellationToken.None);
        await Assert.That(activeRunCount).IsEqualTo(1);
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
                Prompt: "stream structured output",
                Name: "Structured task"),
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
        await Assert.That(events.Count()).IsEqualTo(1);
        await Assert.That(events[0].Category).IsEqualTo("tool.lifecycle");

        var toolProjections = await fixture.Store.ListRunToolProjectionsAsync(run.Id, CancellationToken.None);
        await Assert.That(toolProjections.Count()).IsEqualTo(1);
        await Assert.That(toolProjections[0].ToolCallId).IsEqualTo("call-1");
        await Assert.That(toolProjections[0].ToolName).IsEqualTo("bash");

        var latestDiff = await fixture.Store.GetLatestRunDiffSnapshotAsync(run.Id, CancellationToken.None);
        await Assert.That(latestDiff).IsNotNull();
        await Assert.That(latestDiff!.Sequence).IsEqualTo(25);
        await Assert.That(latestDiff.Summary).IsEqualTo("latest diff");
        await Assert.That(latestDiff.DiffStat).IsEqualTo("1 file changed, 2 insertions(+)");
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

        await fixture.Store.UpdateRepositoryTaskDefaultsAsync(
            repository.Id,
            new UpdateRepositoryTaskDefaultsRequest(
                Kind: TaskKind.OneShot,
                Harness: "codex",
                ExecutionModeDefault: HarnessExecutionMode.Plan,
                Command: "echo mode",
                CronExpression: string.Empty,
                AutoCreatePullRequest: false,
                Enabled: true),
            CancellationToken.None);

        var task = await fixture.Store.CreateTaskAsync(
            new CreateTaskRequest(
                RepositoryId: repository.Id,
                Prompt: "mode prompt",
                Name: "Mode task"),
            CancellationToken.None);

        await Assert.That(task.ExecutionModeDefault).IsEqualTo(HarnessExecutionMode.Plan);

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

        await Assert.That(updatedTask).IsNotNull();
        await Assert.That(updatedTask!.ExecutionModeDefault).IsEqualTo(HarnessExecutionMode.Review);
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

        await fixture.Store.UpdateRepositoryTaskDefaultsAsync(
            repository.Id,
            new UpdateRepositoryTaskDefaultsRequest(
                Kind: TaskKind.OneShot,
                Harness: "codex",
                ExecutionModeDefault: HarnessExecutionMode.Plan,
                Command: "echo mode",
                CronExpression: string.Empty,
                AutoCreatePullRequest: false,
                Enabled: true),
            CancellationToken.None);

        var task = await fixture.Store.CreateTaskAsync(
            new CreateTaskRequest(
                RepositoryId: repository.Id,
                Prompt: "mode prompt",
                Name: "Run mode task"),
            CancellationToken.None);

        var defaultRun = await fixture.Store.CreateRunAsync(task, CancellationToken.None);
        await Assert.That(defaultRun.ExecutionMode).IsEqualTo(HarnessExecutionMode.Plan);
        await Assert.That(defaultRun.StructuredProtocol).IsEqualTo("harness-structured-event-v2");

        var overrideRun = await fixture.Store.CreateRunAsync(
            task,
            CancellationToken.None,
            executionModeOverride: HarnessExecutionMode.Review);
        await Assert.That(overrideRun.ExecutionMode).IsEqualTo(HarnessExecutionMode.Review);
        await Assert.That(overrideRun.StructuredProtocol).IsEqualTo("harness-structured-event-v2");
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
                Prompt: "prune structured rows",
                Name: "Prune task"),
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

        await Assert.That(prune.RunsScanned).IsEqualTo(1);
        await Assert.That(prune.DeletedStructuredEvents).IsGreaterThan(0);
        await Assert.That(prune.DeletedDiffSnapshots).IsGreaterThan(0);
        await Assert.That(prune.DeletedToolProjections).IsGreaterThan(0);

        var terminalEvents = await fixture.Store.ListRunStructuredEventsAsync(terminalRun.Id, 50, CancellationToken.None);
        var terminalDiff = await fixture.Store.GetLatestRunDiffSnapshotAsync(terminalRun.Id, CancellationToken.None);
        var terminalTools = await fixture.Store.ListRunToolProjectionsAsync(terminalRun.Id, CancellationToken.None);

        await Assert.That(terminalEvents).IsEmpty();
        await Assert.That(terminalDiff).IsNull();
        await Assert.That(terminalTools).IsEmpty();

        var activeEvents = await fixture.Store.ListRunStructuredEventsAsync(activeRun.Id, 50, CancellationToken.None);
        var activeDiff = await fixture.Store.GetLatestRunDiffSnapshotAsync(activeRun.Id, CancellationToken.None);
        var activeTools = await fixture.Store.ListRunToolProjectionsAsync(activeRun.Id, CancellationToken.None);

        await Assert.That(activeEvents.Count()).IsEqualTo(1);
        await Assert.That(activeDiff).IsNotNull();
        await Assert.That(activeTools.Count()).IsEqualTo(1);
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
                Prompt: "prune exclusion",
                Name: "Referenced task"),
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

        await Assert.That(prune.RunsScanned).IsEqualTo(0);
        await Assert.That(prune.DeletedStructuredEvents).IsEqualTo(0);
        await Assert.That(prune.DeletedDiffSnapshots).IsEqualTo(0);
        await Assert.That(prune.DeletedToolProjections).IsEqualTo(0);

        var events = await fixture.Store.ListRunStructuredEventsAsync(run.Id, 50, CancellationToken.None);
        var diff = await fixture.Store.GetLatestRunDiffSnapshotAsync(run.Id, CancellationToken.None);
        var tools = await fixture.Store.ListRunToolProjectionsAsync(run.Id, CancellationToken.None);

        await Assert.That(events.Count()).IsEqualTo(1);
        await Assert.That(diff).IsNotNull();
        await Assert.That(tools.Count()).IsEqualTo(1);
    }
}
