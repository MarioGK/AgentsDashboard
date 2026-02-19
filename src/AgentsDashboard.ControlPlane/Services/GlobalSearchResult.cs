using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.ControlPlane.Services;

public enum GlobalSearchKind
{
    Task = 0,
    Run = 1,
    Finding = 2,
    RunLog = 3
}




public sealed record GlobalSearchResult(
    string Query,
    bool LiteDbVectorAvailable,
    string? LiteDbVectorDetail,
    int TotalMatches,
    IReadOnlyList<GlobalSearchKindCount> CountsByKind,
    IReadOnlyList<GlobalSearchHit> Hits);
