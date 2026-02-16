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
    ILogger<WorkflowExecutor> logger) : IWorkflowExecutor
{
    private readonly StageTimeoutConfig _timeoutConfig = options.Value.StageTimeout;

    public virtual async Task<WorkflowExecutionDocument> ExecuteWorkflowAsync(
        WorkflowDocument workflow,
        string projectId,
        CancellationToken cancellationToken)
    {
        var execution = new WorkflowExecutionDocument
        {
            WorkflowId = workflow.Id,
            RepositoryId = workflow.RepositoryId,
            ProjectId = projectId,
            State = WorkflowExecutionState.Running,
            CurrentStageIndex = 0,
            StartedAtUtc = DateTime.UtcNow
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
                StartedAtUtc = DateTime.UtcNow
            };

            var stageTimeout = GetStageTimeout(stage);
            stageTimeout = stageTimeout > maxStageTimeout ? maxStageTimeout : stageTimeout;

            using var stageCts = new CancellationTokenSource(stageTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, stageCts.Token);

            try
            {
                await ExecuteStageWithTimeoutAsync(
                    stage, stageResult, execution, projectId: execution.ProjectId, linkedCts.Token, stageCts);

                stageResult.EndedAtUtc = DateTime.UtcNow;
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
                stageResult.EndedAtUtc = DateTime.UtcNow;
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
                stageResult.EndedAtUtc = DateTime.UtcNow;
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
        string projectId,
        CancellationToken cancellationToken,
        CancellationTokenSource timeoutCts)
    {
        switch (stage.Type)
        {
            case WorkflowStageType.Task:
                await ExecuteTaskStageAsync(stage, result, projectId, cancellationToken);
                break;

            case WorkflowStageType.Approval:
                await ExecuteApprovalStageAsync(stage, result, execution, cancellationToken);
                break;

            case WorkflowStageType.Delay:
                await ExecuteDelayStageAsync(stage, result, cancellationToken);
                break;

            case WorkflowStageType.Parallel:
                await ExecuteParallelStageAsync(stage, result, projectId, cancellationToken);
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
        string projectId,
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

        var project = await store.GetProjectAsync(repository.ProjectId, cancellationToken);
        if (project is null)
        {
            result.Succeeded = false;
            result.Summary = $"Project {repository.ProjectId} not found";
            return;
        }

        var run = await store.CreateRunAsync(task, project.Id, cancellationToken);
        result.RunIds.Add(run.Id);

        logger.LogInformation("Created run {RunId} for task stage {StageName} (task {TaskId})",
            run.Id, stage.Name, task.Id);

        var dispatched = await dispatcher.DispatchAsync(project, repository, task, run, cancellationToken);
        if (!dispatched)
        {
            result.Succeeded = false;
            result.Summary = "Failed to dispatch run (concurrency limit or worker unavailable)";
            return;
        }

        var pollingInterval = TimeSpan.FromSeconds(2);
        var stageTimeout = GetStageTimeout(stage);
        var startTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < stageTimeout)
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
        logger.LogInformation("Stage {StageName} requires approval",
            stage.Name);

        await store.MarkWorkflowExecutionPendingApprovalAsync(execution.Id, stage.Id, cancellationToken);

        var stageTimeout = GetStageTimeout(stage);
        var pollingInterval = TimeSpan.FromSeconds(5);
        var startTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < stageTimeout)
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
        string projectId,
        CancellationToken cancellationToken)
    {
        var parallelTaskIds = stage.ParallelStageIds ?? [];
        if (parallelTaskIds.Count == 0)
        {
            result.Succeeded = false;
            result.Summary = "No parallel task IDs specified";
            return;
        }

        logger.LogInformation("Stage {StageName} executing {TaskCount} tasks in parallel",
            stage.Name, parallelTaskIds.Count);

        var tasks = new List<Task<(bool Success, string Summary, string RunId)>>();

        foreach (var taskId in parallelTaskIds)
        {
            tasks.Add(ExecuteParallelTaskAsync(taskId, projectId, stage, cancellationToken));
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

    private async Task<(bool Success, string Summary, string RunId)> ExecuteParallelTaskAsync(
        string taskId,
        string projectId,
        WorkflowStageConfig stage,
        CancellationToken cancellationToken)
    {
        var task = await store.GetTaskAsync(taskId, cancellationToken);
        if (task is null)
        {
            return (false, $"Task {taskId} not found", string.Empty);
        }

        var repository = await store.GetRepositoryAsync(task.RepositoryId, cancellationToken);
        if (repository is null)
        {
            return (false, $"Repository {task.RepositoryId} not found", string.Empty);
        }

        var project = await store.GetProjectAsync(repository.ProjectId, cancellationToken);
        if (project is null)
        {
            return (false, $"Project {repository.ProjectId} not found", string.Empty);
        }

        var run = await store.CreateRunAsync(task, project.Id, cancellationToken);
        logger.LogInformation("Created run {RunId} for parallel task {TaskId}", run.Id, taskId);

        var dispatched = await dispatcher.DispatchAsync(project, repository, task, run, cancellationToken);
        if (!dispatched)
        {
            return (false, "Failed to dispatch run", run.Id);
        }

        var pollingInterval = TimeSpan.FromSeconds(2);
        var stageTimeout = GetStageTimeout(stage);
        var startTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < stageTimeout)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return (false, "Cancelled", run.Id);
            }

            await Task.Delay(pollingInterval, cancellationToken);

            var updatedRun = await store.GetRunAsync(run.Id, cancellationToken);
            if (updatedRun is null)
            {
                return (false, "Run disappeared", run.Id);
            }

            if (updatedRun.State == RunState.Succeeded)
            {
                return (true, updatedRun.Summary, run.Id);
            }

            if (updatedRun.State == RunState.Failed)
            {
                return (false, $"Run failed: {updatedRun.Summary}", run.Id);
            }

            if (updatedRun.State == RunState.Cancelled)
            {
                return (false, "Run cancelled", run.Id);
            }
        }

        return (false, "Timeout waiting for run", run.Id);
    }

    public virtual async Task<WorkflowExecutionDocument?> ApproveWorkflowStageAsync(
        string executionId,
        string approvedBy,
        CancellationToken cancellationToken)
    {
        return await store.ApproveWorkflowStageAsync(executionId, approvedBy, cancellationToken);
    }
}
