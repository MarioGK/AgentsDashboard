using System.Diagnostics;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;

namespace AgentsDashboard.ControlPlane.Proxy;

public class ProxyAuditMiddleware(
    IOrchestratorStore store,
    ILogger<ProxyAuditMiddleware> logger) : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!context.Request.Path.StartsWithSegments("/proxy"))
        {
            await next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        await next(context);
        stopwatch.Stop();

        try
        {
            var runId = ExtractRouteValue(context, "runId") ?? string.Empty;
            var taskId = ExtractRouteValue(context, "taskId") ?? string.Empty;
            var repoId = ExtractRouteValue(context, "repoId") ?? string.Empty;

            var upstreamTarget = string.Empty;
            try
            {
                var proxyFeature = context.Features.Get<Yarp.ReverseProxy.Model.IReverseProxyFeature>();
                if (proxyFeature?.ProxiedDestination is { } dest)
                {
                    upstreamTarget = dest.DestinationId;
                }
            }
            catch { }

            var auditDocument = new ProxyAuditDocument
            {
                RunId = runId,
                TaskId = taskId,
                RepoId = repoId,
                Path = context.Request.Path,
                UpstreamTarget = upstreamTarget,
                StatusCode = context.Response.StatusCode,
                LatencyMs = stopwatch.Elapsed.TotalMilliseconds,
            };

            _ = Task.Run(async () =>
            {
                try
                {
                    await store.RecordProxyRequestAsync(auditDocument, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.ZLogError(ex, "Failed to record proxy audit for path {Path}", context.Request.Path);
                }
            });
        }
        catch (Exception ex)
        {
            logger.ZLogWarning(ex, "Failed to extract proxy audit metadata from path {Path}", context.Request.Path);
        }
    }

    private static string? ExtractRouteValue(HttpContext context, string key)
    {
        if (context.Request.RouteValues.TryGetValue(key, out var value))
            return value?.ToString();

        var pathSegments = context.Request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pathSegments is null || pathSegments.Length < 2)
            return null;

        if (key == "runId" && pathSegments.Length > 1)
            return pathSegments[1];

        return null;
    }
}
