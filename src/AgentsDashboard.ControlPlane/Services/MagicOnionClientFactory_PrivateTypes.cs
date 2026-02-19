using System.Collections.Concurrent;
using AgentsDashboard.Contracts.TaskRuntime;
using Grpc.Net.Client;
using MagicOnion;
using MagicOnion.Client;

namespace AgentsDashboard.ControlPlane.Services;

public partial class MagicOnionClientFactory : IMagicOnionClientFactory
{
    private sealed record ChannelEntry(string Address, GrpcChannel Channel);
}
