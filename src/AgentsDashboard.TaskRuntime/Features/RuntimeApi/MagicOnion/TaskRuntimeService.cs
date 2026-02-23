


using MagicOnion;
using MagicOnion.Server;

namespace AgentsDashboard.TaskRuntime.Features.RuntimeApi.MagicOnion;

public sealed class TaskRuntimeService(
    ITaskRuntimeQueue queue,
    TaskRuntimeCommandService commandService,
    TaskRuntimeFileSystemService fileSystemService,
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

        await queue.EnqueueAsync(new QueuedJob { Request = request }, CancellationToken.None);

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

    public UnaryResult<HealthCheckResult> CheckHealthAsync()
    {
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

    public async UnaryResult<StartRuntimeCommandResult> StartCommandAsync(StartRuntimeCommandRequest request)
    {
        return await commandService.StartCommandAsync(request, CancellationToken.None);
    }

    public async UnaryResult<CancelRuntimeCommandResult> CancelCommandAsync(CancelRuntimeCommandRequest request)
    {
        return await commandService.CancelCommandAsync(request, CancellationToken.None);
    }

    public async UnaryResult<RuntimeCommandStatusResult> GetCommandStatusAsync(GetRuntimeCommandStatusRequest request)
    {
        return await commandService.GetCommandStatusAsync(request, CancellationToken.None);
    }

    public async UnaryResult<ListRuntimeFilesResult> ListRuntimeFilesAsync(ListRuntimeFilesRequest request)
    {
        return await fileSystemService.ListRuntimeFilesAsync(request, CancellationToken.None);
    }

    public async UnaryResult<CreateRuntimeFileResult> CreateRuntimeFileAsync(CreateRuntimeFileRequest request)
    {
        return await fileSystemService.CreateRuntimeFileAsync(request, CancellationToken.None);
    }

    public async UnaryResult<ReadRuntimeFileResult> ReadRuntimeFileAsync(ReadRuntimeFileRequest request)
    {
        return await fileSystemService.ReadRuntimeFileAsync(request, CancellationToken.None);
    }

    public async UnaryResult<DeleteRuntimeFileResult> DeleteRuntimeFileAsync(DeleteRuntimeFileRequest request)
    {
        return await fileSystemService.DeleteRuntimeFileAsync(request, CancellationToken.None);
    }
}
