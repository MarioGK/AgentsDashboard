using System.Diagnostics;
using System.Diagnostics.Metrics;
using AgentsDashboard.WorkerGateway.Models;
using AgentsDashboard.WorkerGateway.Services;

namespace AgentsDashboard.WorkerGateway.Services;

public interface IContainerOrphanReconciler
{
    Task<OrphanReconciliationResult> ReconcileAsync(IEnumerable<string> activeRunIds, CancellationToken cancellationToken);
}

public sealed record OrphanReconciliationResult(
    int OrphanedCount,
    IReadOnlyList<OrphanedContainer> RemovedContainers);

public sealed record OrphanedContainer(string ContainerId, string RunId);

public sealed class ContainerOrphanReconciler(
    DockerContainerService dockerService,
    ILogger<ContainerOrphanReconciler> logger) : IContainerOrphanReconciler
{
    private static readonly Meter s_meter = new("AgentsDashboard.WorkerGateway.OrphanReconciliation");
    private static readonly Counter<int> s_orphansDetected = s_meter.CreateCounter<int>("orphans_detected_count", "containers");
    private static readonly Counter<int> s_orphansRemoved = s_meter.CreateCounter<int>("orphans_removed_count", "containers");

    public async Task<OrphanReconciliationResult> ReconcileAsync(IEnumerable<string> activeRunIds, CancellationToken cancellationToken)
    {
        var activeRunIdSet = new HashSet<string>(activeRunIds, StringComparer.OrdinalIgnoreCase);
        var containers = await dockerService.ListOrchestratorContainersAsync(cancellationToken);

        var orphanedContainers = containers
            .Where(c => !activeRunIdSet.Contains(c.RunId))
            .ToList();

        if (orphanedContainers.Count == 0)
        {
            logger.ZLogInformation("No orphaned containers found");
            return new OrphanReconciliationResult(0, []);
        }

        s_orphansDetected.Add(orphanedContainers.Count);
        logger.ZLogWarning("Found {Count} orphaned containers with orchestrator labels but no matching run", orphanedContainers.Count);

        var removedContainers = new List<OrphanedContainer>();

        foreach (var orphan in orphanedContainers)
        {
            logger.ZLogWarning(
                "Removing orphaned container {ContainerId} (RunId: {RunId}, State: {State})",
                orphan.ContainerId[..Math.Min(12, orphan.ContainerId.Length)],
                orphan.RunId,
                orphan.State);

            var removed = await dockerService.RemoveContainerForceAsync(orphan.ContainerId, cancellationToken);
            if (removed)
            {
                removedContainers.Add(new OrphanedContainer(orphan.ContainerId, orphan.RunId));
            }
        }

        s_orphansRemoved.Add(removedContainers.Count);
        logger.ZLogInformation("Reconciliation complete: removed {RemovedCount}/{DetectedCount} orphaned containers",
            removedContainers.Count, orphanedContainers.Count);

        return new OrphanReconciliationResult(orphanedContainers.Count, removedContainers);
    }
}
