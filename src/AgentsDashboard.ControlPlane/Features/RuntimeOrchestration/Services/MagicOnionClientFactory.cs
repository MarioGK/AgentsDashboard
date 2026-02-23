using System.Collections.Concurrent;

using Grpc.Net.Client;
using MagicOnion;
using MagicOnion.Client;

namespace AgentsDashboard.ControlPlane.Features.RuntimeOrchestration.Services;

public interface IMagicOnionClientFactory
{
    ITaskRuntimeService CreateTaskRuntimeService(string runtimeId, string grpcAddress);
    Task<ITaskRuntimeEventHub> ConnectEventHubAsync(string runtimeId, string grpcAddress, ITaskRuntimeEventReceiver receiver, CancellationToken ct = default);
    void RemoveTaskRuntime(string runtimeId);
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

    public ITaskRuntimeService CreateTaskRuntimeService(string runtimeId, string grpcAddress)
    {
        var channel = GetOrCreateChannel(runtimeId, grpcAddress);
        return MagicOnionClient.Create<ITaskRuntimeService>(channel);
    }

    public async Task<ITaskRuntimeEventHub> ConnectEventHubAsync(string runtimeId, string grpcAddress, ITaskRuntimeEventReceiver receiver, CancellationToken ct = default)
    {
        var channel = GetOrCreateChannel(runtimeId, grpcAddress);
        return await StreamingHubClient.ConnectAsync<ITaskRuntimeEventHub, ITaskRuntimeEventReceiver>(
            channel,
            receiver,
            cancellationToken: ct
        );
    }

    public void RemoveTaskRuntime(string runtimeId)
    {
        if (!_channels.TryRemove(runtimeId, out var entry))
        {
            return;
        }

        entry.Channel.Dispose();
    }

    private GrpcChannel GetOrCreateChannel(string runtimeId, string grpcAddress)
    {
        var normalized = NormalizeGrpcAddress(grpcAddress);
        var entry = _channels.AddOrUpdate(
            runtimeId,
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
