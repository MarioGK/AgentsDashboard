using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class RecoveryService(
    OrchestratorStore store,
    IRunEventPublisher publisher,
    ILogger<RecoveryService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Recovery service starting — checking for orphaned runs and workflows");

        await RecoverOrphanedRunsAsync(cancellationToken);
        await RecoverOrphanedWorkflowExecutionsAsync(cancellationToken);
        await LogPendingApprovalRunsAsync(cancellationToken);
        await LogQueuedRunsAsync(cancellationToken);
    }

    private async Task RecoverOrphanedRunsAsync(CancellationToken cancellationToken)
    {
        var runningRuns = await store.ListRunsByStateAsync(RunState.Running, cancellationToken);
        var recoveredCount = 0;

        foreach (var run in runningRuns)
        {
            try
            {
                logger.LogWarning("Orphaned running run detected: {RunId}. Marking as failed", run.Id);

                var failed = await store.MarkRunCompletedAsync(
                    run.Id,
                    succeeded: false,
                    summary: "Orphaned run recovered on startup",
                    outputJson: "{}",
                    cancellationToken,
                    failureClass: "OrphanRecovery");

                if (failed is not null)
                {
                    await publisher.PublishStatusAsync(failed, cancellationToken);
                    await store.CreateFindingFromFailureAsync(failed, "Run was still in Running state when control plane restarted. Marked as failed.", cancellationToken);
                    recoveredCount++;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to recover orphaned run {RunId}", run.Id);
            }
        }

        if (recoveredCount > 0)
            logger.LogInformation("Recovery complete: {Count} orphaned runs marked as failed", recoveredCount);
    }

    private async Task RecoverOrphanedWorkflowExecutionsAsync(CancellationToken cancellationToken)
    {
        var runningExecutions = await store.ListWorkflowExecutionsByStateAsync(WorkflowExecutionState.Running, cancellationToken);
        var recoveredCount = 0;

        foreach (var execution in runningExecutions)
        {
            try
            {
                logger.LogWarning("Orphaned workflow execution detected: {ExecutionId} (Workflow: {WorkflowId}). Marking as failed",
                    execution.Id, execution.WorkflowId);

                var failed = await store.MarkWorkflowExecutionCompletedAsync(
                    execution.Id,
                    WorkflowExecutionState.Failed,
                    "Workflow execution was still running when control plane restarted. Marked as failed.",
                    cancellationToken);

                if (failed is not null)
                {
                    recoveredCount++;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to recover orphaned workflow execution {ExecutionId}", execution.Id);
            }
        }

        if (recoveredCount > 0)
            logger.LogInformation("Recovery complete: {Count} orphaned workflow executions marked as failed", recoveredCount);
    }

    private async Task LogPendingApprovalRunsAsync(CancellationToken cancellationToken)
    {
        var pendingRuns = await store.ListRunsByStateAsync(RunState.PendingApproval, cancellationToken);
        if (pendingRuns.Count > 0)
        {
            logger.LogWarning("Found {Count} runs pending approval after restart. These require manual action.", pendingRuns.Count);
            foreach (var run in pendingRuns)
            {
                logger.LogInformation("Run {RunId} is pending approval (Task: {TaskId})", run.Id, run.TaskId);
            }
        }
    }

    private async Task LogQueuedRunsAsync(CancellationToken cancellationToken)
    {
        var queuedRuns = await store.ListRunsByStateAsync(RunState.Queued, cancellationToken);
        if (queuedRuns.Count > 0)
            logger.LogInformation("Recovery found {Count} queued runs that will be picked up by the scheduler", queuedRuns.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
