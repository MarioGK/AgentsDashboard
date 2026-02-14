using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.WorkerGateway.Models;
using AgentsDashboard.WorkerGateway.Services;
using Grpc.Core;

namespace AgentsDashboard.WorkerGateway.Grpc;

public sealed class WorkerGatewayGrpcService(
    WorkerQueue queue,
    WorkerEventBus eventBus,
    IContainerOrphanReconciler orphanReconciler,
    IDockerContainerService dockerService,
    ILogger<WorkerGatewayGrpcService> logger)
    : AgentsDashboard.Contracts.Worker.WorkerGateway.WorkerGatewayBase
{
    public override async Task<DispatchJobReply> DispatchJob(DispatchJobRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.RunId))
        {
            return new DispatchJobReply { Accepted = false, Reason = "run_id is required" };
        }

        if (!queue.CanAcceptJob())
        {
            return new DispatchJobReply { Accepted = false, Reason = "worker at capacity" };
        }

        await queue.EnqueueAsync(new QueuedJob { Request = request }, context.CancellationToken);

        logger.LogInformation("Accepted run {RunId} using harness {Harness}", request.RunId, request.Harness);
        return new DispatchJobReply { Accepted = true, Reason = string.Empty };
    }

    public override Task<CancelJobReply> CancelJob(CancelJobRequest request, ServerCallContext context)
    {
        var accepted = queue.Cancel(request.RunId);
        return Task.FromResult(new CancelJobReply { Accepted = accepted });
    }

    public override async Task<KillContainerReply> KillContainer(KillContainerRequest request, ServerCallContext context)
    {
        if (string.IsNullOrWhiteSpace(request.RunId))
        {
            return new KillContainerReply { Killed = false, Error = "run_id is required" };
        }

        logger.LogWarning("KillContainer request received for run {RunId}. Reason: {Reason}, Force: {Force}",
            request.RunId, request.Reason, request.Force);

        var result = await dockerService.KillContainerByRunIdAsync(
            request.RunId,
            request.Reason,
            request.Force,
            context.CancellationToken);

        return new KillContainerReply
        {
            Killed = result.Killed,
            ContainerId = result.ContainerId,
            Error = result.Error
        };
    }

    public override async Task SubscribeEvents(SubscribeEventsRequest request, IServerStreamWriter<JobEventReply> responseStream, ServerCallContext context)
    {
        await foreach (var evt in eventBus.ReadAllAsync(context.CancellationToken))
        {
            await responseStream.WriteAsync(evt, context.CancellationToken);
        }
    }

    public override Task<HeartbeatReply> Heartbeat(HeartbeatRequest request, ServerCallContext context)
    {
        var reply = new HeartbeatReply
        {
            Acknowledged = true
        };
        return Task.FromResult(reply);
    }

    public override async Task<ReconcileOrphanedContainersReply> ReconcileOrphanedContainers(
        ReconcileOrphanedContainersRequest request,
        ServerCallContext context)
    {
        logger.LogInformation("Received orphan reconciliation request with {Count} active run IDs", request.ActiveRunIds.Count);

        var result = await orphanReconciler.ReconcileAsync(request.ActiveRunIds, context.CancellationToken);

        var reply = new ReconcileOrphanedContainersReply
        {
            OrphanedCount = result.OrphanedCount
        };

        foreach (var container in result.RemovedContainers)
        {
            reply.RemovedContainers.Add(new OrphanedContainerInfo
            {
                ContainerId = container.ContainerId,
                RunId = container.RunId
            });
        }

        logger.LogInformation("Orphan reconciliation complete: {OrphanedCount} found, {RemovedCount} removed",
            result.OrphanedCount, result.RemovedContainers.Count);

        return reply;
    }
}
