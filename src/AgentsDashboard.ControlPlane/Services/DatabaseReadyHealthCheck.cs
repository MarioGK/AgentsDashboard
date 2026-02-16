using AgentsDashboard.ControlPlane.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class DatabaseReadyHealthCheck(IDbContextFactory<OrchestratorDbContext> dbContextFactory) : IHealthCheck
{
    private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(5);

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var timeoutTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutTokenSource.CancelAfter(CheckTimeout);

            await using var dbContext = await dbContextFactory.CreateDbContextAsync(timeoutTokenSource.Token);
            var canConnect = await dbContext.Database.CanConnectAsync(timeoutTokenSource.Token);
            if (!canConnect)
            {
                return HealthCheckResult.Unhealthy("Database connection failed");
            }

            var hasPendingMigrations = (await dbContext.Database.GetPendingMigrationsAsync(timeoutTokenSource.Token)).Any();
            if (hasPendingMigrations)
            {
                return HealthCheckResult.Unhealthy("Database has pending migrations");
            }

            return HealthCheckResult.Healthy("Database is ready");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HealthCheckResult.Unhealthy($"Database readiness check timed out after {CheckTimeout.TotalSeconds:0}s");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database readiness check failed", ex);
        }
    }
}
