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
        logger.LogInformation("Recovery service starting — checking for orphaned runs");

        var runningRuns = await store.ListRunsByStateAsync(RunState.Running, cancellationToken);

        foreach (var run in runningRuns)
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
            }
        }

        if (runningRuns.Count > 0)
            logger.LogInformation("Recovery complete: {Count} orphaned runs marked as failed", runningRuns.Count);

        // Re-queue any queued runs that need dispatching — the scheduler will pick them up
        var queuedRuns = await store.ListRunsByStateAsync(RunState.Queued, cancellationToken);
        if (queuedRuns.Count > 0)
            logger.LogInformation("Recovery found {Count} queued runs that will be picked up by the scheduler", queuedRuns.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
