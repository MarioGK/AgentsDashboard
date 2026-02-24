using MagicOnion;
using AgentsDashboard.Contracts.Features.Runtime.Models.TaskRuntime.Messages;

namespace AgentsDashboard.Contracts.Features.Runtime.Models.TaskRuntime;

public interface ITaskRuntimeService : IService<ITaskRuntimeService>
{
    UnaryResult<DispatchJobResult> DispatchJobAsync(DispatchJobRequest request);
    UnaryResult<StopJobResult> StopJobAsync(StopJobRequest request);
    UnaryResult<HealthCheckResult> CheckHealthAsync();
    UnaryResult<RepositoryWorkspaceResult> EnsureRepositoryWorkspaceAsync(EnsureRepositoryWorkspaceRequest request);
    UnaryResult<RepositoryWorkspaceResult> RefreshRepositoryWorkspaceAsync(RefreshRepositoryWorkspaceRequest request);
    UnaryResult<StartRuntimeCommandResult> StartCommandAsync(StartRuntimeCommandRequest request);
    UnaryResult<CancelRuntimeCommandResult> CancelCommandAsync(CancelRuntimeCommandRequest request);
    UnaryResult<RuntimeCommandStatusResult> GetCommandStatusAsync(GetRuntimeCommandStatusRequest request);
    UnaryResult<ListRuntimeFilesResult> ListRuntimeFilesAsync(ListRuntimeFilesRequest request);
    UnaryResult<CreateRuntimeFileResult> CreateRuntimeFileAsync(CreateRuntimeFileRequest request);
    UnaryResult<ReadRuntimeFileResult> ReadRuntimeFileAsync(ReadRuntimeFileRequest request);
    UnaryResult<DeleteRuntimeFileResult> DeleteRuntimeFileAsync(DeleteRuntimeFileRequest request);
    UnaryResult<ReadEventBacklogResult> ReadEventBacklogAsync(ReadEventBacklogRequest request);
    UnaryResult<RuntimeQueueSnapshotResult> GetRuntimeQueueSnapshotAsync(GetRuntimeQueueSnapshotRequest request);
    UnaryResult<RunExecutionSnapshotResult> GetRunExecutionSnapshotAsync(GetRunExecutionSnapshotRequest request);
}
