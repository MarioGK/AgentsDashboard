namespace AgentsDashboard.ControlPlane.Infrastructure.Data;

public interface ILiteDbCollectionNameResolver
{
    LiteDbCollectionDefinition Resolve<T>();
}
