namespace AgentsDashboard.ControlPlane.Services;

public interface ISqliteVecBootstrapService
{
    bool IsAvailable { get; }
    SqliteVecAvailability Status { get; }
}

public sealed record SqliteVecAvailability(
    bool IsAvailable,
    string? ExtensionPath,
    string? Detail,
    DateTime CheckedAtUtc);

public sealed class SqliteVecBootstrapService(
    IBackgroundWorkCoordinator backgroundWorkCoordinator,
    ILogger<SqliteVecBootstrapService> logger) : IHostedService, ISqliteVecBootstrapService
{
    private const string StartupOperationKey = "startup:litedb-vector-bootstrap";

    private volatile SqliteVecAvailability _status = new(
        IsAvailable: false,
        ExtensionPath: null,
        Detail: "LiteDB vector mode active",
        CheckedAtUtc: DateTime.UtcNow);

    public bool IsAvailable => _status.IsAvailable;

    public SqliteVecAvailability Status => _status;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var workId = backgroundWorkCoordinator.Enqueue(
            BackgroundWorkKind.SqliteVecBootstrap,
            StartupOperationKey,
            async (_, progress) =>
            {
                _status = new SqliteVecAvailability(
                    IsAvailable: false,
                    ExtensionPath: null,
                    Detail: "LiteDB vector mode active",
                    CheckedAtUtc: DateTime.UtcNow);

                progress.Report(new BackgroundWorkSnapshot(
                    WorkId: string.Empty,
                    OperationKey: string.Empty,
                    Kind: BackgroundWorkKind.SqliteVecBootstrap,
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

        logger.ZLogInformation("Queued LiteDB vector bootstrap background work {WorkId}", workId);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
