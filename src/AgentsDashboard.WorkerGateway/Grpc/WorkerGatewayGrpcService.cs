using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.WorkerGateway.Models;
using AgentsDashboard.WorkerGateway.Services;
using Grpc.Core;

namespace AgentsDashboard.WorkerGateway.Grpc;

public sealed class WorkerGatewayGrpcService(WorkerQueue queue, WorkerEventBus eventBus, ILogger<WorkerGatewayGrpcService> logger)
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
}
