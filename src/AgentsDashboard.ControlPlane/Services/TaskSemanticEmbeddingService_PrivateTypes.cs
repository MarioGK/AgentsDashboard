using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;

namespace AgentsDashboard.ControlPlane.Services;

public sealed partial class TaskSemanticEmbeddingService
    IRepository<TaskDocument> tasks,
    IRepository<WorkspacePromptEntryDocument> workspacePromptEntries,
    IRepository<RunDocument> runs,
    ISemanticChunkRepository semanticChunks,
    IWorkspaceAiService workspaceAiService,
    ILogger<TaskSemanticEmbeddingService> logger) : BackgroundService, ITaskSemanticEmbeddingService
{
    private sealed record PendingTaskEmbedding(
        string RepositoryId,
        string TaskId,
        string Reason,
        string? RunId,
        string? PromptEntryId,
        DateTime LastQueuedAtUtc,
        long Sequence);
}
