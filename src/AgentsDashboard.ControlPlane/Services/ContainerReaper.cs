using AgentsDashboard.Contracts.Worker;
using Microsoft.Extensions.Logging;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class ContainerReaper(
    WorkerGateway.WorkerGatewayClient workerClient,
    ILogger<ContainerReaper> logger) : IContainerReaper
{
    public async Task<ContainerKillResult> KillContainerAsync(string runId, string reason, bool force, CancellationToken cancellationToken)
    {
        logger.LogWarning("Requesting container kill for run {RunId}. Reason: {Reason}, Force: {Force}", runId, reason, force);

        try
        {
            var request = new KillContainerRequest
            {
                RunId = runId,
                Reason = reason,
                Force = force
            };

            var response = await workerClient.KillContainerAsync(request, cancellationToken: cancellationToken);

            if (response.Killed)
            {
                logger.LogInformation("Successfully killed container {ContainerId} for run {RunId}", response.ContainerId, runId);
            }
            else
            {
                logger.LogWarning("Failed to kill container for run {RunId}: {Error}", runId, response.Error);
            }

            return new ContainerKillResult
            {
                Killed = response.Killed,
                ContainerId = response.ContainerId,
                Error = response.Error
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception while killing container for run {RunId}", runId);
            return new ContainerKillResult
            {
                Killed = false,
                Error = ex.Message
            };
        }
    }

    public async Task<int> ReapOrphanedContainersAsync(IEnumerable<string> activeRunIds, CancellationToken cancellationToken)
    {
        logger.LogInformation("Scanning for orphaned containers with {Count} active run IDs", activeRunIds.Count());

        try
        {
            var request = new ReconcileOrphanedContainersRequest();
            request.ActiveRunIds.AddRange(activeRunIds);

            var response = await workerClient.ReconcileOrphanedContainersAsync(request, cancellationToken: cancellationToken);

            if (response.OrphanedCount > 0)
            {
                logger.LogWarning("Removed {Count} orphaned containers", response.OrphanedCount);
                foreach (var container in response.RemovedContainers)
                {
                    logger.LogInformation("Removed orphaned container {ContainerId} for run {RunId}",
                        container.ContainerId, container.RunId);
                }
            }

            return response.OrphanedCount;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during container reaping");
            return 0;
        }
    }
}
