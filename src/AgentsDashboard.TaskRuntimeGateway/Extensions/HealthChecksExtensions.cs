using AgentsDashboard.TaskRuntimeGateway.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text.Json;

namespace AgentsDashboard.TaskRuntimeGateway;

internal static class HealthChecksExtensions
{
    public static IServiceCollection AddTaskRuntimeGatewayHealthChecks(this IServiceCollection services)
    {
        services
            .AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live", "ready"])
            .AddCheck<DockerHealthCheckService>("docker", tags: ["ready"]);

        return services;
    }

    public static WebApplication MapTaskRuntimeGatewayHealthChecks(this WebApplication app)
    {
        var readyHealthCheckOptions = new HealthCheckOptions
        {
            Predicate = static registration => registration.Tags.Contains("ready"),
        }.WithJsonResponseWriter();

        var liveHealthCheckOptions = new HealthCheckOptions
        {
            Predicate = static registration => registration.Tags.Contains("live"),
        }.WithJsonResponseWriter();

        app.MapHealthChecks("/health", readyHealthCheckOptions);
        app.MapHealthChecks("/ready", readyHealthCheckOptions);
        app.MapHealthChecks("/alive", liveHealthCheckOptions);

        return app;
    }

    private static HealthCheckOptions WithJsonResponseWriter(this HealthCheckOptions options)
    {
        options.ResponseWriter = WriteHealthResponseAsync;
        return options;
    }

    private static Task WriteHealthResponseAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString(),
                    durationMs = entry.Value.Duration.TotalMilliseconds,
                    description = entry.Value.Description,
                    error = entry.Value.Exception?.Message,
                })
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
