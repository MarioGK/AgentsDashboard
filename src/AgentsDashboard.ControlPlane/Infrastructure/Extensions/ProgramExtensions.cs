using System.IO;
using System.Linq;
using System.Text.Json;
using AgentsDashboard.ControlPlane.Configuration;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AgentsDashboard.ControlPlane;

internal static class ProgramExtensions
{
    public static void EnsureArtifactsDirectoryExists(this string? artifactsRootPath)
    {
        if (string.IsNullOrWhiteSpace(artifactsRootPath))
        {
            return;
        }

        var resolvedPath = RepositoryPathResolver.ResolveDataPath(
            artifactsRootPath,
            OrchestratorOptions.DefaultArtifactsRootPath);

        if (resolvedPath == ":memory:")
        {
            return;
        }

        Directory.CreateDirectory(resolvedPath);
    }

    public static void EnsureLiteDbDirectoryExists(this string? liteDbPath)
    {
        if (string.IsNullOrWhiteSpace(liteDbPath))
        {
            return;
        }

        var resolvedPath = RepositoryPathResolver.ResolveDataPath(
            liteDbPath,
            OrchestratorOptions.DefaultLiteDbPath);

        if (resolvedPath == ":memory:")
        {
            return;
        }

        var directory = Path.GetDirectoryName(resolvedPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
    }

    public static HealthCheckOptions WithJsonResponseWriter(this HealthCheckOptions options)
    {
        options.ResponseWriter = static (context, report) => context.WriteHealthResponseAsync(report);
        return options;
    }

    public static Task WriteHealthResponseAsync(this HttpContext context, HealthReport report)
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
                    error = entry.Value.Exception?.Message
                })
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
