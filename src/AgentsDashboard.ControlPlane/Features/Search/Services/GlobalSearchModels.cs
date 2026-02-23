

namespace AgentsDashboard.ControlPlane.Features.Search.Services;

public enum GlobalSearchKind
{
    Task = 0,
    Run = 1,
    RunLog = 2
}




public sealed record GlobalSearchRequest(
    string Query,
    string? RepositoryId = null,
    string? TaskId = null,
    IReadOnlyList<GlobalSearchKind>? Kinds = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    RunState? RunStateFilter = null,
    int Limit = 50,
    bool IncludeRunLogs = true);
