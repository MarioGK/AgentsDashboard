namespace AgentsDashboard.ControlPlane.Data;

public sealed class LiteDbEntityEntry<T>(T entity)
    where T : class
{
    public LiteDbPropertyValues CurrentValues { get; } = new(entity);
}
