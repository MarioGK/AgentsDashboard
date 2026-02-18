using MagicOnion;
using MessagePack;

namespace AgentsDashboard.Contracts.TaskRuntime;

/// <summary>
/// MagicOnion unary service for task runtime gateway operations.
/// Replaces gRPC TaskRuntimeGateway service.
/// </summary>
public interface ITaskRuntimeGatewayService : IService<ITaskRuntimeGatewayService>
{
    /// <summary>
    /// Dispatch a job to the task runtime for execution.
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
    /// TaskRuntime heartbeat to report status.
    /// </summary>
    UnaryResult<HeartbeatReply> HeartbeatAsync(HeartbeatRequest request);

    /// <summary>
    /// Reconcile orphaned containers on the task runtime.
    /// </summary>
    UnaryResult<ReconcileOrphanedContainersReply> ReconcileOrphanedContainersAsync(ReconcileOrphanedContainersRequest request);

    /// <summary>
    /// Query harness tool availability and versions from this task runtime.
    /// </summary>
    UnaryResult<GetHarnessToolsReply> GetHarnessToolsAsync(GetHarnessToolsRequest request);
}
