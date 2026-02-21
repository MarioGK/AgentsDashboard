using System.Globalization;
using System.Text.Json;
using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.ControlPlane.Data;

public sealed class SemanticChunkRepository(
    IRepository<TaskDocument> tasks,
    IRepository<SemanticChunkDocument> semanticChunks) : ISemanticChunkRepository
{
    public async Task UpsertAsync(string taskId, IReadOnlyList<SemanticChunkDocument> chunks, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId) || chunks.Count == 0)
        {
            return;
        }

        var repositoryId = await tasks.QueryAsync(
            query => query
                .Where(x => x.Id == taskId)
                .Select(x => x.RepositoryId)
                .FirstOrDefault() ?? string.Empty,
            cancellationToken);

        var now = DateTime.UtcNow;
        var normalizedChunks = chunks
            .Where(x => !string.IsNullOrWhiteSpace(x.Content))
            .Select(x =>
            {
                x.TaskId = taskId;
                x.RepositoryId = string.IsNullOrWhiteSpace(x.RepositoryId) ? repositoryId : x.RepositoryId;
                x.ChunkKey = string.IsNullOrWhiteSpace(x.ChunkKey) ? $"{x.SourceRef}:{x.ChunkIndex}" : x.ChunkKey;
                x.Id = string.IsNullOrWhiteSpace(x.Id) ? Guid.NewGuid().ToString("N") : x.Id;
                x.CreatedAtUtc = x.CreatedAtUtc == default ? now : x.CreatedAtUtc;
                x.UpdatedAtUtc = now;

                if (x.EmbeddingDimensions <= 0)
                {
                    var parsedEmbedding = ParseEmbeddingPayload(x.EmbeddingPayload);
                    if (parsedEmbedding is not null)
                    {
                        x.EmbeddingDimensions = parsedEmbedding.Length;
                    }
                }

                return x;
            })
            .ToList();

        if (normalizedChunks.Count == 0)
        {
            return;
        }

        var normalizedChunksByKey = normalizedChunks
            .GroupBy(x => x.ChunkKey, StringComparer.Ordinal)
            .Select(group => group.Last())
            .ToList();
        var chunkKeys = normalizedChunksByKey.Select(x => x.ChunkKey).ToHashSet(StringComparer.Ordinal);
        var existingChunks = await semanticChunks.QueryAsync(
            query => query
                .Where(x => x.TaskId == taskId && chunkKeys.Contains(x.ChunkKey))
                .ToList(),
            cancellationToken);
        var existingByChunkKey = existingChunks.ToDictionary(x => x.ChunkKey, StringComparer.Ordinal);

        foreach (var chunk in normalizedChunksByKey)
        {
            if (existingByChunkKey.TryGetValue(chunk.ChunkKey, out var existing))
            {
                existing.RepositoryId = chunk.RepositoryId;
                existing.TaskId = chunk.TaskId;
                existing.RunId = chunk.RunId;
                existing.SourceType = chunk.SourceType;
                existing.SourceRef = chunk.SourceRef;
                existing.ChunkIndex = chunk.ChunkIndex;
                existing.Content = chunk.Content;
                existing.ContentHash = chunk.ContentHash;
                existing.TokenCount = chunk.TokenCount;
                existing.EmbeddingModel = chunk.EmbeddingModel;
                existing.EmbeddingDimensions = chunk.EmbeddingDimensions;
                existing.EmbeddingPayload = chunk.EmbeddingPayload;
                existing.UpdatedAtUtc = now;
                await semanticChunks.UpsertAsync(existing, cancellationToken);
                continue;
            }

            await semanticChunks.UpsertAsync(chunk, cancellationToken);
        }
    }

    public async Task<List<SemanticChunkDocument>> SearchAsync(
        string taskId,
        string queryText,
        string? queryEmbeddingPayload,
        int limit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return [];
        }

        var normalizedLimit = Math.Clamp(limit, 1, 200);
        var chunks = await semanticChunks.QueryAsync(
            query => query
                .Where(x => x.TaskId == taskId)
                .ToList(),
            cancellationToken);

        if (chunks.Count == 0)
        {
            return [];
        }

        var queryEmbedding = ParseEmbeddingPayload(queryEmbeddingPayload);
        if (queryEmbedding is { Length: > 0 })
        {
            var semanticMatches = chunks
                .Select(chunk => new
                {
                    Chunk = chunk,
                    Score = ComputeCosineSimilarity(queryEmbedding, ParseEmbeddingPayload(chunk.EmbeddingPayload))
                })
                .Where(x => x.Score.HasValue)
                .OrderByDescending(x => x.Score!.Value)
                .ThenByDescending(x => x.Chunk.UpdatedAtUtc)
                .Take(normalizedLimit)
                .Select(x => x.Chunk)
                .ToList();

            if (semanticMatches.Count > 0)
            {
                return semanticMatches;
            }
        }

        var normalizedQuery = queryText.Trim();
        if (normalizedQuery.Length > 0)
        {
            var textMatches = chunks
                .Where(x =>
                    x.Content.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
                    x.SourceRef.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
                    x.ChunkKey.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.UpdatedAtUtc)
                .Take(normalizedLimit)
                .ToList();

            if (textMatches.Count > 0)
            {
                return textMatches;
            }
        }

        return chunks
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(normalizedLimit)
            .ToList();
    }

    private static double[]? ParseEmbeddingPayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        var trimmed = payload.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (trimmed.StartsWith("[", StringComparison.Ordinal))
        {
            try
            {
                return JsonSerializer.Deserialize<double[]>(trimmed, (JsonSerializerOptions?)null);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        var parts = trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        var result = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                return null;
            }

            result[i] = parsed;
        }

        return result;
    }

    private static double? ComputeCosineSimilarity(double[] queryEmbedding, double[]? candidateEmbedding)
    {
        if (candidateEmbedding is null || candidateEmbedding.Length == 0)
        {
            return null;
        }

        if (queryEmbedding.Length != candidateEmbedding.Length)
        {
            return null;
        }

        var dot = 0d;
        var queryNorm = 0d;
        var candidateNorm = 0d;

        for (var i = 0; i < queryEmbedding.Length; i++)
        {
            var queryValue = queryEmbedding[i];
            var candidateValue = candidateEmbedding[i];
            dot += queryValue * candidateValue;
            queryNorm += queryValue * queryValue;
            candidateNorm += candidateValue * candidateValue;
        }

        if (queryNorm <= 0d || candidateNorm <= 0d)
        {
            return null;
        }

        return dot / (Math.Sqrt(queryNorm) * Math.Sqrt(candidateNorm));
    }
}
