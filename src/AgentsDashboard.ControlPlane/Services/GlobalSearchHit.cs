using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.ControlPlane.Services;

public enum GlobalSearchKind
{
    Task = 0,
    Run = 1,
    Finding = 2,
    RunLog = 3
}




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
