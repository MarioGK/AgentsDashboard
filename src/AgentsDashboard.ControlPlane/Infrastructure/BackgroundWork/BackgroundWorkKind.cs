namespace AgentsDashboard.ControlPlane.Infrastructure.BackgroundWork;

public enum BackgroundWorkKind
{
    TaskRuntimeImageResolution = 0,
    LiteDbVectorBootstrap = 1,
    RepositoryGitRefresh = 2,
    Recovery = 3,
    Other = 4,
}
