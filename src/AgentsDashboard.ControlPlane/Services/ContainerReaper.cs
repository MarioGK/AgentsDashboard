using AgentsDashboard.Contracts.Worker;
using Microsoft.Extensions.Logging;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class ContainerReaper(
    IMagicOnionClientFactory clientFactory,
    ILogger<ContainerReaper> logger) : IContainerReaper
{
    public async Task<ContainerKillResult> KillContainerAsync(string runId, string reason, bool force, CancellationToken cancellationToken)
    {
        logger.LogWarning("Requesting container kill for run {RunId}. Reason: {Reason}, Force: {Force}", runId, reason, force);

        try
        {
            var client = clientFactory.CreateWorkerGatewayService();

            var request = new KillContainerRequest
            {
                ContainerId = runId
            };

            var response = await client.KillContainerAsync(request);

            if (response.Success)
            {
                logger.LogInformation("Successfully killed container for run {RunId}. WasRunning: {WasRunning}", runId, response.WasRunning);
            }
            else
            {
                logger.LogWarning("Failed to kill container for run {RunId}: {Error}", runId, response.ErrorMessage);
            }

            return new ContainerKillResult
            {
                Killed = response.Success,
                ContainerId = runId,
                Error = response.ErrorMessage ?? string.Empty
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
            var client = clientFactory.CreateWorkerGatewayService();

            var request = new ReconcileOrphanedContainersRequest
            {
                WorkerId = "control-plane"
            };

            var response = await client.ReconcileOrphanedContainersAsync(request);

            if (response.Success && response.ReconciledCount > 0)
            {
                logger.LogWarning("Removed {Count} orphaned containers", response.ReconciledCount);
                if (response.ContainerIds != null)
                {
                    foreach (var containerId in response.ContainerIds)
                    {
                        logger.LogInformation("Removed orphaned container {ContainerId}", containerId);
                    }
                }
            }
            else if (!response.Success)
            {
                logger.LogWarning("Reconciliation failed: {Error}", response.ErrorMessage);
            }

            return response.ReconciledCount;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during container reaping");
            return 0;
        }
    }
}
