namespace AgentsDashboard.ControlPlane.Data;

public interface ILiteDbCollectionNameResolver
{
    LiteDbCollectionDefinition Resolve<T>();
}
