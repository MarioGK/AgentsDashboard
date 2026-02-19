using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.ControlPlane.Services;

public enum GlobalSearchKind
{
    Task = 0,
    Run = 1,
    Finding = 2,
    RunLog = 3
}

public sealed record GlobalSearchRequest(
    string Query,
    string? RepositoryId = null,
    string? TaskId = null,
    IReadOnlyList<GlobalSearchKind>? Kinds = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    RunState? RunStateFilter = null,
    FindingState? FindingStateFilter = null,
    int Limit = 50,
    bool IncludeRunLogs = true);

public sealed record GlobalSearchResult(
    string Query,
    bool LiteDbVectorAvailable,
    string? LiteDbVectorDetail,
    int TotalMatches,
    IReadOnlyList<GlobalSearchKindCount> CountsByKind,
    IReadOnlyList<GlobalSearchHit> Hits);

public sealed record GlobalSearchKindCount(
    GlobalSearchKind Kind,
    int Count);

public sealed record GlobalSearchHit(
    GlobalSearchKind Kind,
    string Id,
    string RepositoryId,
    string RepositoryName,
    string? TaskId,
    string? TaskName,
    string? RunId,
    string Title,
    string Snippet,
    string? State,
    DateTime TimestampUtc,
    double Score,
    double KeywordScore,
    double SemanticScore);
