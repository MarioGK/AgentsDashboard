namespace AgentsDashboard.ControlPlane.Data;

public interface IOrchestratorRepositorySessionFactory
{
    ValueTask<OrchestratorRepositorySession> CreateAsync(CancellationToken cancellationToken);
}
