using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.TaskRuntime;
using AgentsDashboard.ControlPlane.Data;

namespace AgentsDashboard.ControlPlane.Services;

public interface IWorkspaceService
{
    Task<WorkspacePageData?> GetWorkspacePageDataAsync(
        string repositoryId,
        string? selectedRunId,
        CancellationToken cancellationToken);

    Task<WorkspacePromptSubmissionResult> SubmitPromptAsync(
        string repositoryId,
        WorkspacePromptSubmissionRequest request,
        CancellationToken cancellationToken);

    void NotifyRunEvent(string runId, string eventType, DateTime eventTimestampUtc);

    Task<WorkspaceSummaryRefreshResult> RefreshRunSummaryAsync(
        string repositoryId,
        string runId,
        string eventType,
        bool force,
        CancellationToken cancellationToken);
}

public sealed record WorkspacePageData(
    RepositoryDocument Repository,
    IReadOnlyList<TaskDocument> Tasks,
    IReadOnlyList<RunDocument> Runs,
    RunDocument? LatestActiveRun,
    RunDocument? LatestCompletedRun,
    RunDocument? SelectedRun,
    IReadOnlyList<RunLogEvent> SelectedRunLogs,
    ParsedHarnessOutput? ParsedSelectedRunOutput,
    WorkspaceSummaryRefreshStateView SummaryRefreshState);

public sealed record WorkspaceSummaryRefreshStateView(
    bool ShouldRefresh,
    string Reason,
    DateTime? LastRefreshAtUtc,
    DateTime? NextAllowedRefreshAtUtc,
    bool HasNewEvents);

public sealed record WorkspacePromptSubmissionRequest(
    string Prompt,
    string? TaskId = null,
    string? Harness = null,
    string? Command = null,
    bool ForceNewRun = false,
    string? UserMessage = null,
    HarnessExecutionMode? ModeOverride = null,
    WorkspaceAgentTeamRequest? AgentTeam = null,
    IReadOnlyList<WorkspaceImageInput>? Images = null,
    bool PreferNativeMultimodal = true,
    string? SessionProfileId = null);

public sealed record WorkspaceAgentTeamRequest(
    IReadOnlyList<WorkspaceAgentTeamMemberRequest> Members,
    WorkspaceAgentTeamSynthesisRequest? Synthesis = null);

public sealed record WorkspaceAgentTeamMemberRequest(
    string Harness,
    HarnessExecutionMode Mode,
    string RolePrompt,
    string? ModelOverride = null,
    int? TimeoutSeconds = null);

public sealed record WorkspaceAgentTeamSynthesisRequest(
    bool Enabled,
    string Prompt,
    string? Harness = null,
    HarnessExecutionMode? Mode = null,
    string? ModelOverride = null,
    int? TimeoutSeconds = null);

public sealed record WorkspacePromptSubmissionResult(
    bool Success,
    bool CreatedRun,
    bool DispatchAccepted,
    string Message,
    TaskDocument? Task,
    RunDocument? Run,
    WorkflowExecutionDocument? WorkflowExecution = null,
    int TeamMemberCount = 0,
    bool TeamSynthesisEnabled = false);

public sealed record WorkspaceSummaryRefreshResult(
    bool Success,
    bool Refreshed,
    string Summary,
    bool UsedFallback,
    bool KeyConfigured,
    string Message,
    DateTime? LastRefreshAtUtc,
    DateTime? NextAllowedRefreshAtUtc);

public sealed class WorkspaceService(
    IOrchestratorStore store,
    RunDispatcher dispatcher,
    IWorkflowExecutor workflowExecutor,
    IHarnessOutputParserService parserService,
    IWorkspaceAiService workspaceAiService,
    IWorkspaceImageStorageService workspaceImageStorageService,
    ITaskSemanticEmbeddingService taskSemanticEmbeddingService,
    ILogger<WorkspaceService> logger) : IWorkspaceService
{
    private static readonly HashSet<RunState> s_activeStates =
    [
        RunState.Queued,
        RunState.Running,
        RunState.PendingApproval,
    ];

    private static readonly HashSet<RunState> s_completedStates =
    [
        RunState.Succeeded,
        RunState.Failed,
        RunState.Cancelled,
    ];

    private static readonly TimeSpan s_summaryRefreshCooldown = TimeSpan.FromSeconds(45);

    private readonly ConcurrentDictionary<string, SummaryRefreshState> _summaryRefreshStates =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<WorkspacePageData?> GetWorkspacePageDataAsync(
        string repositoryId,
        string? selectedRunId,
        CancellationToken cancellationToken)
    {
        var repositoryTask = store.GetRepositoryAsync(repositoryId, cancellationToken);
        var tasksTask = store.ListTasksAsync(repositoryId, cancellationToken);
        var runsTask = store.ListRunsByRepositoryAsync(repositoryId, cancellationToken);
        await Task.WhenAll(repositoryTask, tasksTask, runsTask);

        var repository = repositoryTask.Result;
        if (repository is null)
        {
            return null;
        }

        var tasks = tasksTask.Result;
        var runs = runsTask.Result;

        var latestActiveRun = SelectLatestActiveRun(runs);
        var latestCompletedRun = SelectLatestCompletedRun(runs);

        var selectedRun = SelectRun(runs, selectedRunId) ?? latestActiveRun ?? latestCompletedRun;

        IReadOnlyList<RunLogEvent> selectedRunLogs = [];
        ParsedHarnessOutput? parsedOutput = null;
        var refreshStateView = new WorkspaceSummaryRefreshStateView(
            ShouldRefresh: false,
            Reason: "No run selected.",
            LastRefreshAtUtc: null,
            NextAllowedRefreshAtUtc: null,
            HasNewEvents: false);

        if (selectedRun is not null)
        {
            selectedRunLogs = await store.ListRunLogsAsync(selectedRun.Id, cancellationToken);
            parsedOutput = parserService.Parse(selectedRun.OutputJson, selectedRunLogs);

            var signature = ComputeSummarySignature(selectedRun, selectedRunLogs, parsedOutput);
            TrackObservedRunSnapshot(selectedRun.Id, signature, DateTime.UtcNow);
            refreshStateView = EvaluateSummaryRefresh(selectedRun.Id, selectedRun.State, signature, force: false);
        }

        return new WorkspacePageData(
            repository,
            tasks,
            runs,
            latestActiveRun,
            latestCompletedRun,
            selectedRun,
            selectedRunLogs,
            parsedOutput,
            refreshStateView);
    }

    public async Task<WorkspacePromptSubmissionResult> SubmitPromptAsync(
        string repositoryId,
        WorkspacePromptSubmissionRequest request,
        CancellationToken cancellationToken)
    {
        var repository = await store.GetRepositoryAsync(repositoryId, cancellationToken);
        if (repository is null)
        {
            return new WorkspacePromptSubmissionResult(
                Success: false,
                CreatedRun: false,
                DispatchAccepted: false,
                Message: "Repository not found.",
                Task: null,
                Run: null);
        }

        logger.ZLogDebug("Submitting workspace prompt for repository {RepositoryId}", repositoryId);

        var tasks = await store.ListTasksAsync(repositoryId, cancellationToken);
        var task = await ResolveTaskAsync(tasks, repositoryId, request, cancellationToken);

        if (task is null)
        {
            return new WorkspacePromptSubmissionResult(
                Success: false,
                CreatedRun: false,
                DispatchAccepted: false,
                Message: "No task available. Provide taskId or command/harness to create one.",
                Task: null,
                Run: null);
        }

        var requestedSessionProfileId = string.IsNullOrWhiteSpace(request.SessionProfileId)
            ? task.SessionProfileId
            : request.SessionProfileId.Trim();
        RunSessionProfileDocument? sessionProfile = null;
        if (!string.IsNullOrWhiteSpace(requestedSessionProfileId))
        {
            sessionProfile = await store.GetRunSessionProfileAsync(requestedSessionProfileId, cancellationToken);
            if (sessionProfile is null ||
                !sessionProfile.Enabled ||
                !(string.Equals(sessionProfile.RepositoryId, repositoryId, StringComparison.OrdinalIgnoreCase) ||
                  string.Equals(sessionProfile.RepositoryId, "global", StringComparison.OrdinalIgnoreCase)))
            {
                return new WorkspacePromptSubmissionResult(
                    Success: false,
                    CreatedRun: false,
                    DispatchAccepted: false,
                    Message: "Session profile is missing, disabled, or not in scope for this repository.",
                    Task: task,
                    Run: null);
            }
        }

        if (request.AgentTeam is { Members.Count: > 0 })
        {
            return await SubmitAgentTeamRunAsync(repository, task, request, cancellationToken);
        }

        var requestedImages = request.Images?
            .Where(image => !string.IsNullOrWhiteSpace(image.DataUrl))
            .ToList() ?? [];

        var runs = await store.ListRunsByRepositoryAsync(repositoryId, cancellationToken);
        var activeRun = runs
            .Where(run => run.TaskId == task.Id && s_activeStates.Contains(run.State))
            .OrderByDescending(run => run.CreatedAtUtc)
            .FirstOrDefault();

        if (activeRun is not null &&
            !request.ForceNewRun &&
            request.ModeOverride is null &&
            requestedImages.Count == 0)
        {
            if (!string.IsNullOrWhiteSpace(request.UserMessage))
            {
                var promptEntry = await AppendUserPromptEntryAsync(
                    repositoryId,
                    task.Id,
                    activeRun.Id,
                    request.UserMessage,
                    [],
                    cancellationToken);
                taskSemanticEmbeddingService.QueueTaskEmbedding(
                    repositoryId,
                    task.Id,
                    "user-message",
                    runId: activeRun.Id,
                    promptEntryId: promptEntry?.Id);
            }

            return new WorkspacePromptSubmissionResult(
                Success: true,
                CreatedRun: false,
                DispatchAccepted: true,
                Message: $"Active run {activeRun.Id} already exists for this task.",
                Task: task,
                Run: activeRun);
        }

        var dispatchTask = CloneTask(task);
        if (!string.IsNullOrWhiteSpace(request.Prompt))
        {
            dispatchTask.Prompt = request.Prompt.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Command))
        {
            dispatchTask.Command = request.Command.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Harness))
        {
            dispatchTask.Harness = request.Harness.Trim().ToLowerInvariant();
        }
        else if (!string.IsNullOrWhiteSpace(sessionProfile?.Harness))
        {
            dispatchTask.Harness = sessionProfile.Harness.Trim().ToLowerInvariant();
        }

        if (string.IsNullOrWhiteSpace(dispatchTask.Command))
        {
            return new WorkspacePromptSubmissionResult(
                Success: false,
                CreatedRun: false,
                DispatchAccepted: false,
                Message: "Task command is required to submit a prompt.",
                Task: task,
                Run: null);
        }

        if (string.IsNullOrWhiteSpace(dispatchTask.Prompt))
        {
            return new WorkspacePromptSubmissionResult(
                Success: false,
                CreatedRun: false,
                DispatchAccepted: false,
                Message: "Prompt cannot be empty.",
                Task: task,
                Run: null);
        }

        var effectiveModeOverride = request.ModeOverride ?? sessionProfile?.ExecutionModeDefault;
        var run = await store.CreateRunAsync(
            task,
            cancellationToken,
            executionModeOverride: effectiveModeOverride,
            sessionProfileId: sessionProfile?.Id);

        var storedImages = new List<WorkspaceStoredImage>();
        if (requestedImages.Count > 0)
        {
            var stored = await workspaceImageStorageService.StoreAsync(
                run.Id,
                repositoryId,
                task.Id,
                requestedImages,
                cancellationToken);
            if (!stored.Success)
            {
                var failedRun = await store.MarkRunCompletedAsync(
                    run.Id,
                    succeeded: false,
                    summary: "Workspace image validation failed",
                    outputJson: "{}",
                    cancellationToken,
                    failureClass: "InvalidInput");

                return new WorkspacePromptSubmissionResult(
                    Success: false,
                    CreatedRun: true,
                    DispatchAccepted: false,
                    Message: stored.Message,
                    Task: task,
                    Run: failedRun ?? run);
            }

            storedImages = stored.Images.ToList();
            var fallbackBlock = workspaceImageStorageService.BuildFallbackReferenceBlock(stored.Images);
            if (!string.IsNullOrWhiteSpace(fallbackBlock))
            {
                dispatchTask.Prompt = AppendPromptSuffix(dispatchTask.Prompt, fallbackBlock);
            }
        }

        var instructionStack = await BuildRunInstructionStackAsync(
            repository,
            dispatchTask,
            run,
            sessionProfile,
            request.UserMessage,
            cancellationToken);
        instructionStack = await store.UpsertRunInstructionStackAsync(instructionStack, cancellationToken);

        var settings = await store.GetSettingsAsync(cancellationToken);
        var mcpConfigSnapshotJson = !string.IsNullOrWhiteSpace(sessionProfile?.McpConfigJson)
            ? sessionProfile.McpConfigJson
            : settings.Orchestrator.McpConfigJson;

        var dispatchAccepted = await dispatcher.DispatchAsync(
            repository,
            dispatchTask,
            run,
            cancellationToken,
            BuildDispatchInputParts(dispatchTask.Prompt, storedImages),
            BuildDispatchImageAttachments(storedImages),
            request.PreferNativeMultimodal,
            "auto-text-reference",
            sessionProfile?.Id,
            instructionStack.Hash,
            mcpConfigSnapshotJson,
            run.AutomationRunId);

        NotifyRunEvent(run.Id, "created", DateTime.UtcNow);

        if (!string.IsNullOrWhiteSpace(request.UserMessage) || storedImages.Count > 0)
        {
            var promptEntry = await AppendUserPromptEntryAsync(
                repositoryId,
                task.Id,
                run.Id,
                request.UserMessage ?? string.Empty,
                storedImages,
                cancellationToken);
            taskSemanticEmbeddingService.QueueTaskEmbedding(
                repositoryId,
                task.Id,
                "user-message",
                runId: run.Id,
                promptEntryId: promptEntry?.Id);
        }

        logger.ZLogDebug(
            "Workspace prompt submission created run {RunId} for task {TaskId} (dispatch accepted: {DispatchAccepted})",
            run.Id,
            task.Id,
            dispatchAccepted);

        return new WorkspacePromptSubmissionResult(
            Success: true,
            CreatedRun: true,
            DispatchAccepted: dispatchAccepted,
            Message: dispatchAccepted
                ? $"Run {run.Id} created and dispatched."
                : $"Run {run.Id} created and queued.",
            Task: task,
            Run: run);
    }

    private async Task<WorkspacePromptSubmissionResult> SubmitAgentTeamRunAsync(
        RepositoryDocument repository,
        TaskDocument task,
        WorkspacePromptSubmissionRequest request,
        CancellationToken cancellationToken)
    {
        var teamRequest = request.AgentTeam;
        if (teamRequest is null || teamRequest.Members.Count == 0)
        {
            return new WorkspacePromptSubmissionResult(
                Success: false,
                CreatedRun: false,
                DispatchAccepted: false,
                Message: "Agent Team run requires at least one member.",
                Task: task,
                Run: null);
        }

        var command = string.IsNullOrWhiteSpace(request.Command)
            ? task.Command
            : request.Command.Trim();
        if (string.IsNullOrWhiteSpace(command))
        {
            return new WorkspacePromptSubmissionResult(
                Success: false,
                CreatedRun: false,
                DispatchAccepted: false,
                Message: "Task command is required to start an Agent Team run.",
                Task: task,
                Run: null);
        }

        var prompt = string.IsNullOrWhiteSpace(request.Prompt)
            ? task.Prompt
            : request.Prompt.Trim();
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new WorkspacePromptSubmissionResult(
                Success: false,
                CreatedRun: false,
                DispatchAccepted: false,
                Message: "Prompt cannot be empty for an Agent Team run.",
                Task: task,
                Run: null);
        }

        if (request.Images is { Count: > 0 })
        {
            return new WorkspacePromptSubmissionResult(
                Success: false,
                CreatedRun: false,
                DispatchAccepted: false,
                Message: "Image attachments are not supported for Agent Team runs yet.",
                Task: task,
                Run: null);
        }

        var members = teamRequest.Members
            .Select((member, index) => new WorkflowAgentTeamMemberConfig
            {
                Name = string.IsNullOrWhiteSpace(member.RolePrompt) ? $"Agent {index + 1}" : $"Agent {index + 1}",
                Harness = NormalizeHarness(member.Harness, task.Harness),
                Mode = member.Mode,
                RolePrompt = member.RolePrompt?.Trim() ?? string.Empty,
                ModelOverride = member.ModelOverride?.Trim() ?? string.Empty,
                TimeoutSeconds = member.TimeoutSeconds,
            })
            .ToList();

        members = members
            .Where(member => !string.IsNullOrWhiteSpace(member.Harness))
            .ToList();

        if (members.Count == 0)
        {
            return new WorkspacePromptSubmissionResult(
                Success: false,
                CreatedRun: false,
                DispatchAccepted: false,
                Message: "Agent Team run members are invalid.",
                Task: task,
                Run: null);
        }

        var synthesis = BuildSynthesisConfig(teamRequest.Synthesis, task, request.ModeOverride);
        var workflow = new WorkflowDocument
        {
            Id = Guid.NewGuid().ToString("N"),
            RepositoryId = repository.Id,
            Name = $"workspace-team-{DateTime.UtcNow:yyyyMMdd-HHmmss}",
            Description = $"Ephemeral workspace team run for task {task.Name}",
            Enabled = false,
            Stages =
            [
                new WorkflowStageConfig
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = "Agent Team Parallel",
                    Type = WorkflowStageType.Parallel,
                    TaskId = task.Id,
                    PromptOverride = prompt,
                    CommandOverride = command,
                    AgentTeamMembers = members,
                    Synthesis = synthesis,
                    Order = 0
                }
            ]
        };

        var execution = await workflowExecutor.ExecuteWorkflowAsync(workflow, cancellationToken);

        if (!string.IsNullOrWhiteSpace(request.UserMessage))
        {
            await AppendUserPromptEntryAsync(
                repository.Id,
                task.Id,
                execution.Id,
                request.UserMessage,
                [],
                cancellationToken);
        }

        return new WorkspacePromptSubmissionResult(
            Success: true,
            CreatedRun: false,
            DispatchAccepted: true,
            Message: $"Agent Team run started with {members.Count} lane(s).",
            Task: task,
            Run: null,
            WorkflowExecution: execution,
            TeamMemberCount: members.Count,
            TeamSynthesisEnabled: synthesis is { Enabled: true });
    }

    private static WorkflowSynthesisStageConfig? BuildSynthesisConfig(
        WorkspaceAgentTeamSynthesisRequest? request,
        TaskDocument task,
        HarnessExecutionMode? fallbackMode)
    {
        if (request is null || !request.Enabled)
        {
            return null;
        }

        var prompt = request.Prompt?.Trim() ?? string.Empty;
        if (prompt.Length == 0)
        {
            prompt = "Produce a final synthesis from all agent lanes, including risks, changed files, and next steps.";
        }

        return new WorkflowSynthesisStageConfig
        {
            Enabled = true,
            Harness = NormalizeHarness(request.Harness, task.Harness),
            Mode = request.Mode ?? fallbackMode ?? task.ExecutionModeDefault ?? HarnessExecutionMode.Default,
            Prompt = prompt,
            ModelOverride = request.ModelOverride?.Trim() ?? string.Empty,
            TimeoutSeconds = request.TimeoutSeconds,
        };
    }

    private static string NormalizeHarness(string? harness, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(harness))
        {
            return harness.Trim().ToLowerInvariant();
        }

        return string.IsNullOrWhiteSpace(fallback)
            ? "codex"
            : fallback.Trim().ToLowerInvariant();
    }

    public void NotifyRunEvent(string runId, string eventType, DateTime eventTimestampUtc)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return;
        }

        var state = _summaryRefreshStates.GetOrAdd(runId, _ => new SummaryRefreshState());
        var timestamp = eventTimestampUtc == default ? DateTime.UtcNow : eventTimestampUtc;

        lock (state.SyncRoot)
        {
            state.LastEventAtUtc = timestamp;
            state.LastEventType = eventType ?? string.Empty;
        }
    }

    public async Task<WorkspaceSummaryRefreshResult> RefreshRunSummaryAsync(
        string repositoryId,
        string runId,
        string eventType,
        bool force,
        CancellationToken cancellationToken)
    {
        var run = await store.GetRunAsync(runId, cancellationToken);
        if (run is null || !string.Equals(run.RepositoryId, repositoryId, StringComparison.OrdinalIgnoreCase))
        {
            return new WorkspaceSummaryRefreshResult(
                Success: false,
                Refreshed: false,
                Summary: string.Empty,
                UsedFallback: true,
                KeyConfigured: false,
                Message: "Run not found.",
                LastRefreshAtUtc: null,
                NextAllowedRefreshAtUtc: null);
        }

        var cachedSummary = await store.GetRunAiSummaryAsync(runId, cancellationToken);

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            NotifyRunEvent(runId, eventType, DateTime.UtcNow);
        }

        var runLogs = await store.ListRunLogsAsync(runId, cancellationToken);
        var parsedOutput = parserService.Parse(run.OutputJson, runLogs);
        var signature = ComputeSummarySignature(run, runLogs, parsedOutput);

        TrackObservedRunSnapshot(runId, signature, DateTime.UtcNow);

        var decision = EvaluateSummaryRefresh(runId, run.State, signature, force);
        if (!decision.ShouldRefresh)
        {
            var hasCachedSummary = !string.IsNullOrWhiteSpace(cachedSummary?.Summary);
            var cachedUsedFallback = string.Equals(cachedSummary?.Model, "fallback", StringComparison.OrdinalIgnoreCase);
            var summary = !string.IsNullOrWhiteSpace(cachedSummary?.Summary)
                ? cachedSummary.Summary
                : ResolveExistingSummary(run, runId, parsedOutput);
            return new WorkspaceSummaryRefreshResult(
                Success: true,
                Refreshed: false,
                Summary: summary,
                UsedFallback: !hasCachedSummary || cachedUsedFallback,
                KeyConfigured: hasCachedSummary && !cachedUsedFallback,
                Message: decision.Reason,
                LastRefreshAtUtc: decision.LastRefreshAtUtc,
                NextAllowedRefreshAtUtc: decision.NextAllowedRefreshAtUtc);
        }

        var aiResult = await workspaceAiService.SummarizeRunOutputAsync(
            repositoryId,
            run.OutputJson,
            runLogs,
            cancellationToken);

        var resolvedSummary = string.IsNullOrWhiteSpace(aiResult.Text)
            ? ResolveExistingSummary(run, runId, parsedOutput)
            : aiResult.Text.Trim();

        var now = DateTime.UtcNow;
        var state = _summaryRefreshStates.GetOrAdd(runId, _ => new SummaryRefreshState());

        lock (state.SyncRoot)
        {
            state.LastRefreshAtUtc = now;
            state.LastRefreshedSignature = signature;
            state.LastSummary = resolvedSummary;
            state.LastEventType = eventType ?? state.LastEventType;
        }

        var summaryDocument = new RunAiSummaryDocument
        {
            RunId = run.Id,
            RepositoryId = run.RepositoryId,
            TaskId = run.TaskId,
            Title = BuildSummaryTitle(resolvedSummary, run),
            Summary = resolvedSummary,
            Model = aiResult.UsedFallback ? "fallback" : "glm-5",
            SourceFingerprint = signature,
            SourceUpdatedAtUtc = run.EndedAtUtc ?? run.CreatedAtUtc,
            GeneratedAtUtc = now,
            ExpiresAtUtc = now.AddHours(24),
        };
        await store.UpsertRunAiSummaryAsync(summaryDocument, cancellationToken);

        return new WorkspaceSummaryRefreshResult(
            Success: true,
            Refreshed: true,
            Summary: resolvedSummary,
            UsedFallback: aiResult.UsedFallback,
            KeyConfigured: aiResult.KeyConfigured,
                Message: aiResult.Message ?? "Summary refreshed.",
                LastRefreshAtUtc: now,
                NextAllowedRefreshAtUtc: now + s_summaryRefreshCooldown);
    }

    private static string BuildSummaryTitle(string summary, RunDocument run)
    {
        var normalized = summary.Trim();
        if (normalized.Length == 0)
        {
            return $"Run {run.Id[..Math.Min(8, run.Id.Length)]} summary";
        }

        var line = normalized
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? normalized;

        return line.Length <= 80 ? line : $"{line[..80]}...";
    }

    private async Task<TaskDocument?> ResolveTaskAsync(
        IReadOnlyList<TaskDocument> tasks,
        string repositoryId,
        WorkspacePromptSubmissionRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.TaskId))
        {
            var matchedTask = tasks.FirstOrDefault(x => x.Id == request.TaskId);
            if (matchedTask is not null)
            {
                return matchedTask;
            }

            var loaded = await store.GetTaskAsync(request.TaskId, cancellationToken);
            if (loaded is not null && string.Equals(loaded.RepositoryId, repositoryId, StringComparison.OrdinalIgnoreCase))
            {
                return loaded;
            }

            return null;
        }

        var selected = tasks
            .Where(x => x.Enabled)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault() ?? tasks.OrderByDescending(x => x.CreatedAtUtc).FirstOrDefault();

        if (selected is not null)
        {
            return selected;
        }

        if (string.IsNullOrWhiteSpace(request.Command))
        {
            return null;
        }

        var createRequest = new CreateTaskRequest(
            RepositoryId: repositoryId,
            Name: "Workspace Prompt Task",
            Kind: TaskKind.OneShot,
            Harness: string.IsNullOrWhiteSpace(request.Harness) ? "codex" : request.Harness.Trim().ToLowerInvariant(),
            Prompt: string.IsNullOrWhiteSpace(request.Prompt)
                ? "Execute the task and return a structured JSON envelope."
                : request.Prompt.Trim(),
            Command: request.Command.Trim(),
            AutoCreatePullRequest: false,
            CronExpression: string.Empty,
            Enabled: true,
            ExecutionModeDefault: request.ModeOverride,
            SessionProfileId: request.SessionProfileId);

        return await store.CreateTaskAsync(createRequest, cancellationToken);
    }

    private async Task<RunInstructionStackDocument> BuildRunInstructionStackAsync(
        RepositoryDocument repository,
        TaskDocument task,
        RunDocument run,
        RunSessionProfileDocument? sessionProfile,
        string? runOverrideMessage,
        CancellationToken cancellationToken)
    {
        var settings = await store.GetSettingsAsync(cancellationToken);
        var globalRules = BuildGlobalRules(settings);

        var repositoryRuleParts = new List<string>();
        var repositoryInstructions = await store.GetInstructionsAsync(repository.Id, cancellationToken);
        foreach (var instruction in repositoryInstructions.Where(x => x.Enabled).OrderBy(x => x.Priority))
        {
            if (!string.IsNullOrWhiteSpace(instruction.Content))
            {
                repositoryRuleParts.Add(instruction.Content.Trim());
            }
        }

        foreach (var instructionFile in repository.InstructionFiles.OrderBy(x => x.Order))
        {
            if (!string.IsNullOrWhiteSpace(instructionFile.Content))
            {
                repositoryRuleParts.Add(instructionFile.Content.Trim());
            }
        }

        var taskRuleParts = task.InstructionFiles
            .OrderBy(x => x.Order)
            .Where(x => !string.IsNullOrWhiteSpace(x.Content))
            .Select(x => x.Content.Trim())
            .ToList();

        var repositoryRules = string.Join("\n\n", repositoryRuleParts);
        var taskRules = string.Join("\n\n", taskRuleParts);
        var runOverrides = runOverrideMessage?.Trim() ?? string.Empty;

        var resolvedBuilder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(globalRules))
        {
            resolvedBuilder.AppendLine("--- [Global Rules] ---");
            resolvedBuilder.AppendLine(globalRules);
            resolvedBuilder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(repositoryRules))
        {
            resolvedBuilder.AppendLine("--- [Repository Rules] ---");
            resolvedBuilder.AppendLine(repositoryRules);
            resolvedBuilder.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(taskRules))
        {
            resolvedBuilder.AppendLine("--- [Task Rules] ---");
            resolvedBuilder.AppendLine(taskRules);
            resolvedBuilder.AppendLine();
        }

        resolvedBuilder.AppendLine("--- [Prompt] ---");
        resolvedBuilder.AppendLine(task.Prompt);

        if (!string.IsNullOrWhiteSpace(runOverrides))
        {
            resolvedBuilder.AppendLine();
            resolvedBuilder.AppendLine("--- [Run Overrides] ---");
            resolvedBuilder.AppendLine(runOverrides);
        }

        var resolvedText = resolvedBuilder.ToString().Trim();
        var hash = ComputeInstructionStackHash(resolvedText);

        return new RunInstructionStackDocument
        {
            RunId = run.Id,
            RepositoryId = repository.Id,
            TaskId = task.Id,
            SessionProfileId = sessionProfile?.Id ?? string.Empty,
            GlobalRules = globalRules,
            RepositoryRules = repositoryRules,
            TaskRules = taskRules,
            RunOverrides = runOverrides,
            ResolvedText = resolvedText,
            Hash = hash,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    private static string BuildGlobalRules(SystemSettingsDocument settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Orchestrator.GlobalRunRules))
        {
            return settings.Orchestrator.GlobalRunRules.Trim();
        }

        var builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(settings.Orchestrator.TaskPromptPrefix))
        {
            builder.AppendLine(settings.Orchestrator.TaskPromptPrefix.Trim());
        }

        if (!string.IsNullOrWhiteSpace(settings.Orchestrator.TaskPromptSuffix))
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.AppendLine(settings.Orchestrator.TaskPromptSuffix.Trim());
        }

        return builder.ToString().Trim();
    }

    private static string ComputeInstructionStackHash(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private WorkspaceSummaryRefreshStateView EvaluateSummaryRefresh(
        string runId,
        RunState runState,
        string signature,
        bool force)
    {
        var state = _summaryRefreshStates.GetOrAdd(runId, _ => new SummaryRefreshState());
        var now = DateTime.UtcNow;

        lock (state.SyncRoot)
        {
            var hasSignatureDelta = !string.Equals(signature, state.LastRefreshedSignature, StringComparison.Ordinal);
            var hasEventAfterRefresh =
                state.LastEventAtUtc.HasValue &&
                (!state.LastRefreshAtUtc.HasValue || state.LastEventAtUtc.Value > state.LastRefreshAtUtc.Value);

            var hasNewEvents = hasSignatureDelta || hasEventAfterRefresh;

            if (!force && !s_completedStates.Contains(runState))
            {
                return new WorkspaceSummaryRefreshStateView(
                    ShouldRefresh: false,
                    Reason: "Run is still active.",
                    LastRefreshAtUtc: state.LastRefreshAtUtc,
                    NextAllowedRefreshAtUtc: null,
                    HasNewEvents: hasNewEvents);
            }

            if (!force && !hasNewEvents)
            {
                return new WorkspaceSummaryRefreshStateView(
                    ShouldRefresh: false,
                    Reason: "No new run events since last summary refresh.",
                    LastRefreshAtUtc: state.LastRefreshAtUtc,
                    NextAllowedRefreshAtUtc: null,
                    HasNewEvents: false);
            }

            if (!force && state.LastRefreshAtUtc is { } lastRefresh)
            {
                var cooldownEndsAt = lastRefresh + s_summaryRefreshCooldown;
                if (cooldownEndsAt > now)
                {
                    return new WorkspaceSummaryRefreshStateView(
                        ShouldRefresh: false,
                        Reason: "Summary refresh cooldown active.",
                        LastRefreshAtUtc: state.LastRefreshAtUtc,
                        NextAllowedRefreshAtUtc: cooldownEndsAt,
                        HasNewEvents: hasNewEvents);
                }
            }

            return new WorkspaceSummaryRefreshStateView(
                ShouldRefresh: true,
                Reason: force ? "Forced refresh." : "New run events detected.",
                LastRefreshAtUtc: state.LastRefreshAtUtc,
                NextAllowedRefreshAtUtc: null,
                HasNewEvents: hasNewEvents);
        }
    }

    private void TrackObservedRunSnapshot(string runId, string signature, DateTime observedAtUtc)
    {
        var state = _summaryRefreshStates.GetOrAdd(runId, _ => new SummaryRefreshState());

        lock (state.SyncRoot)
        {
            if (string.Equals(state.LastObservedSignature, signature, StringComparison.Ordinal))
            {
                return;
            }

            state.LastObservedSignature = signature;
            state.LastEventAtUtc = observedAtUtc;
        }
    }

    private string ResolveExistingSummary(RunDocument run, string runId, ParsedHarnessOutput parsedOutput)
    {
        if (!string.IsNullOrWhiteSpace(run.Summary))
        {
            return run.Summary.Trim();
        }

        if (!string.IsNullOrWhiteSpace(parsedOutput.Summary))
        {
            return parsedOutput.Summary;
        }

        var state = _summaryRefreshStates.GetOrAdd(runId, _ => new SummaryRefreshState());
        lock (state.SyncRoot)
        {
            if (!string.IsNullOrWhiteSpace(state.LastSummary))
            {
                return state.LastSummary;
            }
        }

        return parsedOutput.Status;
    }

    private static RunDocument? SelectRun(IReadOnlyList<RunDocument> runs, string? selectedRunId)
    {
        if (string.IsNullOrWhiteSpace(selectedRunId))
        {
            return null;
        }

        return runs.FirstOrDefault(x => x.Id == selectedRunId);
    }

    private static RunDocument? SelectLatestActiveRun(IReadOnlyList<RunDocument> runs)
    {
        return runs
            .Where(x => s_activeStates.Contains(x.State))
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefault();
    }

    private static RunDocument? SelectLatestCompletedRun(IReadOnlyList<RunDocument> runs)
    {
        return runs
            .Where(x => s_completedStates.Contains(x.State))
            .OrderByDescending(x => x.EndedAtUtc ?? x.CreatedAtUtc)
            .FirstOrDefault();
    }

    private static string ComputeSummarySignature(
        RunDocument run,
        IReadOnlyList<RunLogEvent> runLogs,
        ParsedHarnessOutput parsedOutput)
    {
        var builder = new StringBuilder();
        builder.Append(run.Id)
            .Append('|')
            .Append(run.State)
            .Append('|')
            .Append(run.CreatedAtUtc.Ticks)
            .Append('|')
            .Append(run.StartedAtUtc?.Ticks ?? 0)
            .Append('|')
            .Append(run.EndedAtUtc?.Ticks ?? 0)
            .Append('|')
            .Append(Truncate(run.OutputJson, 8000))
            .Append('|')
            .Append(parsedOutput.Status)
            .Append('|')
            .Append(parsedOutput.Summary)
            .Append('|')
            .Append(parsedOutput.Error)
            .Append('|')
            .Append(parsedOutput.ToolCallGroups.Count)
            .Append('|')
            .Append(parsedOutput.RawStream.Count);

        foreach (var log in runLogs.TakeLast(300))
        {
            builder
                .Append('|')
                .Append(log.TimestampUtc.Ticks)
                .Append(':')
                .Append(log.Level)
                .Append(':')
                .Append(Truncate(log.Message, 500));
        }

        var bytes = Encoding.UTF8.GetBytes(builder.ToString());
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    private static TaskDocument CloneTask(TaskDocument source)
    {
        return new TaskDocument
        {
            Id = source.Id,
            RepositoryId = source.RepositoryId,
            Name = source.Name,
            Kind = source.Kind,
            Harness = source.Harness,
            ExecutionModeDefault = source.ExecutionModeDefault,
            SessionProfileId = source.SessionProfileId,
            Prompt = source.Prompt,
            Command = source.Command,
            AutoCreatePullRequest = source.AutoCreatePullRequest,
            CronExpression = source.CronExpression,
            Enabled = source.Enabled,
            NextRunAtUtc = source.NextRunAtUtc,
            RetryPolicy = source.RetryPolicy,
            Timeouts = source.Timeouts,
            ApprovalProfile = source.ApprovalProfile,
            SandboxProfile = source.SandboxProfile,
            ArtifactPolicy = source.ArtifactPolicy,
            ArtifactPatterns = [.. source.ArtifactPatterns],
            LinkedFailureRuns = [.. source.LinkedFailureRuns],
            ConcurrencyLimit = source.ConcurrencyLimit,
            InstructionFiles = [.. source.InstructionFiles],
            CreatedAtUtc = source.CreatedAtUtc,
        };
    }

    private async Task<WorkspacePromptEntryDocument?> AppendUserPromptEntryAsync(
        string repositoryId,
        string taskId,
        string runId,
        string userMessage,
        IReadOnlyList<WorkspaceStoredImage> images,
        CancellationToken cancellationToken)
    {
        var normalizedMessage = userMessage.Trim();
        if (normalizedMessage.Length == 0 && images.Count > 0)
        {
            normalizedMessage = workspaceImageStorageService.BuildFallbackReferenceBlock(images);
        }

        if (normalizedMessage.Length == 0)
        {
            return null;
        }

        var imageMetadataJson = images.Count == 0
            ? string.Empty
            : JsonSerializer.Serialize(images.Select(image => new
            {
                image.Id,
                image.FileName,
                image.MimeType,
                image.SizeBytes,
                image.ArtifactName,
                image.ArtifactPath,
                image.Sha256,
                image.Width,
                image.Height
            }));

        return await store.AppendWorkspacePromptEntryAsync(
            new WorkspacePromptEntryDocument
            {
                RepositoryId = repositoryId,
                TaskId = taskId,
                RunId = runId,
                Role = "user",
                Content = normalizedMessage,
                HasImages = images.Count > 0,
                ImageMetadataJson = imageMetadataJson,
                CreatedAtUtc = DateTime.UtcNow,
            },
            cancellationToken);
    }

    private static List<DispatchInputPart>? BuildDispatchInputParts(
        string prompt,
        IReadOnlyList<WorkspaceStoredImage> images)
    {
        if (images.Count == 0)
        {
            return null;
        }

        var parts = new List<DispatchInputPart>(images.Count + 1)
        {
            new DispatchInputPart
            {
                Type = "text",
                Text = prompt,
            }
        };

        foreach (var image in images)
        {
            var imageRef = image.DataUrl;
            if (string.IsNullOrWhiteSpace(imageRef))
            {
                imageRef = image.ArtifactPath;
            }

            parts.Add(new DispatchInputPart
            {
                Type = "image",
                ImageRef = imageRef,
                MimeType = image.MimeType,
                Width = image.Width,
                Height = image.Height,
                SizeBytes = image.SizeBytes,
                Alt = image.FileName,
            });
        }

        return parts;
    }

    private static List<DispatchImageAttachment>? BuildDispatchImageAttachments(IReadOnlyList<WorkspaceStoredImage> images)
    {
        if (images.Count == 0)
        {
            return null;
        }

        return images
            .Select(image => new DispatchImageAttachment
            {
                Id = image.Id,
                FileName = image.FileName,
                MimeType = image.MimeType,
                SizeBytes = image.SizeBytes,
                StoragePath = image.ArtifactPath,
                Sha256 = image.Sha256,
                Width = image.Width,
                Height = image.Height,
            })
            .ToList();
    }

    private static string AppendPromptSuffix(string prompt, string suffix)
    {
        if (string.IsNullOrWhiteSpace(suffix))
        {
            return prompt;
        }

        if (string.IsNullOrWhiteSpace(prompt))
        {
            return suffix.Trim();
        }

        return $"{prompt.TrimEnd()}\n\n{suffix.Trim()}";
    }

    private sealed class SummaryRefreshState
    {
        public object SyncRoot { get; } = new();
        public DateTime? LastEventAtUtc { get; set; }
        public DateTime? LastRefreshAtUtc { get; set; }
        public string LastObservedSignature { get; set; } = string.Empty;
        public string LastRefreshedSignature { get; set; } = string.Empty;
        public string LastSummary { get; set; } = string.Empty;
        public string LastEventType { get; set; } = string.Empty;
    }
}
