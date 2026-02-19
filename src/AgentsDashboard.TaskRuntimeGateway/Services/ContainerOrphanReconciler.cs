using System.Diagnostics;
using System.Diagnostics.Metrics;
using AgentsDashboard.TaskRuntimeGateway.Models;
using AgentsDashboard.TaskRuntimeGateway.Services;

namespace AgentsDashboard.TaskRuntimeGateway.Services;

public interface IContainerOrphanReconciler
{
    Task<OrphanReconciliationResult> ReconcileAsync(IEnumerable<string> activeRunIds, CancellationToken cancellationToken);
}



public sealed record OrphanReconciliationResult(
    int OrphanedCount,
    IReadOnlyList<OrphanedContainer> RemovedContainers);
