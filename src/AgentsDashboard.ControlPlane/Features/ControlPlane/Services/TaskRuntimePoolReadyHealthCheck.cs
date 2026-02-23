using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class TaskRuntimePoolReadyHealthCheck(
    TaskRuntimeHealthSupervisorService healthSupervisor) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var snapshot = healthSupervisor.GetSnapshot();
        var data = new Dictionary<string, object>
        {
            ["total"] = snapshot.TotalRuntimes,
            ["healthy"] = snapshot.HealthyRuntimes,
            ["degraded"] = snapshot.DegradedRuntimes,
            ["unhealthy"] = snapshot.UnhealthyRuntimes,
            ["offline"] = snapshot.OfflineRuntimes,
            ["recovering"] = snapshot.RecoveringRuntimes,
            ["quarantined"] = snapshot.QuarantinedRuntimes,
            ["readiness_blocked"] = snapshot.ReadinessBlocked
        };

        if (snapshot.ReadinessBlocked)
        {
            var message = snapshot.ReadinessBlockedSinceUtc.HasValue
                ? $"Task runtime readiness blocked since {snapshot.ReadinessBlockedSinceUtc.Value:O}"
                : "Task runtime readiness blocked";
            return Task.FromResult(HealthCheckResult.Degraded(message, data: data));
        }

        if (snapshot.UnhealthyRuntimes > 0 || snapshot.DegradedRuntimes > 0)
        {
            return Task.FromResult(HealthCheckResult.Degraded("Task runtime pool is degraded", data: data));
        }

        return Task.FromResult(HealthCheckResult.Healthy("Task runtime pool is healthy", data: data));
    }
}
