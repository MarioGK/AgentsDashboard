using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class DatabaseReadyHealthCheck(
    LiteDbDatabase database,
    IOptions<OrchestratorOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var path = options.Value.LiteDbPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                return HealthCheckResult.Unhealthy("LiteDB path is not configured");
            }

            _ = await database.ExecuteAsync(
                db => db.GetCollectionNames().ToList(),
                cancellationToken);
            if (!File.Exists(path))
            {
                return HealthCheckResult.Unhealthy($"LiteDB file is missing: {path}");
            }

            return HealthCheckResult.Healthy("LiteDB is ready");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("LiteDB readiness check failed", ex);
        }
    }
}
