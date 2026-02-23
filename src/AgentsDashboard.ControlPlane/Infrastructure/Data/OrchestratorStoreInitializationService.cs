using Microsoft.Extensions.Hosting;

namespace AgentsDashboard.ControlPlane.Infrastructure.Data;

public sealed class OrchestratorStoreInitializationService(
    ISystemStore store) : IHostedService
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
