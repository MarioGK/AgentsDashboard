using AgentsDashboard.ControlPlane.Data;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class MongoInitializationService(IOrchestratorStore store, ILogger<MongoInitializationService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await store.InitializeAsync(cancellationToken);
        logger.LogInformation("Mongo collections and indexes initialized");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
