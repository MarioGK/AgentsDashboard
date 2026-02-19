using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.ControlPlane.Services;

public enum GlobalSearchKind
{
    Task = 0,
    Run = 1,
    Finding = 2,
    RunLog = 3
}




public sealed record GlobalSearchKindCount(
    GlobalSearchKind Kind,
    int Count);
