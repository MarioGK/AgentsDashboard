using AgentsDashboard.Contracts.TaskRuntime;
using AgentsDashboard.TaskRuntime.Models;
using AgentsDashboard.TaskRuntime.Services;
using MagicOnion.Server;

namespace AgentsDashboard.TaskRuntime.MagicOnion;

public sealed class TaskRuntimeService(
    ITaskRuntimeQueue queue,
    TaskRuntimeCommandService commandService,
    ILogger<TaskRuntimeService> logger)
    : ServiceBase<ITaskRuntimeService>, ITaskRuntimeService
{
    public async ValueTask<DispatchJobResult> DispatchJobAsync(DispatchJobRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RunId))
        {
            return new DispatchJobResult
            {
                Success = false,
                ErrorMessage = "run_id is required",
                DispatchedAt = DateTimeOffset.UtcNow,
            };
        }

        if (!queue.CanAcceptJob())
        {
            return new DispatchJobResult
            {
                Success = false,
                ErrorMessage = "task runtime at capacity",
                DispatchedAt = DateTimeOffset.UtcNow,
            };
        }

        await queue.EnqueueAsync(new QueuedJob { Request = request }, cancellationToken);

        logger.LogInformation("Accepted run {RunId} using harness {Harness}", request.RunId, request.HarnessType);

        return new DispatchJobResult
        {
            Success = true,
            ErrorMessage = null,
            DispatchedAt = DateTimeOffset.UtcNow,
        };
    }

    public ValueTask<StopJobResult> StopJobAsync(StopJobRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.RunId))
        {
            return ValueTask.FromResult(new StopJobResult
            {
                Success = false,
                ErrorMessage = "run_id is required",
            });
        }

        var accepted = queue.Cancel(request.RunId);

        return ValueTask.FromResult(new StopJobResult
        {
            Success = accepted,
            ErrorMessage = accepted ? null : $"Run {request.RunId} not found or already completed",
        });
    }

    public ValueTask<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (queue.ActiveSlots > queue.MaxSlots)
        {
            return ValueTask.FromResult(new HealthCheckResult
            {
                Success = false,
                ErrorMessage = $"active slots {queue.ActiveSlots} exceeds max slots {queue.MaxSlots}",
                CheckedAt = DateTimeOffset.UtcNow,
            });
        }

        return ValueTask.FromResult(new HealthCheckResult
        {
            Success = true,
            ErrorMessage = null,
            CheckedAt = DateTimeOffset.UtcNow,
        });
    }

    public ValueTask<StartRuntimeCommandResult> StartCommandAsync(StartRuntimeCommandRequest request, CancellationToken cancellationToken)
    {
        return commandService.StartCommandAsync(request, cancellationToken);
    }

    public ValueTask<CancelRuntimeCommandResult> CancelCommandAsync(CancelRuntimeCommandRequest request, CancellationToken cancellationToken)
    {
        return commandService.CancelCommandAsync(request, cancellationToken);
    }

    public ValueTask<RuntimeCommandStatusResult> GetCommandStatusAsync(GetRuntimeCommandStatusRequest request, CancellationToken cancellationToken)
    {
        return commandService.GetCommandStatusAsync(request, cancellationToken);
    }
}
