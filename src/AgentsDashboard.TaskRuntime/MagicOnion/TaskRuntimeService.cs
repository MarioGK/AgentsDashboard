using AgentsDashboard.Contracts.TaskRuntime;
using AgentsDashboard.TaskRuntime.Models;
using AgentsDashboard.TaskRuntime.Services;
using MagicOnion.Server;

namespace AgentsDashboard.TaskRuntime.MagicOnion;

public sealed class TaskRuntimeService(
    ITaskRuntimeQueue queue,
    TaskRuntimeFileService fileService,
    TaskRuntimeGitService gitService,
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

    public ValueTask<DirectoryListingDto> ListDirectoryAsync(FileSystemRequest request, CancellationToken cancellationToken)
    {
        return fileService.ListDirectoryAsync(request, cancellationToken);
    }

    public ValueTask<FileContentDto> ReadFileAsync(FileReadRequest request, CancellationToken cancellationToken)
    {
        return fileService.ReadFileAsync(request, cancellationToken);
    }

    public ValueTask<WriteFileResult> WriteFileAsync(WriteFileRequest request, CancellationToken cancellationToken)
    {
        return fileService.WriteFileAsync(request, cancellationToken);
    }

    public ValueTask<DeletePathResult> DeletePathAsync(DeletePathRequest request, CancellationToken cancellationToken)
    {
        return fileService.DeletePathAsync(request, cancellationToken);
    }

    public ValueTask<GitStatusDto> StatusAsync(GitStatusRequest request, CancellationToken cancellationToken)
    {
        return gitService.StatusAsync(request, cancellationToken);
    }

    public ValueTask<GitDiffDto> DiffAsync(GitDiffRequest request, CancellationToken cancellationToken)
    {
        return gitService.DiffAsync(request, cancellationToken);
    }

    public ValueTask<GitCommitResult> CommitAsync(GitCommitRequest request, CancellationToken cancellationToken)
    {
        return gitService.CommitAsync(request, cancellationToken);
    }

    public ValueTask<GitPushResult> PushAsync(GitPushRequest request, CancellationToken cancellationToken)
    {
        return gitService.PushAsync(request, cancellationToken);
    }

    public ValueTask<GitFetchResult> FetchAsync(GitFetchRequest request, CancellationToken cancellationToken)
    {
        return gitService.FetchAsync(request, cancellationToken);
    }

    public ValueTask<GitCheckoutResult> CheckoutAsync(GitCheckoutRequest request, CancellationToken cancellationToken)
    {
        return gitService.CheckoutAsync(request, cancellationToken);
    }
}
