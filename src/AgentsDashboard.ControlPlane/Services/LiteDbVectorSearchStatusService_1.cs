namespace AgentsDashboard.ControlPlane.Services;

public interface ILiteDbVectorSearchStatusService
{
    bool IsAvailable { get; }
    LiteDbVectorSearchAvailability Status { get; }
}


public sealed class LiteDbVectorSearchStatusService(
    IBackgroundWorkCoordinator backgroundWorkCoordinator,
    ILogger<LiteDbVectorSearchStatusService> logger) : IHostedService, ILiteDbVectorSearchStatusService
{
    private const string StartupOperationKey = "startup:litedb-vector-bootstrap";

    private volatile LiteDbVectorSearchAvailability _status = new(
        IsAvailable: true,
        ExtensionPath: null,
        Detail: "LiteDB vector mode active",
        CheckedAtUtc: DateTime.UtcNow);

    public bool IsAvailable => _status.IsAvailable;

    public LiteDbVectorSearchAvailability Status => _status;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var workId = backgroundWorkCoordinator.Enqueue(
            BackgroundWorkKind.LiteDbVectorBootstrap,
            StartupOperationKey,
            async (_, progress) =>
            {
                _status = new LiteDbVectorSearchAvailability(
                    IsAvailable: true,
                    ExtensionPath: null,
                    Detail: "LiteDB vector mode active",
                    CheckedAtUtc: DateTime.UtcNow);

                progress.Report(new BackgroundWorkSnapshot(
                    WorkId: string.Empty,
                    OperationKey: string.Empty,
                    Kind: BackgroundWorkKind.LiteDbVectorBootstrap,
                    State: BackgroundWorkState.Succeeded,
                    PercentComplete: 100,
                    Message: "LiteDB vector bootstrap completed.",
                    StartedAt: null,
                    UpdatedAt: DateTimeOffset.UtcNow,
                    ErrorCode: null,
                    ErrorMessage: null));

                await Task.CompletedTask;
            },
            dedupeByOperationKey: true,
            isCritical: false);

        logger.LogInformation("Queued LiteDB vector bootstrap background work {WorkId}", workId);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
