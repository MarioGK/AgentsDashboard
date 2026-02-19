using System.Threading;
using System.Threading.Tasks;
using MagicOnion;

namespace AgentsDashboard.Contracts.TaskRuntime;

public interface ITaskRuntimeService : IService<ITaskRuntimeService>
{
    ValueTask<DispatchJobResult> DispatchJobAsync(DispatchJobRequest request, CancellationToken cancellationToken);
    ValueTask<StopJobResult> StopJobAsync(StopJobRequest request, CancellationToken cancellationToken);
    ValueTask<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken);
    ValueTask<StartRuntimeCommandResult> StartCommandAsync(StartRuntimeCommandRequest request, CancellationToken cancellationToken);
    ValueTask<CancelRuntimeCommandResult> CancelCommandAsync(CancelRuntimeCommandRequest request, CancellationToken cancellationToken);
    ValueTask<RuntimeCommandStatusResult> GetCommandStatusAsync(GetRuntimeCommandStatusRequest request, CancellationToken cancellationToken);
}
