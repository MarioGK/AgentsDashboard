namespace AgentsDashboard.ControlPlane.Data;

public interface ITrackedRepositorySet
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
