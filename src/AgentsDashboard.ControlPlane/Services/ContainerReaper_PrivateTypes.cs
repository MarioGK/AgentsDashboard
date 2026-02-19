using Docker.DotNet;
using Docker.DotNet.Models;

namespace AgentsDashboard.ControlPlane.Services;

public sealed partial class ContainerReaper
{
    private sealed record RunContainer(string ContainerId, string RunId);
}
