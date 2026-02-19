using System.Threading;
using System.Threading.Tasks;
using MagicOnion;

namespace AgentsDashboard.Contracts.TaskRuntime;

public interface ITaskRuntimeService : IService<ITaskRuntimeService>
{
    ValueTask<RuntimeDispatchResult> DispatchJobAsync(RuntimeDispatchRequest request, CancellationToken cancellationToken);
    ValueTask<RuntimeCancelResult> CancelJobAsync(RuntimeCancelRequest request, CancellationToken cancellationToken);
    ValueTask<RuntimeHealthResult> HealthAsync(CancellationToken cancellationToken);
    ValueTask<ListDirResult> ListDirectoryAsync(FileSystemRequest request, CancellationToken cancellationToken);
    ValueTask<FileReadResult> ReadFileAsync(FileReadRequest request, CancellationToken cancellationToken);
    ValueTask<FileWriteResult> WriteFileAsync(FileWriteRequest request, CancellationToken cancellationToken);
    ValueTask<FileDeleteResult> DeletePathAsync(DeletePathRequest request, CancellationToken cancellationToken);
    ValueTask<GitStatusResult> StatusAsync(GitStatusRequest request, CancellationToken cancellationToken);
    ValueTask<GitDiffResult> DiffAsync(GitDiffRequest request, CancellationToken cancellationToken);
    ValueTask<GitCommitResult> CommitAsync(GitCommitRequest request, CancellationToken cancellationToken);
    ValueTask<GitPushResult> PushAsync(GitPushRequest request, CancellationToken cancellationToken);
    ValueTask<GitFetchResult> FetchAsync(GitFetchRequest request, CancellationToken cancellationToken);
    ValueTask<GitCheckoutResult> CheckoutAsync(GitCheckoutRequest request, CancellationToken cancellationToken);
}
