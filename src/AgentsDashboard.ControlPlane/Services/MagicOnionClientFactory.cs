using System.Collections.Concurrent;
using AgentsDashboard.Contracts.TaskRuntime;
using Grpc.Net.Client;
using MagicOnion;
using MagicOnion.Client;

namespace AgentsDashboard.ControlPlane.Services;

public interface IMagicOnionClientFactory
{
    ITaskRuntimeGatewayService CreateTaskRuntimeGatewayService(string workerId, string grpcAddress);
    Task<ITaskRuntimeEventHub> ConnectEventHubAsync(string workerId, string grpcAddress, ITaskRuntimeEventReceiver receiver, CancellationToken ct = default);
    void RemoveWorker(string workerId);
}

public class MagicOnionClientFactory : IMagicOnionClientFactory
{
    private readonly ConcurrentDictionary<string, ChannelEntry> _channels = new(StringComparer.OrdinalIgnoreCase);

    private static string NormalizeGrpcAddress(string address)
    {
        if (address.StartsWith("grpc://", StringComparison.OrdinalIgnoreCase))
        {
            return "http://" + address["grpc://".Length..];
        }

        if (address.StartsWith("grpcs://", StringComparison.OrdinalIgnoreCase))
        {
            return "https://" + address["grpcs://".Length..];
        }

        return address;
    }

    public ITaskRuntimeGatewayService CreateTaskRuntimeGatewayService(string workerId, string grpcAddress)
    {
        var channel = GetOrCreateChannel(workerId, grpcAddress);
        return MagicOnionClient.Create<ITaskRuntimeGatewayService>(channel);
    }

    public async Task<ITaskRuntimeEventHub> ConnectEventHubAsync(string workerId, string grpcAddress, ITaskRuntimeEventReceiver receiver, CancellationToken ct = default)
    {
        var channel = GetOrCreateChannel(workerId, grpcAddress);
        return await StreamingHubClient.ConnectAsync<ITaskRuntimeEventHub, ITaskRuntimeEventReceiver>(
            channel,
            receiver,
            cancellationToken: ct
        );
    }

    public void RemoveWorker(string workerId)
    {
        if (!_channels.TryRemove(workerId, out var entry))
        {
            return;
        }

        entry.Channel.Dispose();
    }

    private GrpcChannel GetOrCreateChannel(string workerId, string grpcAddress)
    {
        var normalized = NormalizeGrpcAddress(grpcAddress);
        var entry = _channels.AddOrUpdate(
            workerId,
            _ => new ChannelEntry(normalized, GrpcChannel.ForAddress(normalized)),
            (_, existing) =>
            {
                if (string.Equals(existing.Address, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return existing;
                }

                existing.Channel.Dispose();
                return new ChannelEntry(normalized, GrpcChannel.ForAddress(normalized));
            });

        return entry.Channel;
    }

    private sealed record ChannelEntry(string Address, GrpcChannel Channel);
}
