namespace AgentsDashboard.ControlPlane.Services;

public sealed class TaskTemplateInitializationService(
    TaskTemplateService templateService,
    IBackgroundWorkCoordinator backgroundWorkCoordinator,
    ILogger<TaskTemplateInitializationService> logger) : IHostedService
{
    private const string StartupOperationKey = "startup:task-template-initialization";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var workId = backgroundWorkCoordinator.Enqueue(
            BackgroundWorkKind.TaskTemplateInit,
            StartupOperationKey,
            async (workCancellationToken, progress) =>
            {
                progress.Report(new BackgroundWorkSnapshot(
                    WorkId: string.Empty,
                    OperationKey: string.Empty,
                    Kind: BackgroundWorkKind.TaskTemplateInit,
                    State: BackgroundWorkState.Running,
                    PercentComplete: 10,
                    Message: "Initializing task templates.",
                    StartedAt: null,
                    UpdatedAt: DateTimeOffset.UtcNow,
                    ErrorCode: null,
                    ErrorMessage: null));

                await templateService.InitializeAsync(workCancellationToken);

                progress.Report(new BackgroundWorkSnapshot(
                    WorkId: string.Empty,
                    OperationKey: string.Empty,
                    Kind: BackgroundWorkKind.TaskTemplateInit,
                    State: BackgroundWorkState.Succeeded,
                    PercentComplete: 100,
                    Message: "Task template initialization completed.",
                    StartedAt: null,
                    UpdatedAt: DateTimeOffset.UtcNow,
                    ErrorCode: null,
                    ErrorMessage: null));
            },
            dedupeByOperationKey: true,
            isCritical: false);

        logger.ZLogInformation("Queued task template initialization background work {WorkId}", workId);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
