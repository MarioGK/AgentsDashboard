namespace AgentsDashboard.ControlPlane.Data;

public sealed class LiteDbScopeFactory(
    IServiceProvider serviceProvider,
    LiteDbExecutor executor,
    LiteDbDatabase database) : ILiteDbScopeFactory
{
    public Task<LiteDbScope> CreateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new LiteDbScope(serviceProvider, executor, database));
    }
}
