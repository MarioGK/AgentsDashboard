namespace AgentsDashboard.ControlPlane.Data;

public interface ILiteDbScopeFactory
{
    Task<LiteDbScope> CreateAsync(CancellationToken cancellationToken);
}
