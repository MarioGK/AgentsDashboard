using MagicOnion;
using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

/// <summary>
/// MagicOnion unary service for worker gateway operations.
/// Replaces gRPC WorkerGateway service.
/// </summary>
public interface ITaskRuntimeGatewayService : IService<ITaskRuntimeGatewayService>
{
    /// <summary>
    /// Dispatch a job to the worker for execution.
    /// </summary>
    UnaryResult<DispatchJobReply> DispatchJobAsync(DispatchJobRequest request);

    /// <summary>
    /// Cancel a running job.
    /// </summary>
    UnaryResult<CancelJobReply> CancelJobAsync(CancelJobRequest request);

    /// <summary>
    /// Force kill a container.
    /// </summary>
    UnaryResult<KillContainerReply> KillContainerAsync(KillContainerRequest request);

    /// <summary>
    /// Worker heartbeat to report status.
    /// </summary>
    UnaryResult<HeartbeatReply> HeartbeatAsync(HeartbeatRequest request);

    /// <summary>
    /// Reconcile orphaned containers on the worker.
    /// </summary>
    UnaryResult<ReconcileOrphanedContainersReply> ReconcileOrphanedContainersAsync(ReconcileOrphanedContainersRequest request);

    /// <summary>
    /// Query harness tool availability and versions from this worker.
    /// </summary>
    UnaryResult<GetHarnessToolsReply> GetHarnessToolsAsync(GetHarnessToolsRequest request);
}
