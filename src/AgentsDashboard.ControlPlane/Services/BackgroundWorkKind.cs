namespace AgentsDashboard.ControlPlane.Services;

public enum BackgroundWorkKind
{
    WorkerImageResolution = 0,
    SqliteVecBootstrap = 1,
    RepositoryGitRefresh = 2,
    Recovery = 3,
    TaskTemplateInit = 4,
    Other = 5,
}
