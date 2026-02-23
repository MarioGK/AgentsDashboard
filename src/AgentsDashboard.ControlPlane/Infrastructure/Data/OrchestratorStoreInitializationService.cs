using Microsoft.Extensions.Hosting;

namespace AgentsDashboard.ControlPlane.Data;

public sealed class OrchestratorStoreInitializationService(
    IOrchestratorStore store) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await store.InitializeAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
