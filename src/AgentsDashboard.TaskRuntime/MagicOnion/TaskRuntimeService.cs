using AgentsDashboard.Contracts.TaskRuntime;
using AgentsDashboard.TaskRuntime.Models;
using AgentsDashboard.TaskRuntime.Services;
using MagicOnion;
using MagicOnion.Server;

namespace AgentsDashboard.TaskRuntime.MagicOnion;

public sealed class TaskRuntimeService(
    ITaskRuntimeQueue queue,
    TaskRuntimeCommandService commandService,
    TaskRuntimeFileSystemService fileSystemService,
    ILogger<TaskRuntimeService> logger)
    : ServiceBase<ITaskRuntimeService>, ITaskRuntimeService
{
    public async UnaryResult<DispatchJobResult> DispatchJobAsync(DispatchJobRequest request, CancellationToken cancellationToken)
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

    public UnaryResult<StopJobResult> StopJobAsync(StopJobRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.RunId))
        {
            return UnaryResult.FromResult(new StopJobResult
            {
                Success = false,
                ErrorMessage = "run_id is required",
            });
        }

        var accepted = queue.Cancel(request.RunId);

        return UnaryResult.FromResult(new StopJobResult
        {
            Success = accepted,
            ErrorMessage = accepted ? null : $"Run {request.RunId} not found or already completed",
        });
    }

    public UnaryResult<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (queue.ActiveSlots > queue.MaxSlots)
        {
            return UnaryResult.FromResult(new HealthCheckResult
            {
                Success = false,
                ErrorMessage = $"active slots {queue.ActiveSlots} exceeds max slots {queue.MaxSlots}",
                CheckedAt = DateTimeOffset.UtcNow,
            });
        }

        return UnaryResult.FromResult(new HealthCheckResult
        {
            Success = true,
            ErrorMessage = null,
            CheckedAt = DateTimeOffset.UtcNow,
        });
    }

    public async UnaryResult<StartRuntimeCommandResult> StartCommandAsync(StartRuntimeCommandRequest request, CancellationToken cancellationToken)
    {
        return await commandService.StartCommandAsync(request, cancellationToken);
    }

    public async UnaryResult<CancelRuntimeCommandResult> CancelCommandAsync(CancelRuntimeCommandRequest request, CancellationToken cancellationToken)
    {
        return await commandService.CancelCommandAsync(request, cancellationToken);
    }

    public async UnaryResult<RuntimeCommandStatusResult> GetCommandStatusAsync(GetRuntimeCommandStatusRequest request, CancellationToken cancellationToken)
    {
        return await commandService.GetCommandStatusAsync(request, cancellationToken);
    }

    public async UnaryResult<ListRuntimeFilesResult> ListRuntimeFilesAsync(ListRuntimeFilesRequest request, CancellationToken cancellationToken)
    {
        return await fileSystemService.ListRuntimeFilesAsync(request, cancellationToken);
    }

    public async UnaryResult<CreateRuntimeFileResult> CreateRuntimeFileAsync(CreateRuntimeFileRequest request, CancellationToken cancellationToken)
    {
        return await fileSystemService.CreateRuntimeFileAsync(request, cancellationToken);
    }

    public async UnaryResult<ReadRuntimeFileResult> ReadRuntimeFileAsync(ReadRuntimeFileRequest request, CancellationToken cancellationToken)
    {
        return await fileSystemService.ReadRuntimeFileAsync(request, cancellationToken);
    }

    public async UnaryResult<DeleteRuntimeFileResult> DeleteRuntimeFileAsync(DeleteRuntimeFileRequest request, CancellationToken cancellationToken)
    {
        return await fileSystemService.DeleteRuntimeFileAsync(request, cancellationToken);
    }
}
