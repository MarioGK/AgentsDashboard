using AgentsDashboard.ControlPlane.Data;
using Microsoft.EntityFrameworkCore;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class DbMigrationHostedService(
    IServiceScopeFactory scopeFactory,
    INotificationSink notificationSink,
    ILogger<DbMigrationHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await notificationSink.PublishAsync(
                "Database migration: running",
                "Applying EF Core migrations during startup.",
                NotificationSeverity.Info,
                NotificationSource.System);

            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrchestratorDbContext>();
            await db.Database.MigrateAsync(cancellationToken);
            logger.ZLogInformation("EF Core migrations applied successfully");

            await notificationSink.PublishAsync(
                "Database migration: succeeded",
                "EF Core migrations completed successfully.",
                NotificationSeverity.Success,
                NotificationSource.System);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.ZLogInformation("Database migration canceled during startup.");

            await notificationSink.PublishAsync(
                "Database migration: cancelled",
                "EF Core migration was cancelled during startup.",
                NotificationSeverity.Warning,
                NotificationSource.System);
        }
        catch (Exception ex)
        {
            logger.ZLogError(ex, "Failed to apply EF Core migrations");

            await notificationSink.PublishAsync(
                "Database migration: failed",
                $"EF Core migration failed: {ex.Message}. Run migration diagnostics and retry startup.",
                NotificationSeverity.Error,
                NotificationSource.System);
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
