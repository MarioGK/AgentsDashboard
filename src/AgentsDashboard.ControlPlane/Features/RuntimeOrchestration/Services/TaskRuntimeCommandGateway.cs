

namespace AgentsDashboard.ControlPlane.Features.RuntimeOrchestration.Services;

public sealed class TaskRuntimeCommandGateway(
    IMagicOnionClientFactory clientFactory,
    ITaskRuntimeLifecycleManager lifecycleManager)
{
    public async Task<StartRuntimeCommandResult> StartAsync(
        string taskRuntimeId,
        StartRuntimeCommandRequest request,
        CancellationToken cancellationToken)
    {
        var client = await CreateClientAsync(taskRuntimeId, cancellationToken);
        return await client.WithCancellationToken(cancellationToken).StartCommandAsync(request);
    }

    public async Task<CancelRuntimeCommandResult> CancelAsync(
        string taskRuntimeId,
        CancelRuntimeCommandRequest request,
        CancellationToken cancellationToken)
    {
        var client = await CreateClientAsync(taskRuntimeId, cancellationToken);
        return await client.WithCancellationToken(cancellationToken).CancelCommandAsync(request);
    }

    public async Task<RuntimeCommandStatusResult> GetStatusAsync(
        string taskRuntimeId,
        GetRuntimeCommandStatusRequest request,
        CancellationToken cancellationToken)
    {
        var client = await CreateClientAsync(taskRuntimeId, cancellationToken);
        return await client.WithCancellationToken(cancellationToken).GetCommandStatusAsync(request);
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
