using System.Text;
using System.Text.RegularExpressions;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;

namespace AgentsDashboard.ControlPlane.Services;

public sealed partial class GlobalSearchService
    IRepository<RepositoryDocument> repositoryDocuments,
    IRepository<TaskDocument> taskDocuments,
    IRepository<RunDocument> runDocuments,
    IRepository<FindingDocument> findingDocuments,
    IRepository<RunLogEvent> runEvents,
    ISemanticChunkRepository semanticChunkRepository,
    IWorkspaceAiService workspaceAiService,
    IHarnessOutputParserService parserService,
    ILiteDbVectorSearchStatusService vectorSearchStatusService,
    ILogger<GlobalSearchService> logger) : IGlobalSearchService
{
    private sealed record SearchCandidate(
        GlobalSearchKind Kind,
        string Id,
        string RepositoryId,
        string RepositoryName,
        string? TaskId,
        string? TaskName,
        string? RunId,
        string Title,
        string Body,
        string? State,
        DateTime TimestampUtc,
        string? SemanticKey);
}
