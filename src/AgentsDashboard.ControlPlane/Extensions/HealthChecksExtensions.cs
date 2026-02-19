using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AgentsDashboard.ControlPlane;

internal static class HealthChecksExtensions
{
    public static IServiceCollection AddControlPlaneHealthChecks(this IServiceCollection services)
    {
        services
            .AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live", "ready"])
            .AddCheck<DatabaseReadyHealthCheck>("database", tags: ["ready"]);

        return services;
    }

    public static WebApplication MapControlPlaneHealthChecks(this WebApplication app)
    {
        var readyHealthCheckOptions = new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready"),
        }
        .WithJsonResponseWriter();

        var liveHealthCheckOptions = new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("live"),
        }
        .WithJsonResponseWriter();

        app.MapHealthChecks("/health", readyHealthCheckOptions);
        app.MapHealthChecks("/ready", readyHealthCheckOptions);
        app.MapHealthChecks("/alive", liveHealthCheckOptions);

        return app;
    }
}
