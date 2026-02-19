using AgentsDashboard.Contracts.TaskRuntime;
using AgentsDashboard.TaskRuntime.Models;
using AgentsDashboard.TaskRuntime.Services;
using MagicOnion;
using MagicOnion.Server;

namespace AgentsDashboard.TaskRuntime.MagicOnion;

public sealed class TaskRuntimeService(
    ITaskRuntimeQueue queue,
    ILogger<TaskRuntimeService> logger)
    : ServiceBase<ITaskRuntimeService>, ITaskRuntimeService
{
    public async UnaryResult<DispatchJobResult> DispatchJobAsync(DispatchJobRequest request)
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

        await queue.EnqueueAsync(new QueuedJob { Request = request }, Context.CallContext.CancellationToken);

        logger.LogInformation("Accepted run {RunId} using harness {Harness}", request.RunId, request.HarnessType);

        return new DispatchJobResult
        {
            Success = true,
            ErrorMessage = null,
            DispatchedAt = DateTimeOffset.UtcNow,
        };
    }

    public UnaryResult<StopJobResult> StopJobAsync(StopJobRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.RunId))
        {
            return new StopJobResult
            {
                Success = false,
                ErrorMessage = "run_id is required",
            };
        }

        var accepted = queue.Cancel(request.RunId);

        return new StopJobResult
        {
            Success = accepted,
            ErrorMessage = accepted ? null : $"Run {request.RunId} not found or already completed",
        };
    }

    public UnaryResult<HealthCheckResult> CheckHealthAsync()
    {
        if (queue.ActiveSlots > queue.MaxSlots)
        {
            return new HealthCheckResult
            {
                Success = false,
                ErrorMessage = $"active slots {queue.ActiveSlots} exceeds max slots {queue.MaxSlots}",
                CheckedAt = DateTimeOffset.UtcNow,
            };
        }

        return new HealthCheckResult
        {
            Success = true,
            ErrorMessage = null,
            CheckedAt = DateTimeOffset.UtcNow,
        };
    }
}
