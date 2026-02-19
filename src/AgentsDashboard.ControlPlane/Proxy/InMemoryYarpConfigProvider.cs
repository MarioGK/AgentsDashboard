using System.Collections.Concurrent;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

namespace AgentsDashboard.ControlPlane.Proxy;


public sealed class InMemoryYarpConfigProvider : IProxyConfigProvider, IDisposable
{
    private volatile InMemoryConfig _config;
    private readonly ConcurrentDictionary<string, RouteConfig> _routes = new();
    private readonly ConcurrentDictionary<string, ClusterConfig> _clusters = new();
    private readonly ConcurrentDictionary<string, DateTime> _routeTtls = new();
    private readonly ConcurrentDictionary<string, RouteOwnership> _routeOwnership = new();
    private readonly Timer _cleanupTimer;

    public InMemoryYarpConfigProvider()
    {
        _config = new InMemoryConfig([], []);
        _cleanupTimer = new Timer(CleanupExpiredRoutes, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public IProxyConfig GetConfig() => _config;

    public void UpsertRoute(
        string routeId,
        string pathPattern,
        string destination,
        TimeSpan? ttl = null,
        string? repoId = null,
        string? taskId = null,
        string? runId = null)
    {
        if (!string.IsNullOrEmpty(runId))
        {
            var expectedPrefix = $"run-{runId}";
            if (!routeId.StartsWith(expectedPrefix) && routeId != expectedPrefix)
                throw new InvalidOperationException($"Route ID '{routeId}' must match run ownership pattern 'run-{{runId}}'");
        }

        var clusterId = $"cluster-{routeId}";
        _clusters[clusterId] = new ClusterConfig
        {
            ClusterId = clusterId,
            Destinations = new Dictionary<string, DestinationConfig>
            {
                ["d1"] = new() { Address = destination }
            }
        };

        _routes[routeId] = new RouteConfig
        {
            RouteId = routeId,
            ClusterId = clusterId,
            Match = new RouteMatch { Path = pathPattern }
        };

        if (ttl.HasValue)
            _routeTtls[routeId] = DateTime.UtcNow + ttl.Value;
        else
            _routeTtls.TryRemove(routeId, out _);

        if (!string.IsNullOrEmpty(repoId) ||
            !string.IsNullOrEmpty(taskId) || !string.IsNullOrEmpty(runId))
        {
            _routeOwnership[routeId] = new RouteOwnership
            {
                RepoId = repoId ?? string.Empty,
                TaskId = taskId ?? string.Empty,
                RunId = runId ?? string.Empty
            };
        }

        Refresh();
    }

    public RouteOwnership? GetRouteOwnership(string routeId)
    {
        return _routeOwnership.TryGetValue(routeId, out var ownership) ? ownership : null;
    }

    public void RemoveRoute(string routeId)
    {
        _routes.TryRemove(routeId, out _);
        _clusters.TryRemove($"cluster-{routeId}", out _);
        _routeTtls.TryRemove(routeId, out _);
        _routeOwnership.TryRemove(routeId, out _);
        Refresh();
    }

    private void CleanupExpiredRoutes(object? state)
    {
        var now = DateTime.UtcNow;
        var expired = _routeTtls.Where(kvp => kvp.Value < now).Select(kvp => kvp.Key).ToList();

        if (expired.Count == 0)
            return;

        foreach (var routeId in expired)
        {
            _routes.TryRemove(routeId, out _);
            _clusters.TryRemove($"cluster-{routeId}", out _);
            _routeTtls.TryRemove(routeId, out _);
            _routeOwnership.TryRemove(routeId, out _);
        }

        Refresh();
    }

    private void Refresh()
    {
        var oldConfig = _config;
        _config = new InMemoryConfig(_routes.Values.ToList(), _clusters.Values.ToList());
        oldConfig.SignalChange();
    }

    public void Dispose()
    {
        _cleanupTimer.Dispose();
    }

    private sealed class InMemoryConfig(IReadOnlyList<RouteConfig> routes, IReadOnlyList<ClusterConfig> clusters) : IProxyConfig
    {
        private readonly CancellationTokenSource _cts = new();

        public IReadOnlyList<RouteConfig> Routes { get; } = routes;
        public IReadOnlyList<ClusterConfig> Clusters { get; } = clusters;
        public IChangeToken ChangeToken => new CancellationChangeToken(_cts.Token);

        public void SignalChange() => _cts.Cancel();
    }
}
