namespace AgentsDashboard.ControlPlane.Features.RuntimeOrchestration.Services;

public sealed class TaskRuntimeImageBootstrapService(
    ITaskRuntimeLifecycleManager lifecycleManager,
    IBackgroundWorkCoordinator backgroundWorkCoordinator,
    ILogger<TaskRuntimeImageBootstrapService> logger) : IHostedService
{
    private const string StartupOperationKey = "startup:worker-image-resolution";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var workId = backgroundWorkCoordinator.Enqueue(
            BackgroundWorkKind.TaskRuntimeImageResolution,
            StartupOperationKey,
            async (workCancellationToken, progress) => await lifecycleManager.EnsureTaskRuntimeImageAvailableAsync(workCancellationToken, progress),
            dedupeByOperationKey: true,
            isCritical: false);

        logger.LogInformation("Queued startup task runtime image bootstrap as background work {WorkId}", workId);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
