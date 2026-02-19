using System.Diagnostics;
using System.Diagnostics.Metrics;
using AgentsDashboard.TaskRuntimeGateway.Models;
using AgentsDashboard.TaskRuntimeGateway.Services;

namespace AgentsDashboard.TaskRuntimeGateway.Services;

public interface IContainerOrphanReconciler
{
    Task<OrphanReconciliationResult> ReconcileAsync(IEnumerable<string> activeRunIds, CancellationToken cancellationToken);
}



public sealed class ContainerOrphanReconciler(
    DockerContainerService dockerService,
    ILogger<ContainerOrphanReconciler> logger) : IContainerOrphanReconciler
{
    private static readonly Meter s_meter = new("AgentsDashboard.TaskRuntimeGateway.OrphanReconciliation");
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
            logger.LogInformation("No orphaned containers found");
            return new OrphanReconciliationResult(0, []);
        }

        s_orphansDetected.Add(orphanedContainers.Count);
        logger.LogWarning("Found {Count} orphaned containers with orchestrator labels but no matching run", orphanedContainers.Count);

        var removedContainers = new List<OrphanedContainer>();

        foreach (var orphan in orphanedContainers)
        {
            logger.LogWarning(
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
        logger.LogInformation("Reconciliation complete: removed {RemovedCount}/{DetectedCount} orphaned containers",
            removedContainers.Count, orphanedContainers.Count);

        return new OrphanReconciliationResult(orphanedContainers.Count, removedContainers);
    }
}
