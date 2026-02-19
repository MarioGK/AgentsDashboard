namespace AgentsDashboard.ControlPlane.Data;

public interface ILiteDbSet
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
