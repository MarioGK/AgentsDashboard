namespace AgentsDashboard.ControlPlane.Services;

public interface ILiteDbVectorSearchStatusService
{
    bool IsAvailable { get; }
    LiteDbVectorSearchAvailability Status { get; }
}

public interface ILiteDbVectorBootstrapService
{
    bool IsAvailable { get; }
    LiteDbVectorSearchAvailability Status { get; }
}


public sealed record LiteDbVectorSearchAvailability(
    bool IsAvailable,
    string? ExtensionPath,
    string? Detail,
    DateTime CheckedAtUtc);

public sealed class LiteDbVectorSearchStatusService(
    IBackgroundWorkCoordinator backgroundWorkCoordinator,
    ILogger<LiteDbVectorSearchStatusService> logger)
    : BackgroundService, ILiteDbVectorSearchStatusService, ILiteDbVectorBootstrapService
{
    private string _workId = string.Empty;
    private readonly object _sync = new();
    private LiteDbVectorSearchAvailability _status = new(
        false,
        null,
        "LiteDB vector search status unknown",
        DateTime.UtcNow);

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.CompletedTask;
    }

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        var workId = backgroundWorkCoordinator.Enqueue(
            BackgroundWorkKind.LiteDbVectorBootstrap,
            "lite-db-vector-bootstrap",
            BootstrapAsync,
            dedupeByOperationKey: true,
            isCritical: false);

        _workId = workId;
        return base.StartAsync(cancellationToken);
    }

    public bool IsAvailable
    {
        get
        {
            lock (_sync)
            {
                return _status.IsAvailable;
            }
        }
    }

    public LiteDbVectorSearchAvailability Status
    {
        get
        {
            lock (_sync)
            {
                return _status;
            }
        }
    }

    private async Task BootstrapAsync(CancellationToken cancellationToken, IProgress<BackgroundWorkSnapshot> progress)
    {
        SetStatus(
            isAvailable: false,
            extensionPath: null,
            detail: "LiteDB vector mode initializing");

        logger.LogInformation("Starting LiteDB vector bootstrap check.");

        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(1), cancellationToken);

            SetStatus(
                isAvailable: true,
                extensionPath: null,
                detail: "LiteDB vector mode active");

            progress.Report(new BackgroundWorkSnapshot(
                WorkId: _workId,
                OperationKey: "lite-db-vector-bootstrap",
                Kind: BackgroundWorkKind.LiteDbVectorBootstrap,
                State: BackgroundWorkState.Succeeded,
                PercentComplete: 100,
                Message: "LiteDB vector mode active",
                StartedAt: DateTimeOffset.UtcNow,
                UpdatedAt: DateTimeOffset.UtcNow,
                ErrorCode: null,
                ErrorMessage: null));
        }
        catch (OperationCanceledException)
        {
            SetStatus(
                isAvailable: false,
                extensionPath: null,
                detail: "LiteDB vector mode initialization canceled");
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "LiteDB vector bootstrap failed.");
            SetStatus(
                isAvailable: false,
                extensionPath: null,
                detail: "LiteDB vector mode unavailable");
            throw;
        }
    }

    private void SetStatus(bool isAvailable, string? extensionPath, string detail)
    {
        lock (_sync)
        {
            _status = new LiteDbVectorSearchAvailability(isAvailable, extensionPath, detail, DateTime.UtcNow);
        }
    }
}
