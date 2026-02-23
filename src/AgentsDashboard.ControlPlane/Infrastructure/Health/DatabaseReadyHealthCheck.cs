using AgentsDashboard.ControlPlane.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class DatabaseReadyHealthCheck(
    LiteDbDatabase database) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = database.DatabasePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                return HealthCheckResult.Unhealthy("LiteDB path is not configured");
            }

            await database.ExecuteAsync(
                static db =>
                {
                    _ = db.GetCollectionNames().ToList();
                    return true;
                },
                cancellationToken);

            return HealthCheckResult.Healthy("LiteDB is ready");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("LiteDB readiness check failed", ex);
        }
    }
}
