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
                Kind: TaskKind.EventDriven,
                Harness: "OpenCode",
                ExecutionModeDefault: HarnessExecutionMode.Plan,
                Command: "echo defaults",
                AutoCreatePullRequest: true,
                Enabled: false,
                SessionProfileId: "profile-1"),
            CancellationToken.None);

        await Assert.That(updatedRepository).IsNotNull();
        if (updatedRepository is null)
        {
            return;
        }

        await Assert.That(updatedRepository.TaskDefaults.Kind).IsEqualTo(TaskKind.EventDriven);
        await Assert.That(updatedRepository.TaskDefaults.Harness).IsEqualTo("opencode");
        await Assert.That(updatedRepository.TaskDefaults.ExecutionModeDefault).IsEqualTo(HarnessExecutionMode.Plan);
        await Assert.That(updatedRepository.TaskDefaults.Command).IsEqualTo("echo defaults");
        await Assert.That(updatedRepository.TaskDefaults.AutoCreatePullRequest).IsTrue();
        await Assert.That(updatedRepository.TaskDefaults.Enabled).IsFalse();
        await Assert.That(updatedRepository.TaskDefaults.SessionProfileId).IsEqualTo("profile-1");

        var task = await fixture.Store.CreateTaskAsync(
            new CreateTaskRequest(
                RepositoryId: repository.Id,
                Prompt: "run defaults",
                Name: "defaults task"),
            CancellationToken.None);

        await Assert.That(task.Kind).IsEqualTo(TaskKind.EventDriven);
        await Assert.That(task.Harness).IsEqualTo("opencode");
        await Assert.That(task.ExecutionModeDefault).IsEqualTo(HarnessExecutionMode.Plan);
        await Assert.That(task.Command).IsEqualTo("echo defaults");
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
                Kind: TaskKind.EventDriven,
                Harness: "codex",
                ExecutionModeDefault: HarnessExecutionMode.Default,
                Command: "echo event",
                AutoCreatePullRequest: false,
                Enabled: true),
            CancellationToken.None);

        var eventTask = await fixture.Store.CreateTaskAsync(
            new CreateTaskRequest(
                RepositoryId: repository.Id,
                Prompt: "Handle webhook event",
                Name: "Event task"),
            CancellationToken.None);

        await fixture.Store.UpdateRepositoryTaskDefaultsAsync(
            repository.Id,
            new UpdateRepositoryTaskDefaultsRequest(
                Kind: TaskKind.OneShot,
                Harness: "codex",
                ExecutionModeDefault: HarnessExecutionMode.Default,
                Command: "echo disabled",
                AutoCreatePullRequest: false,
                Enabled: false),
            CancellationToken.None);

        var disabledTask = await fixture.Store.CreateTaskAsync(
            new CreateTaskRequest(
                RepositoryId: repository.Id,
                Prompt: "Disabled",
                Name: "Disabled task"),
            CancellationToken.None);

        var dueTaskIds = (await fixture.Store.ListDueTasksAsync(DateTime.UtcNow, limit: 10, CancellationToken.None))
            .Select(task => task.Id)
            .ToList();

        await Assert.That(dueTaskIds).Contains(oneShotTask.Id);
        await Assert.That(dueTaskIds).DoesNotContain(eventTask.Id);
        await Assert.That(dueTaskIds).DoesNotContain(disabledTask.Id);

        await fixture.Store.MarkOneShotTaskConsumedAsync(oneShotTask.Id, CancellationToken.None);

        var dueTaskIdsAfterConsume = (await fixture.Store.ListDueTasksAsync(DateTime.UtcNow, limit: 10, CancellationToken.None))
            .Select(task => task.Id)
            .ToList();

        await Assert.That(dueTaskIdsAfterConsume).DoesNotContain(oneShotTask.Id);

        var firstRun = await fixture.Store.CreateRunAsync(eventTask, CancellationToken.None);
        await fixture.Store.MarkRunStartedAsync(firstRun.Id, "worker-1", CancellationToken.None);
        await fixture.Store.MarkRunCompletedAsync(firstRun.Id, succeeded: false, summary: "failed", outputJson: "{}", CancellationToken.None);

        var secondRun = await fixture.Store.CreateRunAsync(eventTask, CancellationToken.None);
        await fixture.Store.MarkRunStartedAsync(secondRun.Id, "worker-2", CancellationToken.None);

        var latestStates = await fixture.Store.GetLatestRunStatesByTaskIdsAsync([eventTask.Id, oneShotTask.Id], CancellationToken.None);

        await Assert.That(latestStates.ContainsKey(eventTask.Id)).IsTrue();
        await Assert.That(latestStates[eventTask.Id]).IsEqualTo(RunState.Running);
        await Assert.That(latestStates.ContainsKey(oneShotTask.Id)).IsFalse();

        var activeRunCount = await fixture.Store.CountActiveRunsByTaskAsync(eventTask.Id, CancellationToken.None);
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
    public async Task RunQuestionRequests_WhenPlanModeToolPayloadArrives_PersistsPendingAndMarksAnswered()
    {
        await using var fixture = await OrchestratorStoreIntegrationFixture.CreateAsync();

        var repository = await fixture.Store.CreateRepositoryAsync(
            new CreateRepositoryRequest(
                Name: "Question Repository",
                GitUrl: "https://example.com/org/questions.git",
                LocalPath: "/tmp/question-repository",
                DefaultBranch: "main"),
            CancellationToken.None);

        var task = await fixture.Store.CreateTaskAsync(
            new CreateTaskRequest(
                RepositoryId: repository.Id,
                Prompt: "answer follow-up questions",
                Name: "Question task"),
            CancellationToken.None);

        var run = await fixture.Store.CreateRunAsync(
            task,
            CancellationToken.None,
            executionModeOverride: HarnessExecutionMode.Plan);

        await fixture.Store.AppendRunStructuredEventAsync(
            new RunStructuredEventDocument
            {
                RunId = run.Id,
                RepositoryId = repository.Id,
                TaskId = task.Id,
                Sequence = 12,
                EventType = "tool.lifecycle",
                Category = "tool.lifecycle",
                PayloadJson = """
                              {
                                "toolName": "request_user_input",
                                "toolCallId": "call-plan-1",
                                "state": "running",
                                "input": {
                                  "questions": [
                                    {
                                      "id": "mode",
                                      "header": "Mode",
                                      "question": "Select execution mode",
                                      "options": [
                                        { "value": "fast", "label": "Fast", "description": "Lower checks" },
                                        { "value": "safe", "label": "Safe", "description": "Run full validation" }
                                      ]
                                    }
                                  ]
                                }
                              }
                              """,
                SchemaVersion = "harness-structured-event-v2",
                TimestampUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
            },
            CancellationToken.None);

        var pending = await fixture.Store.ListPendingRunQuestionRequestsAsync(task.Id, run.Id, CancellationToken.None);
        await Assert.That(pending.Count()).IsEqualTo(1);
        await Assert.That(pending[0].Status).IsEqualTo(RunQuestionRequestStatus.Pending);
        await Assert.That(pending[0].SourceToolName).IsEqualTo("request_user_input");
        await Assert.That(pending[0].Questions.Count).IsEqualTo(1);
        await Assert.That(pending[0].Questions[0].Options.Count).IsEqualTo(2);
        await Assert.That(pending[0].Questions[0].Options[0].Value).IsEqualTo("fast");

        var answered = await fixture.Store.MarkRunQuestionRequestAnsweredAsync(
            pending[0].Id,
            [
                new RunQuestionAnswerDocument
                {
                    QuestionId = "mode",
                    SelectedOptionValue = "safe",
                    SelectedOptionLabel = "Safe",
                    SelectedOptionDescription = "Run full validation",
                    AdditionalContext = "Prefer reliability over speed.",
                }
            ],
            answeredRunId: "follow-up-run-1",
            CancellationToken.None);

        await Assert.That(answered).IsNotNull();
        if (answered is null)
        {
            return;
        }

        await Assert.That(answered.Status).IsEqualTo(RunQuestionRequestStatus.Answered);
        await Assert.That(answered.AnsweredRunId).IsEqualTo("follow-up-run-1");
        await Assert.That(answered.Answers.Count).IsEqualTo(1);

        var pendingAfterAnswer = await fixture.Store.ListPendingRunQuestionRequestsAsync(task.Id, run.Id, CancellationToken.None);
        await Assert.That(pendingAfterAnswer).IsEmpty();
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

}
