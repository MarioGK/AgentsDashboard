using MagicOnion;
using MagicOnion.Client;
using Grpc.Net.Client;
using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.ControlPlane.Configuration;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.ControlPlane.Services;

public interface IMagicOnionClientFactory
{
    IWorkerGatewayService CreateWorkerGatewayService();
    Task<IWorkerEventHub> ConnectEventHubAsync(IWorkerEventReceiver receiver, CancellationToken ct = default);
    Task<ITerminalHub> ConnectTerminalHubAsync(ITerminalReceiver receiver, CancellationToken ct = default);
}

public class MagicOnionClientFactory : IMagicOnionClientFactory
{
    private readonly GrpcChannel _channel;

    public MagicOnionClientFactory(IOptions<OrchestratorOptions> options)
    {
        var workerGatewayUrl = options.Value.WorkerGrpcAddress;
        _channel = GrpcChannel.ForAddress(workerGatewayUrl);
    }

    public IWorkerGatewayService CreateWorkerGatewayService()
    {
        return MagicOnionClient.Create<IWorkerGatewayService>(_channel);
    }

    public async Task<IWorkerEventHub> ConnectEventHubAsync(IWorkerEventReceiver receiver, CancellationToken ct = default)
    {
        return await StreamingHubClient.ConnectAsync<IWorkerEventHub, IWorkerEventReceiver>(
            _channel,
            receiver,
            cancellationToken: ct
        );
    }

    public async Task<ITerminalHub> ConnectTerminalHubAsync(ITerminalReceiver receiver, CancellationToken ct = default)
    {
        return await StreamingHubClient.ConnectAsync<ITerminalHub, ITerminalReceiver>(
            _channel,
            receiver,
            cancellationToken: ct
        );
    }
}
