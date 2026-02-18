using AgentsDashboard.Contracts.TaskRuntime;
using AgentsDashboard.TaskRuntimeGateway.Models;
using AgentsDashboard.TaskRuntimeGateway.Services;
using MagicOnion;
using MagicOnion.Server;

namespace AgentsDashboard.TaskRuntimeGateway.MagicOnion;

/// <summary>
/// MagicOnion unary service for worker gateway operations.
/// Replaces gRPC WorkerGateway service.
/// </summary>
public sealed class TaskRuntimeGatewayService(
    ITaskRuntimeQueue queue,
    IContainerOrphanReconciler orphanReconciler,
    IDockerContainerService dockerService,
    TaskRuntimeHarnessToolHealthService harnessToolHealthService,
    ILogger<TaskRuntimeGatewayService> logger)
    : ServiceBase<ITaskRuntimeGatewayService>, ITaskRuntimeGatewayService
{
    public async UnaryResult<DispatchJobReply> DispatchJobAsync(DispatchJobRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RunId))
        {
            return new DispatchJobReply
            {
                Success = false,
                ErrorMessage = "run_id is required",
                DispatchedAt = DateTimeOffset.UtcNow
            };
        }

        if (!queue.CanAcceptJob())
        {
            return new DispatchJobReply
            {
                Success = false,
                ErrorMessage = "worker at capacity",
                DispatchedAt = DateTimeOffset.UtcNow
            };
        }

        await queue.EnqueueAsync(new QueuedJob { Request = request }, CancellationToken.None);

        logger.ZLogInformation("Accepted run {RunId} using harness {Harness}", request.RunId, request.HarnessType);

        return new DispatchJobReply
        {
            Success = true,
            ErrorMessage = null,
            DispatchedAt = DateTimeOffset.UtcNow
        };
    }

    public UnaryResult<CancelJobReply> CancelJobAsync(CancelJobRequest request)
    {
        var accepted = queue.Cancel(request.RunId);

        return new UnaryResult<CancelJobReply>(new CancelJobReply
        {
            Success = accepted,
            ErrorMessage = accepted ? null : $"Run {request.RunId} not found or already completed"
        });
    }

    public async UnaryResult<KillContainerReply> KillContainerAsync(KillContainerRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ContainerId))
        {
            return new KillContainerReply
            {
                Success = false,
                ErrorMessage = "container_id is required",
                WasRunning = false
            };
        }

        logger.ZLogWarning("KillContainer request received for container {ContainerId}",
            request.ContainerId);

        try
        {
            var removed = await dockerService.RemoveContainerForceAsync(
                request.ContainerId,
                CancellationToken.None);

            return new KillContainerReply
            {
                Success = removed,
                ErrorMessage = removed ? null : $"Failed to remove container {request.ContainerId}",
                WasRunning = removed
            };
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, "Error killing container {ContainerId}", request.ContainerId);

            return new KillContainerReply
            {
                Success = false,
                ErrorMessage = ex.Message,
                WasRunning = false
            };
        }
    }

    public UnaryResult<HeartbeatReply> HeartbeatAsync(HeartbeatRequest request)
    {
        logger.ZLogDebug("Heartbeat received from worker {TaskRuntimeId} on {HostName}: {ActiveSlots}/{MaxSlots} slots",
            request.TaskRuntimeId, request.HostName, request.ActiveSlots, request.MaxSlots);

        return new UnaryResult<HeartbeatReply>(new HeartbeatReply
        {
            Success = true,
            ErrorMessage = null
        });
    }

    public async UnaryResult<ReconcileOrphanedContainersReply> ReconcileOrphanedContainersAsync(
        ReconcileOrphanedContainersRequest request)
    {
        logger.ZLogInformation("Received orphan reconciliation request from worker {TaskRuntimeId}",
            request.TaskRuntimeId);

        try
        {
            var activeRunIds = queue.ActiveRunIds;

            var result = await orphanReconciler.ReconcileAsync(activeRunIds, CancellationToken.None);

            logger.ZLogInformation("Orphan reconciliation complete for worker {TaskRuntimeId}: {OrphanedCount} found, {RemovedCount} removed",
                request.TaskRuntimeId, result.OrphanedCount, result.RemovedContainers.Count);

            return new ReconcileOrphanedContainersReply
            {
                Success = true,
                ErrorMessage = null,
                ReconciledCount = result.OrphanedCount,
                ContainerIds = result.RemovedContainers.Select(c => c.ContainerId).ToList()
            };
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, "Error during orphan reconciliation for worker {TaskRuntimeId}", request.TaskRuntimeId);

            return new ReconcileOrphanedContainersReply
            {
                Success = false,
                ErrorMessage = ex.Message,
                ReconciledCount = 0,
                ContainerIds = null
            };
        }
    }

    public async UnaryResult<GetHarnessToolsReply> GetHarnessToolsAsync(GetHarnessToolsRequest request)
    {
        try
        {
            var tools = await harnessToolHealthService.GetHarnessToolsAsync(CancellationToken.None);

            return new GetHarnessToolsReply
            {
                Success = true,
                ErrorMessage = null,
                CheckedAt = DateTimeOffset.UtcNow,
                Tools = tools
                    .Select(x => new HarnessToolStatus
                    {
                        Command = x.Command,
                        DisplayName = x.DisplayName,
                        Status = x.Status,
                        Version = x.Version
                    })
                    .ToList()
            };
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, "Failed to retrieve harness tool status for worker");

            return new GetHarnessToolsReply
            {
                Success = false,
                ErrorMessage = ex.Message,
                CheckedAt = DateTimeOffset.UtcNow,
                Tools = []
            };
        }
    }
}
