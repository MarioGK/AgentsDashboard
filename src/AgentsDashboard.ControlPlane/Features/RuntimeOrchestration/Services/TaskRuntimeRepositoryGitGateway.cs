namespace AgentsDashboard.ControlPlane.Features.RuntimeOrchestration.Services;

public sealed class TaskRuntimeRepositoryGitGateway(
    IMagicOnionClientFactory clientFactory,
    ITaskRuntimeLifecycleManager lifecycleManager)
{
    public async Task<RepositoryWorkspaceResult> EnsureWorkspaceAsync(
        EnsureRepositoryWorkspaceRequest request,
        CancellationToken cancellationToken)
    {
        var client = await CreateClientAsync(cancellationToken);
        return await client.WithCancellationToken(cancellationToken).EnsureRepositoryWorkspaceAsync(request);
    }

    public async Task<RepositoryWorkspaceResult> RefreshWorkspaceAsync(
        RefreshRepositoryWorkspaceRequest request,
        CancellationToken cancellationToken)
    {
        var client = await CreateClientAsync(cancellationToken);
        return await client.WithCancellationToken(cancellationToken).RefreshRepositoryWorkspaceAsync(request);
    }

    private async Task<ITaskRuntimeService> CreateClientAsync(CancellationToken cancellationToken)
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

        return clientFactory.CreateTaskRuntimeService(runtime.TaskRuntimeId, runtime.GrpcEndpoint);
    }
}
