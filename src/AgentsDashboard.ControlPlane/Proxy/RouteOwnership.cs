using System.Collections.Concurrent;
using Microsoft.Extensions.Primitives;
using Yarp.ReverseProxy.Configuration;

namespace AgentsDashboard.ControlPlane.Proxy;


public sealed class RouteOwnership
{
    public string RepoId { get; init; } = string.Empty;
    public string TaskId { get; init; } = string.Empty;
    public string RunId { get; init; } = string.Empty;
}
