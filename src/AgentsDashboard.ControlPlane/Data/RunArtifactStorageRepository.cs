using LiteDB;

namespace AgentsDashboard.ControlPlane.Data;

public sealed class RunArtifactStorageRepository(LiteDbExecutor liteDbExecutor) : IRunArtifactStorage
{
    private const string ArtifactCollectionName = "run_artifacts";
    private const string ArtifactFileStorageRoot = "$/run-artifacts";

    public async Task SaveAsync(string runId, string fileName, Stream stream, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            throw new ArgumentException("Run id is required.", nameof(runId));
        }

        await using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;

        var metadata = new RunArtifactDocument
        {
            Id = BuildArtifactId(runId, fileName),
            RunId = runId,
            FileName = fileName,
            FileStorageId = BuildArtifactFileStorageId(runId, fileName),
            CreatedAtUtc = DateTime.UtcNow,
        };

        await liteDbExecutor.ExecuteAsync(
            db =>
            {
                db.FileStorage.Upload(metadata.FileStorageId, fileName, memory);
                var collection = db.GetCollection<RunArtifactDocument>(ArtifactCollectionName);
                collection.EnsureIndex(x => x.RunId);
                collection.EnsureIndex(x => x.FileName);
                collection.Upsert(metadata);
            },
            cancellationToken);
    }

    public Task<List<string>> ListAsync(string runId, CancellationToken cancellationToken)
    {
        return liteDbExecutor.ExecuteAsync(
            db =>
            {
                if (string.IsNullOrWhiteSpace(runId))
                {
                    return new List<string>();
                }

                var metadataCollection = db.GetCollection<RunArtifactDocument>(ArtifactCollectionName);
                metadataCollection.EnsureIndex(x => x.RunId);
                return metadataCollection.Find(x => x.RunId == runId)
                    .Select(x => x.FileName)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            },
            cancellationToken);
    }

    public async Task<Stream?> GetAsync(string runId, string fileName, CancellationToken cancellationToken)
    {
        var payload = await liteDbExecutor.ExecuteAsync(
            db =>
            {
                var metadataCollection = db.GetCollection<RunArtifactDocument>(ArtifactCollectionName);
                var metadata = metadataCollection.FindById(BuildArtifactId(runId, fileName));
                if (metadata is null || string.IsNullOrWhiteSpace(metadata.FileStorageId))
                {
                    return null;
                }

                if (!db.FileStorage.Exists(metadata.FileStorageId))
                {
                    return null;
                }

                using var fileStream = db.FileStorage.OpenRead(metadata.FileStorageId);
                using var memory = new MemoryStream();
                fileStream.CopyTo(memory);
                return memory.ToArray();
            },
            cancellationToken);

        return payload is null ? null : new MemoryStream(payload, writable: false);
    }

    public Task DeleteByRunIdsAsync(IReadOnlyList<string> runIds, CancellationToken cancellationToken)
    {
        if (runIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        return liteDbExecutor.ExecuteAsync(
            db =>
            {
                var metadataCollection = db.GetCollection<RunArtifactDocument>(ArtifactCollectionName);
                metadataCollection.EnsureIndex(x => x.RunId);

                foreach (var runId in runIds.Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var artifacts = metadataCollection.Find(x => x.RunId == runId).ToList();
                    foreach (var artifact in artifacts)
                    {
                        if (!string.IsNullOrWhiteSpace(artifact.FileStorageId) && db.FileStorage.Exists(artifact.FileStorageId))
                        {
                            db.FileStorage.Delete(artifact.FileStorageId);
                        }

                        metadataCollection.Delete(artifact.Id);
                    }
                }
            },
            cancellationToken);
    }

    private static string BuildArtifactId(string runId, string fileName)
    {
        return $"{runId.Trim()}::{fileName}";
    }

    private static string BuildArtifactFileStorageId(string runId, string fileName)
    {
        return $"{ArtifactFileStorageRoot}/{runId.Trim()}/{fileName}";
    }
}
