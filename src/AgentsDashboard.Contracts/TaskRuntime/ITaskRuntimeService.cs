using System.Threading;
using MagicOnion;

namespace AgentsDashboard.Contracts.TaskRuntime;

public interface ITaskRuntimeService : IService<ITaskRuntimeService>
{
    UnaryResult<DispatchJobResult> DispatchJobAsync(DispatchJobRequest request, CancellationToken cancellationToken);
    UnaryResult<StopJobResult> StopJobAsync(StopJobRequest request, CancellationToken cancellationToken);
    UnaryResult<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken);
    UnaryResult<StartRuntimeCommandResult> StartCommandAsync(StartRuntimeCommandRequest request, CancellationToken cancellationToken);
    UnaryResult<CancelRuntimeCommandResult> CancelCommandAsync(CancelRuntimeCommandRequest request, CancellationToken cancellationToken);
    UnaryResult<RuntimeCommandStatusResult> GetCommandStatusAsync(GetRuntimeCommandStatusRequest request, CancellationToken cancellationToken);
    UnaryResult<ListRuntimeFilesResult> ListRuntimeFilesAsync(ListRuntimeFilesRequest request, CancellationToken cancellationToken);
    UnaryResult<CreateRuntimeFileResult> CreateRuntimeFileAsync(CreateRuntimeFileRequest request, CancellationToken cancellationToken);
    UnaryResult<ReadRuntimeFileResult> ReadRuntimeFileAsync(ReadRuntimeFileRequest request, CancellationToken cancellationToken);
    UnaryResult<DeleteRuntimeFileResult> DeleteRuntimeFileAsync(DeleteRuntimeFileRequest request, CancellationToken cancellationToken);
}
