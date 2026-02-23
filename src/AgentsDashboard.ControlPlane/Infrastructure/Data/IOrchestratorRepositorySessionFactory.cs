namespace AgentsDashboard.ControlPlane.Infrastructure.Data;

public interface IOrchestratorRepositorySessionFactory
{
    ValueTask<OrchestratorRepositorySession> CreateAsync(CancellationToken cancellationToken);
}
