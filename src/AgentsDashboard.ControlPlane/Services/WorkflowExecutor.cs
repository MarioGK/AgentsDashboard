using System.Text;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.ControlPlane.Services;

public class WorkflowExecutor(
    IOrchestratorStore store,
    RunDispatcher dispatcher,
    IContainerReaper containerReaper,
    IOptions<OrchestratorOptions> options,
    ILogger<WorkflowExecutor> logger,
    TimeProvider? timeProvider = null) : IWorkflowExecutor
{
    private readonly StageTimeoutConfig _timeoutConfig = options.Value.StageTimeout;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public virtual async Task<WorkflowExecutionDocument> ExecuteWorkflowAsync(
        WorkflowDocument workflow,
        CancellationToken cancellationToken)
    {
        var execution = new WorkflowExecutionDocument
        {
            WorkflowId = workflow.Id,
            RepositoryId = workflow.RepositoryId,
            State = WorkflowExecutionState.Running,
            CurrentStageIndex = 0,
            StartedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
        };

        execution = await store.CreateWorkflowExecutionAsync(execution, cancellationToken);

        logger.LogInformation("Starting workflow execution {ExecutionId} for workflow {WorkflowId} with {StageCount} stages",
            execution.Id, workflow.Id, workflow.Stages.Count);

        _ = Task.Run(async () =>
        {
            try
            {
                await ExecuteWorkflowStagesAsync(execution, workflow, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled exception in workflow execution {ExecutionId}", execution.Id);
                await store.MarkWorkflowExecutionCompletedAsync(
                    execution.Id,
                    WorkflowExecutionState.Failed,
                    $"Unhandled exception: {ex.Message}",
                    cancellationToken);
            }
        }, cancellationToken);

        return execution;
    }

    private async Task ExecuteWorkflowStagesAsync(
        WorkflowExecutionDocument execution,
        WorkflowDocument workflow,
        CancellationToken cancellationToken)
    {
        var orderedStages = workflow.Stages.OrderBy(s => s.Order).ToList();
        var maxStageTimeout = TimeSpan.FromHours(_timeoutConfig.MaxStageTimeoutHours);

        for (int i = 0; i < orderedStages.Count; i++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                logger.LogInformation("Workflow execution {ExecutionId} cancelled at stage {StageIndex}", execution.Id, i);
                await store.MarkWorkflowExecutionCompletedAsync(
                    execution.Id,
                    WorkflowExecutionState.Cancelled,
                    "Cancelled by user",
                    cancellationToken);
                return;
            }

            var stage = orderedStages[i];
            execution.CurrentStageIndex = i;

            logger.LogInformation("Executing stage {StageIndex}/{TotalStages}: {StageName} (Type: {StageType})",
                i + 1, orderedStages.Count, stage.Name, stage.Type);

            var stageResult = new WorkflowStageResult
            {
                StageId = stage.Id,
                StageName = stage.Name,
                StageType = stage.Type,
                StartedAtUtc = _timeProvider.GetUtcNow().UtcDateTime
            };

            var stageTimeout = GetStageTimeout(stage);
            stageTimeout = stageTimeout > maxStageTimeout ? maxStageTimeout : stageTimeout;

            using var stageCts = new CancellationTokenSource(stageTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stageCts.Token);

            try
            {
                await ExecuteStageWithTimeoutAsync(stage, stageResult, execution, linkedCts.Token);

                stageResult.EndedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
                execution.StageResults.Add(stageResult);
                await store.UpdateWorkflowExecutionAsync(execution, cancellationToken);

                if (!stageResult.Succeeded)
                {
                    logger.LogWarning("Stage {StageName} failed: {Summary}", stage.Name, stageResult.Summary);
                    await store.MarkWorkflowExecutionCompletedAsync(
                        execution.Id,
                        WorkflowExecutionState.Failed,
                        $"Stage '{stage.Name}' failed: {stageResult.Summary}",
                        cancellationToken);

                    await store.CreateFindingFromFailureAsync(
                        new RunDocument { Id = execution.Id, RepositoryId = execution.RepositoryId },
                        $"Workflow '{workflow.Name}' failed at stage '{stage.Name}': {stageResult.Summary}",
                        cancellationToken);
                    return;
                }

                logger.LogInformation("Stage {StageName} completed successfully: {Summary}", stage.Name, stageResult.Summary);
            }
            catch (OperationCanceledException) when (stageCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Stage {StageName} timed out after {Timeout}", stage.Name, stageTimeout);
                stageResult.Succeeded = false;
                stageResult.Summary = $"Stage timed out after {stageTimeout.TotalMinutes:F0} minutes";
                stageResult.EndedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
                execution.StageResults.Add(stageResult);
                await store.UpdateWorkflowExecutionAsync(execution, cancellationToken);

                await KillStageContainersAsync(stageResult.RunIds, "Stage timeout");

                await store.MarkWorkflowExecutionCompletedAsync(
                    execution.Id,
                    WorkflowExecutionState.Failed,
                    $"Stage '{stage.Name}' timed out after {stageTimeout.TotalMinutes:F0} minutes",
                    cancellationToken);
                return;
            }
            catch (OperationCanceledException)
            {
                logger.LogInformation("Workflow execution {ExecutionId} cancelled during stage {StageName}", execution.Id, stage.Name);
                await store.MarkWorkflowExecutionCompletedAsync(
                    execution.Id,
                    WorkflowExecutionState.Cancelled,
                    $"Cancelled during stage '{stage.Name}'",
                    cancellationToken);
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception during stage {StageName} execution", stage.Name);
                stageResult.Succeeded = false;
                stageResult.Summary = $"Exception: {ex.Message}";
                stageResult.EndedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
                execution.StageResults.Add(stageResult);
                await store.UpdateWorkflowExecutionAsync(execution, cancellationToken);
                await store.MarkWorkflowExecutionCompletedAsync(
                    execution.Id,
                    WorkflowExecutionState.Failed,
                    $"Stage '{stage.Name}' threw exception: {ex.Message}",
                    cancellationToken);
                return;
            }
        }

        logger.LogInformation("Workflow execution {ExecutionId} completed successfully", execution.Id);
        await store.MarkWorkflowExecutionCompletedAsync(
            execution.Id,
            WorkflowExecutionState.Succeeded,
            string.Empty,
            cancellationToken);
    }

    private async Task ExecuteStageWithTimeoutAsync(
        WorkflowStageConfig stage,
        WorkflowStageResult result,
        WorkflowExecutionDocument execution,
        CancellationToken cancellationToken)
    {
        switch (stage.Type)
        {
            case WorkflowStageType.Task:
                await ExecuteTaskStageAsync(stage, result, cancellationToken);
                break;
            case WorkflowStageType.Approval:
                await ExecuteApprovalStageAsync(stage, result, execution, cancellationToken);
                break;
            case WorkflowStageType.Delay:
                await ExecuteDelayStageAsync(stage, result, cancellationToken);
                break;
            case WorkflowStageType.Parallel:
                await ExecuteParallelStageAsync(stage, result, cancellationToken);
                break;
        }
    }

    private TimeSpan GetStageTimeout(WorkflowStageConfig stage)
    {
        if (stage.TimeoutMinutes.HasValue && stage.TimeoutMinutes.Value > 0)
        {
            return TimeSpan.FromMinutes(stage.TimeoutMinutes.Value);
        }

        return stage.Type switch
        {
            WorkflowStageType.Task => TimeSpan.FromMinutes(_timeoutConfig.DefaultTaskStageTimeoutMinutes),
            WorkflowStageType.Approval => TimeSpan.FromHours(_timeoutConfig.DefaultApprovalStageTimeoutHours),
            WorkflowStageType.Parallel => TimeSpan.FromMinutes(_timeoutConfig.DefaultParallelStageTimeoutMinutes),
            WorkflowStageType.Delay => TimeSpan.FromHours(1),
            _ => TimeSpan.FromMinutes(60)
        };
    }

    private async Task KillStageContainersAsync(List<string> runIds, string reason)
    {
        foreach (var runId in runIds)
        {
            try
            {
                await containerReaper.KillContainerAsync(runId, reason, force: true, CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to kill container for run {RunId}", runId);
            }
        }
    }

    private async Task ExecuteTaskStageAsync(
        WorkflowStageConfig stage,
        WorkflowStageResult result,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(stage.TaskId))
        {
            result.Succeeded = false;
            result.Summary = "No TaskId specified for Task stage";
            return;
        }

        var task = await store.GetTaskAsync(stage.TaskId, cancellationToken);
        if (task is null)
        {
            result.Succeeded = false;
            result.Summary = $"Task {stage.TaskId} not found";
            return;
        }

        var repository = await store.GetRepositoryAsync(task.RepositoryId, cancellationToken);
        if (repository is null)
        {
            result.Succeeded = false;
            result.Summary = $"Repository {task.RepositoryId} not found";
            return;
        }

        var run = await store.CreateRunAsync(task, cancellationToken);
        result.RunIds.Add(run.Id);

        logger.LogInformation("Created run {RunId} for task stage {StageName} (task {TaskId})",
            run.Id, stage.Name, task.Id);

        var dispatched = await dispatcher.DispatchAsync(repository, task, run, cancellationToken);
        if (!dispatched)
        {
            logger.LogInformation(
                "Run {RunId} for workflow stage {StageName} was queued; waiting for terminal state",
                run.Id,
                stage.Name);
        }

        var pollingInterval = TimeSpan.FromSeconds(2);
        var stageTimeout = GetStageTimeout(stage);
        var startTime = _timeProvider.GetUtcNow().UtcDateTime;

        while (_timeProvider.GetUtcNow().UtcDateTime - startTime < stageTimeout)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                result.Succeeded = false;
                result.Summary = "Cancelled";
                return;
            }

            await Task.Delay(pollingInterval, cancellationToken);

            var updatedRun = await store.GetRunAsync(run.Id, cancellationToken);
            if (updatedRun is null)
            {
                result.Succeeded = false;
                result.Summary = "Run disappeared from database";
                return;
            }

            if (updatedRun.State == RunState.Succeeded)
            {
                result.Succeeded = true;
                result.Summary = updatedRun.Summary;
                return;
            }

            if (updatedRun.State == RunState.Failed)
            {
                result.Succeeded = false;
                result.Summary = $"Run failed: {updatedRun.Summary}";
                return;
            }

            if (updatedRun.State == RunState.Cancelled)
            {
                result.Succeeded = false;
                result.Summary = "Run was cancelled";
                return;
            }
        }

        result.Succeeded = false;
        result.Summary = "Timeout waiting for run to complete";
    }

    private async Task ExecuteApprovalStageAsync(
        WorkflowStageConfig stage,
        WorkflowStageResult result,
        WorkflowExecutionDocument execution,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Stage {StageName} requires approval", stage.Name);

        await store.MarkWorkflowExecutionPendingApprovalAsync(execution.Id, stage.Id, cancellationToken);

        var stageTimeout = GetStageTimeout(stage);
        var pollingInterval = TimeSpan.FromSeconds(5);
        var startTime = _timeProvider.GetUtcNow().UtcDateTime;

        while (_timeProvider.GetUtcNow().UtcDateTime - startTime < stageTimeout)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                result.Succeeded = false;
                result.Summary = "Cancelled";
                return;
            }

            await Task.Delay(pollingInterval, cancellationToken);

            var updatedExecution = await store.GetWorkflowExecutionAsync(execution.Id, cancellationToken);
            if (updatedExecution is null)
            {
                result.Succeeded = false;
                result.Summary = "Execution disappeared from database";
                return;
            }

            if (updatedExecution.State == WorkflowExecutionState.Running &&
                updatedExecution.PendingApprovalStageId == string.Empty)
            {
                result.Succeeded = true;
                result.Summary = $"Approved by {updatedExecution.ApprovedBy}";
                execution.State = WorkflowExecutionState.Running;
                execution.ApprovedBy = updatedExecution.ApprovedBy;
                execution.PendingApprovalStageId = string.Empty;
                return;
            }

            if (updatedExecution.State == WorkflowExecutionState.Cancelled)
            {
                result.Succeeded = false;
                result.Summary = "Workflow was cancelled during approval";
                throw new OperationCanceledException();
            }
        }

        result.Succeeded = false;
        result.Summary = $"Approval timeout ({stageTimeout.TotalHours:F0} hours)";
    }

    private async Task ExecuteDelayStageAsync(
        WorkflowStageConfig stage,
        WorkflowStageResult result,
        CancellationToken cancellationToken)
    {
        var delaySeconds = stage.DelaySeconds ?? 0;
        if (delaySeconds <= 0)
        {
            result.Succeeded = true;
            result.Summary = "No delay configured, skipped";
            return;
        }

        logger.LogInformation("Stage {StageName} delaying for {DelaySeconds} seconds", stage.Name, delaySeconds);

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            result.Succeeded = true;
            result.Summary = $"Delayed for {delaySeconds} seconds";
        }
        catch (OperationCanceledException)
        {
            result.Succeeded = false;
            result.Summary = "Delay cancelled";
            throw;
        }
    }

    private async Task ExecuteParallelStageAsync(
        WorkflowStageConfig stage,
        WorkflowStageResult result,
        CancellationToken cancellationToken)
    {
        var agentTeamMembers = stage.AgentTeamMembers?
            .Where(member => !string.IsNullOrWhiteSpace(member.Harness))
            .ToList() ?? [];
        if (agentTeamMembers.Count > 0)
        {
            await ExecuteAgentTeamParallelStageAsync(stage, result, agentTeamMembers, cancellationToken);
            return;
        }

        var parallelTaskIds = stage.ParallelStageIds ?? [];
        if (parallelTaskIds.Count == 0)
        {
            result.Succeeded = false;
            result.Summary = "No parallel task IDs specified";
            return;
        }

        logger.LogInformation("Stage {StageName} executing {TaskCount} tasks in parallel", stage.Name, parallelTaskIds.Count);

        var tasks = new List<Task<ParallelRunResult>>();

        foreach (var taskId in parallelTaskIds)
        {
            tasks.Add(ExecuteParallelTaskAsync(taskId, stage, cancellationToken));
        }

        var results = await Task.WhenAll(tasks);

        var allSucceeded = results.All(r => r.Success);
        var summaries = results.Select(r => r.Summary).ToList();
        var runIds = results.Select(r => r.RunId).Where(id => !string.IsNullOrEmpty(id)).ToList();

        result.Succeeded = allSucceeded;
        result.RunIds.AddRange(runIds);
        result.Summary = allSucceeded
            ? $"All {results.Length} parallel tasks succeeded"
            : $"Some parallel tasks failed: {string.Join("; ", summaries)}";
    }

    private async Task ExecuteAgentTeamParallelStageAsync(
        WorkflowStageConfig stage,
        WorkflowStageResult result,
        IReadOnlyList<WorkflowAgentTeamMemberConfig> members,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stage.TaskId))
        {
            result.Succeeded = false;
            result.Summary = "Agent Team stage requires TaskId for baseline task.";
            return;
        }

        var baselineTask = await store.GetTaskAsync(stage.TaskId, cancellationToken);
        if (baselineTask is null)
        {
            result.Succeeded = false;
            result.Summary = $"Baseline task {stage.TaskId} not found.";
            return;
        }

        var repository = await store.GetRepositoryAsync(baselineTask.RepositoryId, cancellationToken);
        if (repository is null)
        {
            result.Succeeded = false;
            result.Summary = $"Repository {baselineTask.RepositoryId} not found.";
            return;
        }

        logger.LogInformation("Stage {StageName} executing Agent Team with {MemberCount} members", stage.Name, members.Count);

        var memberTasks = members
            .Select((member, index) => ExecuteAgentTeamMemberAsync(
                stage,
                baselineTask,
                repository,
                member,
                index,
                cancellationToken))
            .ToList();

        var memberResults = await Task.WhenAll(memberTasks);
        var allMembersSucceeded = memberResults.All(x => x.Success);
        var agentTeamDiff = await BuildAgentTeamDiffResultAsync(memberResults, cancellationToken);

        result.RunIds.AddRange(memberResults.Select(x => x.RunId).Where(id => !string.IsNullOrWhiteSpace(id)));
        result.AgentTeamDiff = agentTeamDiff;
        result.Succeeded = allMembersSucceeded;
        result.Summary = allMembersSucceeded
            ? $"All {members.Count} agent lanes succeeded."
            : $"Agent lane failure: {string.Join("; ", memberResults.Where(x => !x.Success).Select(x => x.Summary))}";

        if (agentTeamDiff.ConflictCount > 0)
        {
            result.Summary = $"{result.Summary} Merge conflicts: {agentTeamDiff.ConflictCount}.";
        }
        else if (agentTeamDiff.MergedFiles > 0)
        {
            result.Summary = $"{result.Summary} Merged files: {agentTeamDiff.MergedFiles}.";
        }

        var synthesis = stage.Synthesis;
        if (agentTeamDiff.ConflictCount > 0 && (synthesis is null || !synthesis.Enabled))
        {
            synthesis = new WorkflowSynthesisStageConfig
            {
                Enabled = true,
                Harness = baselineTask.Harness,
                Mode = HarnessExecutionMode.Review,
                Prompt = "Resolve the conflicting lane edits. Produce a final patch recommendation that reconciles conflicts and preserves valid non-conflicting changes.",
                ModelOverride = string.Empty,
                TimeoutSeconds = null,
            };
        }

        if (!allMembersSucceeded || synthesis is null || !synthesis.Enabled)
        {
            return;
        }

        var synthesisResult = await ExecuteAgentTeamSynthesisAsync(
            stage,
            baselineTask,
            repository,
            synthesis,
            memberResults,
            agentTeamDiff,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(synthesisResult.RunId))
        {
            result.RunIds.Add(synthesisResult.RunId);
        }

        if (synthesisResult.Success)
        {
            result.Summary = $"{result.Summary} Synthesis completed.";
            return;
        }

        result.Succeeded = false;
        result.Summary = $"{result.Summary} Synthesis failed: {synthesisResult.Summary}";
    }

    private async Task<ParallelRunResult> ExecuteParallelTaskAsync(
        string taskId,
        WorkflowStageConfig stage,
        CancellationToken cancellationToken)
    {
        var task = await store.GetTaskAsync(taskId, cancellationToken);
        if (task is null)
        {
            return new ParallelRunResult(false, $"Task {taskId} not found", string.Empty, taskId, string.Empty);
        }

        var repository = await store.GetRepositoryAsync(task.RepositoryId, cancellationToken);
        if (repository is null)
        {
            return new ParallelRunResult(false, $"Repository {task.RepositoryId} not found", string.Empty, taskId, task.Harness);
        }

        return await DispatchAndAwaitRunAsync(
            repository,
            task,
            stage,
            cancellationToken,
            laneLabel: taskId,
            executionModeOverride: null);
    }

    private async Task<ParallelRunResult> ExecuteAgentTeamMemberAsync(
        WorkflowStageConfig stage,
        TaskDocument baselineTask,
        RepositoryDocument repository,
        WorkflowAgentTeamMemberConfig member,
        int index,
        CancellationToken cancellationToken)
    {
        var laneTask = CloneTaskForLane(baselineTask, $"team-{index + 1}");
        laneTask.Harness = NormalizeHarness(member.Harness, baselineTask.Harness);
        laneTask.ExecutionModeDefault = member.Mode;
        laneTask.Prompt = BuildAgentLanePrompt(stage, baselineTask, member);

        if (!string.IsNullOrWhiteSpace(stage.CommandOverride))
        {
            laneTask.Command = stage.CommandOverride.Trim();
        }

        if (member.TimeoutSeconds is > 0)
        {
            laneTask.Timeouts = new TimeoutConfig(
                ExecutionSeconds: member.TimeoutSeconds.Value,
                OverallSeconds: Math.Max(member.TimeoutSeconds.Value, baselineTask.Timeouts.OverallSeconds));
        }

        if (!string.IsNullOrWhiteSpace(member.ModelOverride))
        {
            UpsertInstructionFile(laneTask, "model-override", member.ModelOverride.Trim());
        }

        return await DispatchAndAwaitRunAsync(
            repository,
            laneTask,
            stage,
            cancellationToken,
            laneLabel: string.IsNullOrWhiteSpace(member.Name) ? $"agent-{index + 1}" : member.Name,
            executionModeOverride: member.Mode);
    }

    private async Task<ParallelRunResult> ExecuteAgentTeamSynthesisAsync(
        WorkflowStageConfig stage,
        TaskDocument baselineTask,
        RepositoryDocument repository,
        WorkflowSynthesisStageConfig synthesis,
        IReadOnlyList<ParallelRunResult> laneResults,
        WorkflowAgentTeamDiffResult? diffResult,
        CancellationToken cancellationToken)
    {
        var synthesisTask = CloneTaskForLane(baselineTask, "synthesis");
        synthesisTask.Harness = NormalizeHarness(synthesis.Harness, baselineTask.Harness);
        synthesisTask.ExecutionModeDefault = synthesis.Mode;
        synthesisTask.Prompt = BuildSynthesisPrompt(stage, baselineTask, synthesis, laneResults, diffResult);

        if (!string.IsNullOrWhiteSpace(stage.CommandOverride))
        {
            synthesisTask.Command = stage.CommandOverride.Trim();
        }

        if (synthesis.TimeoutSeconds is > 0)
        {
            synthesisTask.Timeouts = new TimeoutConfig(
                ExecutionSeconds: synthesis.TimeoutSeconds.Value,
                OverallSeconds: Math.Max(synthesis.TimeoutSeconds.Value, baselineTask.Timeouts.OverallSeconds));
        }

        if (!string.IsNullOrWhiteSpace(synthesis.ModelOverride))
        {
            UpsertInstructionFile(synthesisTask, "model-override", synthesis.ModelOverride.Trim());
        }

        return await DispatchAndAwaitRunAsync(
            repository,
            synthesisTask,
            stage,
            cancellationToken,
            laneLabel: "synthesis",
            executionModeOverride: synthesis.Mode);
    }

    private async Task<ParallelRunResult> DispatchAndAwaitRunAsync(
        RepositoryDocument repository,
        TaskDocument dispatchTask,
        WorkflowStageConfig stage,
        CancellationToken cancellationToken,
        string laneLabel,
        HarnessExecutionMode? executionModeOverride)
    {
        var run = await store.CreateRunAsync(
            dispatchTask,
            cancellationToken,
            executionModeOverride: executionModeOverride);
        logger.LogInformation("Created run {RunId} for workflow lane {LaneLabel}", run.Id, laneLabel);

        var dispatched = await dispatcher.DispatchAsync(repository, dispatchTask, run, cancellationToken);
        if (!dispatched)
        {
            logger.LogInformation(
                "Run {RunId} for workflow lane {LaneLabel} was queued; waiting for terminal state",
                run.Id,
                laneLabel);
        }

        var pollingInterval = TimeSpan.FromSeconds(2);
        var stageTimeout = GetStageTimeout(stage);
        var startTime = _timeProvider.GetUtcNow().UtcDateTime;

        while (_timeProvider.GetUtcNow().UtcDateTime - startTime < stageTimeout)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return new ParallelRunResult(false, "Cancelled", run.Id, laneLabel, dispatchTask.Harness);
            }

            await Task.Delay(pollingInterval, cancellationToken);

            var updatedRun = await store.GetRunAsync(run.Id, cancellationToken);
            if (updatedRun is null)
            {
                return new ParallelRunResult(false, "Run disappeared", run.Id, laneLabel, dispatchTask.Harness);
            }

            if (updatedRun.State == RunState.Succeeded)
            {
                return new ParallelRunResult(true, updatedRun.Summary, run.Id, laneLabel, dispatchTask.Harness);
            }

            if (updatedRun.State == RunState.Failed)
            {
                return new ParallelRunResult(false, $"Run failed: {updatedRun.Summary}", run.Id, laneLabel, dispatchTask.Harness);
            }

            if (updatedRun.State == RunState.Cancelled)
            {
                return new ParallelRunResult(false, "Run cancelled", run.Id, laneLabel, dispatchTask.Harness);
            }
        }

        return new ParallelRunResult(false, "Timeout waiting for run", run.Id, laneLabel, dispatchTask.Harness);
    }

    private static TaskDocument CloneTaskForLane(TaskDocument source, string laneSuffix)
    {
        return new TaskDocument
        {
            Id = $"{source.Id}-{laneSuffix}-{Guid.NewGuid():N}",
            RepositoryId = source.RepositoryId,
            Name = source.Name,
            Kind = source.Kind,
            Harness = source.Harness,
            ExecutionModeDefault = source.ExecutionModeDefault,
            Prompt = source.Prompt,
            Command = source.Command,
            AutoCreatePullRequest = source.AutoCreatePullRequest,
            CronExpression = source.CronExpression,
            Enabled = source.Enabled,
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

    private static string BuildAgentLanePrompt(
        WorkflowStageConfig stage,
        TaskDocument baselineTask,
        WorkflowAgentTeamMemberConfig member)
    {
        var basePrompt = string.IsNullOrWhiteSpace(stage.PromptOverride)
            ? baselineTask.Prompt
            : stage.PromptOverride.Trim();
        var rolePrompt = member.RolePrompt?.Trim() ?? string.Empty;

        if (rolePrompt.Length == 0)
        {
            return basePrompt;
        }

        var builder = new StringBuilder();
        builder.AppendLine(basePrompt.Trim());
        builder.AppendLine();
        builder.AppendLine("Agent role instructions:");
        builder.AppendLine(rolePrompt);
        return builder.ToString();
    }

    private static string BuildSynthesisPrompt(
        WorkflowStageConfig stage,
        TaskDocument baselineTask,
        WorkflowSynthesisStageConfig synthesis,
        IReadOnlyList<ParallelRunResult> laneResults,
        WorkflowAgentTeamDiffResult? diffResult)
    {
        var basePrompt = synthesis.Prompt.Trim();
        if (basePrompt.Length == 0)
        {
            basePrompt = "Synthesize all lane outputs into one final response.";
        }

        var builder = new StringBuilder();
        builder.AppendLine(basePrompt);
        builder.AppendLine();
        builder.AppendLine("Lane outputs:");

        foreach (var lane in laneResults)
        {
            builder.Append("- ");
            builder.Append(lane.LaneLabel);
            builder.Append(" [");
            builder.Append(lane.Success ? "succeeded" : "failed");
            builder.Append("] Run ");
            builder.Append(lane.RunId);
            builder.Append(": ");
            builder.AppendLine(lane.Summary);
        }

        if (diffResult is not null)
        {
            builder.AppendLine();
            builder.Append("Merge result: ");
            builder.Append(diffResult.MergedFiles);
            builder.Append(" merged file(s), ");
            builder.Append(diffResult.ConflictCount);
            builder.AppendLine(" conflict(s).");

            if (diffResult.LaneDiffs.Count > 0)
            {
                builder.AppendLine("Lane diff summaries:");
                foreach (var laneDiff in diffResult.LaneDiffs)
                {
                    builder.Append("- ");
                    builder.Append(laneDiff.LaneLabel);
                    builder.Append(" Run ");
                    builder.Append(laneDiff.RunId);
                    builder.Append(": ");
                    builder.Append(laneDiff.FilesChanged);
                    builder.Append(" file(s), +");
                    builder.Append(laneDiff.Additions);
                    builder.Append(" / -");
                    builder.Append(laneDiff.Deletions);
                    if (!string.IsNullOrWhiteSpace(laneDiff.DiffStat))
                    {
                        builder.Append(" (");
                        builder.Append(laneDiff.DiffStat);
                        builder.Append(')');
                    }
                    builder.AppendLine();
                }
            }

            if (diffResult.Conflicts.Count > 0)
            {
                builder.AppendLine("Conflicts:");
                foreach (var conflict in diffResult.Conflicts)
                {
                    builder.Append("- File ");
                    builder.Append(conflict.FilePath);
                    builder.Append(": ");
                    builder.Append(conflict.Reason);
                    if (conflict.LaneLabels.Count > 0)
                    {
                        builder.Append(" [");
                        builder.Append(string.Join(", ", conflict.LaneLabels));
                        builder.Append(']');
                    }
                    if (conflict.HunkHeaders.Count > 0)
                    {
                        builder.Append(" hunks: ");
                        builder.Append(string.Join(" | ", conflict.HunkHeaders));
                    }
                    builder.AppendLine();
                }
            }

            if (!string.IsNullOrWhiteSpace(diffResult.MergedPatch))
            {
                builder.AppendLine();
                builder.AppendLine("Merged non-conflicting patch:");
                builder.AppendLine(diffResult.MergedPatch.Trim());
            }
        }

        if (!string.IsNullOrWhiteSpace(stage.PromptOverride))
        {
            builder.AppendLine();
            builder.AppendLine("Original task prompt:");
            builder.AppendLine(stage.PromptOverride.Trim());
        }
        else if (!string.IsNullOrWhiteSpace(baselineTask.Prompt))
        {
            builder.AppendLine();
            builder.AppendLine("Original task prompt:");
            builder.AppendLine(baselineTask.Prompt.Trim());
        }

        return builder.ToString();
    }

    private async Task<WorkflowAgentTeamDiffResult> BuildAgentTeamDiffResultAsync(
        IReadOnlyList<ParallelRunResult> laneResults,
        CancellationToken cancellationToken)
    {
        if (laneResults.Count == 0)
        {
            return new WorkflowAgentTeamDiffResult();
        }

        var laneDiffTasks = laneResults.Select(async lane =>
        {
            var runId = lane.RunId ?? string.Empty;
            if (string.IsNullOrWhiteSpace(runId))
            {
                return new AgentTeamLaneDiffInput(
                    LaneLabel: lane.LaneLabel,
                    Harness: lane.Harness,
                    RunId: string.Empty,
                    Succeeded: lane.Success,
                    Summary: lane.Summary,
                    DiffStat: string.Empty,
                    DiffPatch: string.Empty);
            }

            var diff = await store.GetLatestRunDiffSnapshotAsync(runId, cancellationToken);
            return new AgentTeamLaneDiffInput(
                LaneLabel: lane.LaneLabel,
                Harness: lane.Harness,
                RunId: runId,
                Succeeded: lane.Success,
                Summary: lane.Summary,
                DiffStat: diff?.DiffStat ?? string.Empty,
                DiffPatch: diff?.DiffPatch ?? string.Empty);
        });

        var laneDiffs = await Task.WhenAll(laneDiffTasks);
        return AgentTeamDiffMergeService.Build(laneDiffs);
    }

    private static string NormalizeHarness(string harness, string fallback)
    {
        if (!string.IsNullOrWhiteSpace(harness))
        {
            return harness.Trim().ToLowerInvariant();
        }

        return string.IsNullOrWhiteSpace(fallback) ? "codex" : fallback.Trim().ToLowerInvariant();
    }

    private static void UpsertInstructionFile(TaskDocument task, string name, string content)
    {
        var existing = task.InstructionFiles.FindIndex(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        var instruction = new InstructionFile(name, content, Order: 0);
        if (existing >= 0)
        {
            task.InstructionFiles[existing] = instruction;
            return;
        }

        task.InstructionFiles.Add(instruction);
    }

    private sealed record ParallelRunResult(bool Success, string Summary, string RunId, string LaneLabel, string Harness);

    public virtual async Task<WorkflowExecutionDocument?> ApproveWorkflowStageAsync(
        string executionId,
        string approvedBy,
        CancellationToken cancellationToken)
    {
        return await store.ApproveWorkflowStageAsync(executionId, approvedBy, cancellationToken);
    }
}
