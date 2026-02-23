
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AgentsDashboard.ControlPlane.Infrastructure.Health;

public sealed class DatabaseReadyHealthCheck(
    LiteDbDatabase database) : IHealthCheck
{
    public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = database.DatabasePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("LiteDB path is not configured");
            }

            await database.ExecuteAsync(
                static db =>
                {
                    _ = db.GetCollectionNames().ToList();
                    return true;
                },
                cancellationToken);

            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("LiteDB is ready");
        }
        catch (Exception ex)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("LiteDB readiness check failed", ex);
        }
    }
}
