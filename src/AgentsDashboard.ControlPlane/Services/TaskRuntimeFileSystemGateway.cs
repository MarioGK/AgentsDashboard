using AgentsDashboard.Contracts.TaskRuntime;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class TaskRuntimeFileSystemGateway(
    IMagicOnionClientFactory clientFactory,
    ITaskRuntimeLifecycleManager lifecycleManager)
{
    public async Task<ListRuntimeFilesResult> ListRuntimeFilesAsync(
        string taskRuntimeId,
        ListRuntimeFilesRequest request,
        CancellationToken cancellationToken)
    {
        var client = await CreateClientAsync(taskRuntimeId, cancellationToken);
        return await client.WithCancellationToken(cancellationToken).ListRuntimeFilesAsync(request);
    }

    public async Task<CreateRuntimeFileResult> CreateRuntimeFileAsync(
        string taskRuntimeId,
        CreateRuntimeFileRequest request,
        CancellationToken cancellationToken)
    {
        var client = await CreateClientAsync(taskRuntimeId, cancellationToken);
        return await client.WithCancellationToken(cancellationToken).CreateRuntimeFileAsync(request);
    }

    public async Task<ReadRuntimeFileResult> ReadRuntimeFileAsync(
        string taskRuntimeId,
        ReadRuntimeFileRequest request,
        CancellationToken cancellationToken)
    {
        var client = await CreateClientAsync(taskRuntimeId, cancellationToken);
        return await client.WithCancellationToken(cancellationToken).ReadRuntimeFileAsync(request);
    }

    public async Task<DeleteRuntimeFileResult> DeleteRuntimeFileAsync(
        string taskRuntimeId,
        DeleteRuntimeFileRequest request,
        CancellationToken cancellationToken)
    {
        var client = await CreateClientAsync(taskRuntimeId, cancellationToken);
        return await client.WithCancellationToken(cancellationToken).DeleteRuntimeFileAsync(request);
    }

    private async Task<ITaskRuntimeService> CreateClientAsync(string taskRuntimeId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskRuntimeId))
        {
            throw new ArgumentException("Task runtime id is required.", nameof(taskRuntimeId));
        }

        var runtime = await lifecycleManager.GetTaskRuntimeAsync(taskRuntimeId.Trim(), cancellationToken);
        if (runtime is null || string.IsNullOrWhiteSpace(runtime.GrpcEndpoint))
        {
            throw new InvalidOperationException($"Task runtime '{taskRuntimeId}' is unavailable.");
        }

        return clientFactory.CreateTaskRuntimeService(runtime.TaskRuntimeId, runtime.GrpcEndpoint);
    }
}
