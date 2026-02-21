using System.Security.Cryptography;
using System.Text.Json;
using AgentsDashboard.Contracts.TaskRuntime;
using AgentsDashboard.TaskRuntime.Configuration;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.TaskRuntime.Services;

public sealed class TaskRuntimeArtifactStreamService(
    TaskRuntimeEventBus eventBus,
    IOptions<TaskRuntimeOptions> options,
    ILogger<TaskRuntimeArtifactStreamService> logger)
{
    private static readonly Dictionary<string, string> s_contentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".txt"] = "text/plain",
        [".log"] = "text/plain",
        [".md"] = "text/markdown",
        [".json"] = "application/json",
        [".xml"] = "application/xml",
        [".yaml"] = "application/x-yaml",
        [".yml"] = "application/x-yaml",
        [".diff"] = "text/plain",
        [".patch"] = "text/plain",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
    };

    public async Task<List<string>> StreamArtifactsAsync(
        string runId,
        string taskId,
        string executionToken,
        IReadOnlyList<string> artifactPaths,
        CancellationToken cancellationToken)
    {
        var fileNames = new List<string>();
        if (artifactPaths.Count == 0)
        {
            return fileNames;
        }

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long sequence = 0;

        foreach (var artifactPath in artifactPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(artifactPath))
            {
                continue;
            }

            if (!File.Exists(artifactPath))
            {
                logger.LogWarning("Skipping missing artifact path {ArtifactPath}", artifactPath);
                continue;
            }

            var fileInfo = new FileInfo(artifactPath);
            if (fileInfo.Length == 0)
            {
                continue;
            }

            var fileName = BuildUniqueFileName(Path.GetFileName(artifactPath), usedNames);
            var artifactId = Guid.NewGuid().ToString("N");
            var contentType = ResolveContentType(fileName);
            var chunkSize = ResolveChunkSize();
            var totalChunks = (int)Math.Ceiling(fileInfo.Length / (double)chunkSize);

            await eventBus.PublishAsync(
                CreateManifestEvent(
                    runId,
                    taskId,
                    executionToken,
                    artifactId,
                    fileName,
                    contentType,
                    fileInfo.Length,
                    totalChunks,
                    ++sequence),
                cancellationToken);

            using var stream = new FileStream(
                artifactPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: chunkSize,
                useAsync: true);

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[chunkSize];
            var chunkIndex = 0;

            while (true)
            {
                var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (bytesRead <= 0)
                {
                    break;
                }

                hash.AppendData(buffer, 0, bytesRead);
                var payload = new byte[bytesRead];
                Buffer.BlockCopy(buffer, 0, payload, 0, bytesRead);

                var isLastChunk = stream.Position >= stream.Length;
                await eventBus.PublishAsync(
                    CreateChunkEvent(
                        runId,
                        taskId,
                        executionToken,
                        artifactId,
                        fileName,
                        payload,
                        chunkIndex,
                        isLastChunk,
                        contentType,
                        ++sequence),
                    cancellationToken);

                chunkIndex++;
            }

            var sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
            await eventBus.PublishAsync(
                CreateCommitEvent(
                    runId,
                    taskId,
                    executionToken,
                    artifactId,
                    fileName,
                    contentType,
                    fileInfo.Length,
                    sha256,
                    ++sequence),
                cancellationToken);

            fileNames.Add(fileName);
        }

        return fileNames;
    }

    private JobEventMessage CreateManifestEvent(
        string runId,
        string taskId,
        string executionToken,
        string artifactId,
        string fileName,
        string contentType,
        long sizeBytes,
        int totalChunks,
        long sequence)
    {
        var payload = JsonSerializer.Serialize(new
        {
            artifactId,
            fileName,
            contentType,
            sizeBytes,
            totalChunks,
        });

        return new JobEventMessage
        {
            RunId = runId,
            TaskId = taskId,
            ExecutionToken = executionToken,
            EventType = "artifact_manifest",
            Summary = fileName,
            Error = null,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Metadata = null,
            Sequence = sequence,
            Category = "artifact.manifest",
            PayloadJson = payload,
            SchemaVersion = "runtime-artifact-v1",
            ArtifactId = artifactId,
            ChunkIndex = 0,
            IsLastChunk = false,
            BinaryPayload = null,
            ContentType = contentType,
        };
    }

    private JobEventMessage CreateChunkEvent(
        string runId,
        string taskId,
        string executionToken,
        string artifactId,
        string fileName,
        byte[] payload,
        int chunkIndex,
        bool isLastChunk,
        string contentType,
        long sequence)
    {
        return new JobEventMessage
        {
            RunId = runId,
            TaskId = taskId,
            ExecutionToken = executionToken,
            EventType = "artifact_chunk",
            Summary = fileName,
            Error = null,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Metadata = null,
            Sequence = sequence,
            Category = "artifact.chunk",
            PayloadJson = null,
            SchemaVersion = "runtime-artifact-v1",
            ArtifactId = artifactId,
            ChunkIndex = chunkIndex,
            IsLastChunk = isLastChunk,
            BinaryPayload = payload,
            ContentType = contentType,
        };
    }

    private JobEventMessage CreateCommitEvent(
        string runId,
        string taskId,
        string executionToken,
        string artifactId,
        string fileName,
        string contentType,
        long sizeBytes,
        string sha256,
        long sequence)
    {
        var payload = JsonSerializer.Serialize(new
        {
            artifactId,
            fileName,
            contentType,
            sizeBytes,
            sha256,
        });

        return new JobEventMessage
        {
            RunId = runId,
            TaskId = taskId,
            ExecutionToken = executionToken,
            EventType = "artifact_commit",
            Summary = fileName,
            Error = null,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Metadata = null,
            Sequence = sequence,
            Category = "artifact.commit",
            PayloadJson = payload,
            SchemaVersion = "runtime-artifact-v1",
            ArtifactId = artifactId,
            ChunkIndex = null,
            IsLastChunk = true,
            BinaryPayload = null,
            ContentType = contentType,
        };
    }

    private int ResolveChunkSize()
    {
        var chunkSize = options.Value.ArtifactChunkSizeBytes;
        if (chunkSize < 4096)
        {
            return 4096;
        }

        if (chunkSize > 1_048_576)
        {
            return 1_048_576;
        }

        return chunkSize;
    }

    private static string BuildUniqueFileName(string fileName, ISet<string> usedNames)
    {
        var baseName = string.IsNullOrWhiteSpace(fileName) ? "artifact.bin" : fileName.Trim();
        var normalizedBaseName = Path.GetFileName(baseName);
        if (!usedNames.Contains(normalizedBaseName))
        {
            usedNames.Add(normalizedBaseName);
            return normalizedBaseName;
        }

        var extension = Path.GetExtension(normalizedBaseName);
        var name = Path.GetFileNameWithoutExtension(normalizedBaseName);
        var index = 1;
        while (true)
        {
            var candidate = $"{name}_{index}{extension}";
            if (usedNames.Add(candidate))
            {
                return candidate;
            }

            index++;
        }
    }

    private static string ResolveContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "application/octet-stream";
        }

        return s_contentTypes.TryGetValue(extension, out var contentType)
            ? contentType
            : "application/octet-stream";
    }
}
