

namespace AgentsDashboard.ControlPlane.Infrastructure.Data;

public interface ISemanticChunkRepository
{
    Task UpsertAsync(string taskId, IReadOnlyList<SemanticChunkDocument> chunks, CancellationToken cancellationToken);
    Task<List<SemanticChunkDocument>> SearchAsync(string taskId, string queryText, string? queryEmbeddingPayload, int limit, CancellationToken cancellationToken);
}
