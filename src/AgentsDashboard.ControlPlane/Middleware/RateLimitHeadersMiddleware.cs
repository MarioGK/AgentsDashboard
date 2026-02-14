using System.Threading.RateLimiting;

namespace AgentsDashboard.ControlPlane.Middleware;

public sealed class RateLimitHeadersMiddleware : IMiddleware
{
    private static readonly string LimitMetadataKey = "RateLimit-Limit";
    private static readonly string RemainingMetadataKey = "RateLimit-Remaining";

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        await next(context);

        var rateLimitLease = context.Features.Get<RateLimitLease>();

        if (rateLimitLease != null)
        {
            if (rateLimitLease.TryGetMetadata(LimitMetadataKey, out var limit))
            {
                context.Response.Headers["X-RateLimit-Limit"] = limit?.ToString();
            }

            if (rateLimitLease.TryGetMetadata(RemainingMetadataKey, out var remaining))
            {
                context.Response.Headers["X-RateLimit-Remaining"] = remaining?.ToString();
            }
        }
    }
}
