namespace AgentsDashboard.ControlPlane.Infrastructure.Data;

public interface ITrackedRepositorySet
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
