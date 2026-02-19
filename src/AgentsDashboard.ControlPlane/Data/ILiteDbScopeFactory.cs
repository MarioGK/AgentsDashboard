namespace AgentsDashboard.ControlPlane.Data;

public interface ILiteDbScopeFactory
{
    ValueTask<OrchestratorRepositorySession> CreateAsync(CancellationToken cancellationToken);
}
