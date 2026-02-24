using Grpc.Core;
using Microsoft.Extensions.Logging;

namespace AgentsDashboard.ControlPlane.Features.RuntimeOrchestration.Services;

public sealed class TaskRuntimeRepositoryGitGateway(
    IMagicOnionClientFactory clientFactory,
    ITaskRuntimeLifecycleManager lifecycleManager,
    ILogger<TaskRuntimeRepositoryGitGateway> logger)
{
    public async Task<RepositoryWorkspaceResult> EnsureWorkspaceAsync(
        EnsureRepositoryWorkspaceRequest request,
        CancellationToken cancellationToken)
    {
        var (_, client) = await CreateClientAsync(cancellationToken);
        return await client.WithCancellationToken(cancellationToken).EnsureRepositoryWorkspaceAsync(request);
    }

    public async Task<RepositoryWorkspaceResult> RefreshWorkspaceAsync(
        RefreshRepositoryWorkspaceRequest request,
        CancellationToken cancellationToken)
    {
        var (runtimeId, client) = await CreateClientAsync(cancellationToken);
        try
        {
            return await client.WithCancellationToken(cancellationToken).RefreshRepositoryWorkspaceAsync(request);
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.Unimplemented)
        {
            logger.LogWarning(
                ex,
                "Runtime {RuntimeId} does not support RefreshRepositoryWorkspaceAsync; falling back to EnsureRepositoryWorkspaceAsync for repository {RepositoryId}.",
                runtimeId,
                request.RepositoryId);

            var fallbackRequest = new EnsureRepositoryWorkspaceRequest
            {
                RepositoryId = request.RepositoryId,
                GitUrl = request.GitUrl,
                DefaultBranch = request.DefaultBranch,
                GitHubToken = request.GitHubToken,
                FetchRemote = request.FetchRemote,
                RepositoryKeyHint = request.LocalPath
            };
            return await client.WithCancellationToken(cancellationToken).EnsureRepositoryWorkspaceAsync(fallbackRequest);
        }
    }

    private async Task<(string RuntimeId, ITaskRuntimeService Client)> CreateClientAsync(CancellationToken cancellationToken)
    {
        var runtime = (await lifecycleManager.ListTaskRuntimesAsync(cancellationToken))
            .Where(candidate =>
                candidate.IsRunning &&
                !candidate.IsDraining &&
                !string.IsNullOrWhiteSpace(candidate.GrpcEndpoint) &&
                candidate.LifecycleState is TaskRuntimeLifecycleState.Ready or TaskRuntimeLifecycleState.Busy)
            .OrderBy(candidate => candidate.ActiveSlots)
            .ThenBy(candidate => candidate.LastActivityUtc)
            .FirstOrDefault();

        if (runtime is null)
        {
            throw new InvalidOperationException("No healthy task runtime is available for git operations.");
        }

        return (runtime.TaskRuntimeId, clientFactory.CreateTaskRuntimeService(runtime.TaskRuntimeId, runtime.GrpcEndpoint));
    }
}
