using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;

namespace AgentsDashboard.ControlPlane.Services;

public interface ITaskSemanticEmbeddingService
{
    void QueueTaskEmbedding(string repositoryId, string taskId, string reason, string? runId = null, string? promptEntryId = null);
    Task ReindexTaskAsync(string repositoryId, string taskId, CancellationToken cancellationToken);
}

public sealed class TaskSemanticEmbeddingService(
    IOrchestratorStore store,
    IWorkspaceAiService workspaceAiService,
    ILogger<TaskSemanticEmbeddingService> logger) : BackgroundService, ITaskSemanticEmbeddingService
{
    private static readonly TimeSpan s_debounceWindow = TimeSpan.FromSeconds(3);
    private const int MaxEmbeddingSegmentLength = 5000;
    private const int MaxArtifactTextBytes = 262_144;

    private readonly Channel<string> _taskQueue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false
    });
    private readonly ConcurrentDictionary<string, PendingTaskEmbedding> _pendingEmbeddings = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _scheduledTaskKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _contentHashes = new(StringComparer.Ordinal);
    private long _sequence;

    public void QueueTaskEmbedding(string repositoryId, string taskId, string reason, string? runId = null, string? promptEntryId = null)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return;
        }

        var normalizedTaskId = taskId.Trim();
        var normalizedRepositoryId = repositoryId?.Trim() ?? string.Empty;
        var sequence = Interlocked.Increment(ref _sequence);
        var now = DateTime.UtcNow;
        var taskKey = BuildTaskKey(normalizedRepositoryId, normalizedTaskId);
        var update = new PendingTaskEmbedding(
            normalizedRepositoryId,
            normalizedTaskId,
            reason?.Trim() ?? string.Empty,
            runId?.Trim(),
            promptEntryId?.Trim(),
            now,
            sequence);

        _pendingEmbeddings.AddOrUpdate(
            taskKey,
            _ => update,
            (_, existing) => update with
            {
                RepositoryId = string.IsNullOrWhiteSpace(update.RepositoryId) ? existing.RepositoryId : update.RepositoryId,
                Reason = MergeReasons(existing.Reason, update.Reason),
                RunId = string.IsNullOrWhiteSpace(update.RunId) ? existing.RunId : update.RunId,
                PromptEntryId = string.IsNullOrWhiteSpace(update.PromptEntryId) ? existing.PromptEntryId : update.PromptEntryId,
            });

        if (_scheduledTaskKeys.TryAdd(taskKey, 0))
        {
            if (!_taskQueue.Writer.TryWrite(taskKey))
            {
                _scheduledTaskKeys.TryRemove(taskKey, out _);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await _taskQueue.Reader.WaitToReadAsync(stoppingToken))
        {
            while (_taskQueue.Reader.TryRead(out var taskKey))
            {
                await ProcessTaskAsync(taskKey, stoppingToken);
            }
        }
    }

    public async Task ReindexTaskAsync(string repositoryId, string taskId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return;
        }

        var task = await store.GetTaskAsync(taskId, cancellationToken);
        if (task is null)
        {
            return;
        }

        var resolvedRepositoryId = string.IsNullOrWhiteSpace(repositoryId) ? task.RepositoryId : repositoryId;
        var promptEntriesTask = store.ListWorkspacePromptEntriesForEmbeddingAsync(taskId, cancellationToken);
        var completedRunsTask = store.ListCompletedRunsByTaskForEmbeddingAsync(taskId, cancellationToken);
        await Task.WhenAll(promptEntriesTask, completedRunsTask);

        var promptEntries = promptEntriesTask.Result
            .OrderBy(x => x.CreatedAtUtc)
            .ToList();
        var completedRuns = completedRunsTask.Result
            .OrderBy(x => x.CreatedAtUtc)
            .ToList();

        var chunks = new List<SemanticChunkDocument>();

        await AddTaskDefinitionChunksAsync(resolvedRepositoryId, task, chunks, cancellationToken);

        foreach (var promptEntry in promptEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(promptEntry.Content))
            {
                continue;
            }

            var promptContent = BuildPromptEntryContent(promptEntry);
            await AddContentChunksAsync(
                resolvedRepositoryId,
                taskId,
                promptEntry.RunId,
                $"prompt:{promptEntry.Id}",
                "user-message",
                promptEntry.Id,
                promptContent,
                chunks,
                cancellationToken);
        }

        foreach (var run in completedRuns)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(run.OutputJson))
            {
                var outputContent = BuildRunOutputContent(run);
                await AddContentChunksAsync(
                    resolvedRepositoryId,
                    taskId,
                    run.Id,
                    $"run:{run.Id}:output",
                    "run-output",
                    run.Id,
                    outputContent,
                    chunks,
                    cancellationToken);
            }

            var runLogsTask = store.ListRunLogsAsync(run.Id, cancellationToken);
            var structuredEventsTask = store.ListRunStructuredEventsAsync(run.Id, 5000, cancellationToken);
            var artifactNamesTask = store.ListArtifactsAsync(run.Id, cancellationToken);
            await Task.WhenAll(runLogsTask, structuredEventsTask, artifactNamesTask);

            var runLogs = runLogsTask.Result;
            if (runLogs.Count > 0)
            {
                var logsContent = BuildRunLogsContent(run, runLogs);
                await AddContentChunksAsync(
                    resolvedRepositoryId,
                    taskId,
                    run.Id,
                    $"run:{run.Id}:logs",
                    "run-log",
                    run.Id,
                    logsContent,
                    chunks,
                    cancellationToken);
            }

            var structuredEvents = structuredEventsTask.Result;
            if (structuredEvents.Count > 0)
            {
                var structuredContent = BuildRunStructuredEventsContent(run, structuredEvents);
                await AddContentChunksAsync(
                    resolvedRepositoryId,
                    taskId,
                    run.Id,
                    $"run:{run.Id}:structured",
                    "run-structured",
                    run.Id,
                    structuredContent,
                    chunks,
                    cancellationToken);
            }

            var artifactNames = artifactNamesTask.Result;
            if (artifactNames.Count > 0)
            {
                foreach (var artifactName in artifactNames.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(artifactName))
                    {
                        continue;
                    }

                    var fileExtension = Path.GetExtension(artifactName)?.Trim().ToLowerInvariant() ?? string.Empty;
                    var sourceRef = $"{run.Id}:{artifactName}";
                    if (IsTextArtifact(fileExtension))
                    {
                        var artifactText = await ReadArtifactTextAsync(run.Id, artifactName, cancellationToken);
                        if (artifactText.Length == 0)
                        {
                            continue;
                        }

                        var artifactContent = BuildTextArtifactContent(run, artifactName, artifactText);
                        await AddContentChunksAsync(
                            resolvedRepositoryId,
                            taskId,
                            run.Id,
                            $"run:{run.Id}:artifact:{artifactName}",
                            "run-artifact",
                            sourceRef,
                            artifactContent,
                            chunks,
                            cancellationToken);
                        continue;
                    }

                    if (IsImageArtifact(fileExtension))
                    {
                        var imageMetadataContent = BuildImageArtifactMetadataContent(run, artifactName, fileExtension);
                        await AddContentChunksAsync(
                            resolvedRepositoryId,
                            taskId,
                            run.Id,
                            $"run:{run.Id}:image:{artifactName}",
                            "run-image-metadata",
                            sourceRef,
                            imageMetadataContent,
                            chunks,
                            cancellationToken);
                    }
                }
            }
        }

        if (chunks.Count == 0)
        {
            logger.LogDebug("Task semantic embedding skipped for task {TaskId}: no changed chunks", taskId);
            return;
        }

        await store.UpsertSemanticChunksAsync(taskId, chunks, cancellationToken);
        logger.LogDebug(
            "Task semantic embedding upserted {ChunkCount} chunk rows for task {TaskId} in repository {RepositoryId}",
            chunks.Count,
            taskId,
            resolvedRepositoryId);
    }

    private async Task ProcessTaskAsync(string taskKey, CancellationToken stoppingToken)
    {
        while (true)
        {
            if (!_pendingEmbeddings.TryGetValue(taskKey, out var pending))
            {
                _scheduledTaskKeys.TryRemove(taskKey, out _);
                return;
            }

            var waitTime = pending.LastQueuedAtUtc + s_debounceWindow - DateTime.UtcNow;
            if (waitTime <= TimeSpan.Zero)
            {
                break;
            }

            await Task.Delay(waitTime, stoppingToken);
        }

        if (!_pendingEmbeddings.TryGetValue(taskKey, out var trigger))
        {
            _scheduledTaskKeys.TryRemove(taskKey, out _);
            return;
        }

        var processingSequence = trigger.Sequence;

        try
        {
            logger.LogDebug(
                "Processing task semantic embedding for task {TaskId} in repository {RepositoryId} ({Reason})",
                trigger.TaskId,
                trigger.RepositoryId,
                trigger.Reason);
            await ReindexTaskAsync(trigger.RepositoryId, trigger.TaskId, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Task semantic embedding failed for task {TaskId} in repository {RepositoryId}",
                trigger.TaskId,
                trigger.RepositoryId);
        }

        _scheduledTaskKeys.TryRemove(taskKey, out _);

        if (_pendingEmbeddings.TryGetValue(taskKey, out var latest))
        {
            if (latest.Sequence != processingSequence)
            {
                if (_scheduledTaskKeys.TryAdd(taskKey, 0))
                {
                    _taskQueue.Writer.TryWrite(taskKey);
                }
            }
            else
            {
                _pendingEmbeddings.TryRemove(taskKey, out _);
            }
        }
    }

    private async Task AddTaskDefinitionChunksAsync(
        string repositoryId,
        TaskDocument task,
        List<SemanticChunkDocument> chunks,
        CancellationToken cancellationToken)
    {
        var content = new StringBuilder()
            .AppendLine($"Task {task.Id}")
            .AppendLine($"Name: {task.Name}")
            .AppendLine($"Harness: {task.Harness}")
            .AppendLine("Prompt:")
            .AppendLine(task.Prompt)
            .AppendLine("Command:")
            .AppendLine(task.Command)
            .ToString();

        await AddContentChunksAsync(
            repositoryId,
            task.Id,
            runId: string.Empty,
            chunkKeyPrefix: $"task:{task.Id}:definition",
            sourceType: "task",
            sourceRef: task.Id,
            content,
            chunks,
            cancellationToken);
    }

    private async Task AddContentChunksAsync(
        string repositoryId,
        string taskId,
        string runId,
        string chunkKeyPrefix,
        string sourceType,
        string sourceRef,
        string content,
        List<SemanticChunkDocument> chunks,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        var normalized = NormalizeContent(content);
        if (normalized.Length == 0)
        {
            return;
        }

        var segments = SegmentContent(normalized, MaxEmbeddingSegmentLength);
        for (var index = 0; index < segments.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var segment = segments[index];
            var chunkKey = segments.Count == 1
                ? chunkKeyPrefix
                : $"{chunkKeyPrefix}:{index}";
            var hash = ComputeContentHash(segment);
            var cacheKey = $"{taskId}:{chunkKey}";

            if (_contentHashes.TryGetValue(cacheKey, out var existingHash) &&
                string.Equals(existingHash, hash, StringComparison.Ordinal))
            {
                continue;
            }

            var embedding = await workspaceAiService.CreateEmbeddingAsync(repositoryId, segment, cancellationToken);
            if (!embedding.Success || string.IsNullOrWhiteSpace(embedding.Payload))
            {
                continue;
            }

            chunks.Add(new SemanticChunkDocument
            {
                RepositoryId = repositoryId,
                TaskId = taskId,
                RunId = runId,
                ChunkKey = chunkKey,
                SourceType = sourceType,
                SourceRef = sourceRef,
                ChunkIndex = index,
                Content = segment,
                ContentHash = hash,
                TokenCount = CountTokens(segment),
                EmbeddingModel = embedding.Model,
                EmbeddingDimensions = embedding.Dimensions,
                EmbeddingPayload = embedding.Payload,
                UpdatedAtUtc = DateTime.UtcNow,
            });

            _contentHashes[cacheKey] = hash;
        }
    }

    private static string BuildTaskKey(string repositoryId, string taskId)
    {
        return $"{repositoryId}:{taskId}";
    }

    private static string MergeReasons(string existing, string incoming)
    {
        if (string.IsNullOrWhiteSpace(existing))
        {
            return incoming;
        }

        if (string.IsNullOrWhiteSpace(incoming))
        {
            return existing;
        }

        if (existing.Contains(incoming, StringComparison.OrdinalIgnoreCase))
        {
            return existing;
        }

        return $"{existing}|{incoming}";
    }

    private static string BuildPromptEntryContent(WorkspacePromptEntryDocument promptEntry)
    {
        return new StringBuilder()
            .AppendLine($"Prompt entry {promptEntry.Id}")
            .AppendLine($"Role: {promptEntry.Role}")
            .AppendLine($"CreatedAtUtc: {promptEntry.CreatedAtUtc:O}")
            .AppendLine(promptEntry.Content)
            .ToString();
    }

    private static string BuildRunOutputContent(RunDocument run)
    {
        return new StringBuilder()
            .AppendLine($"Run {run.Id}")
            .AppendLine($"State: {run.State}")
            .AppendLine($"CreatedAtUtc: {run.CreatedAtUtc:O}")
            .AppendLine($"StartedAtUtc: {(run.StartedAtUtc.HasValue ? run.StartedAtUtc.Value.ToString("O") : "null")}")
            .AppendLine($"EndedAtUtc: {(run.EndedAtUtc.HasValue ? run.EndedAtUtc.Value.ToString("O") : "null")}")
            .AppendLine($"Summary: {run.Summary}")
            .AppendLine("FullOutput:")
            .AppendLine(run.OutputJson)
            .ToString();
    }

    private static string BuildRunLogsContent(RunDocument run, IReadOnlyList<RunLogEvent> runLogs)
    {
        var builder = new StringBuilder()
            .AppendLine($"Run logs for {run.Id}")
            .AppendLine($"LogEntries: {runLogs.Count}");

        foreach (var runLog in runLogs)
        {
            builder
                .Append(runLog.TimestampUtc.ToString("O"))
                .Append(" [")
                .Append(runLog.Level)
                .Append("] ")
                .AppendLine(runLog.Message);
        }

        return builder.ToString();
    }

    private static string BuildRunStructuredEventsContent(RunDocument run, IReadOnlyList<RunStructuredEventDocument> structuredEvents)
    {
        var builder = new StringBuilder()
            .AppendLine($"Run structured events for {run.Id}")
            .AppendLine($"StructuredEntries: {structuredEvents.Count}");

        foreach (var structuredEvent in structuredEvents)
        {
            builder
                .Append(structuredEvent.TimestampUtc.ToString("O"))
                .Append(" #")
                .Append(structuredEvent.Sequence)
                .Append(' ')
                .Append(structuredEvent.Category)
                .Append(": ")
                .AppendLine(structuredEvent.Summary);

            if (!string.IsNullOrWhiteSpace(structuredEvent.PayloadJson))
            {
                builder.AppendLine(structuredEvent.PayloadJson);
            }
        }

        return builder.ToString();
    }

    private static string BuildTextArtifactContent(RunDocument run, string artifactName, string artifactText)
    {
        return new StringBuilder()
            .AppendLine($"Run artifact {artifactName}")
            .AppendLine($"RunId: {run.Id}")
            .AppendLine("Content:")
            .AppendLine(artifactText)
            .ToString();
    }

    private static string BuildImageArtifactMetadataContent(RunDocument run, string artifactName, string fileExtension)
    {
        return new StringBuilder()
            .AppendLine($"Run image artifact {artifactName}")
            .AppendLine($"RunId: {run.Id}")
            .AppendLine($"Extension: {fileExtension}")
            .AppendLine("Type: image-metadata")
            .ToString();
    }

    private async Task<string> ReadArtifactTextAsync(string runId, string artifactName, CancellationToken cancellationToken)
    {
        await using var stream = await store.GetArtifactAsync(runId, artifactName, cancellationToken);
        if (stream is null)
        {
            return string.Empty;
        }

        if (stream.CanSeek && stream.Length > MaxArtifactTextBytes)
        {
            return string.Empty;
        }

        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            if (memory.Length + bytesRead > MaxArtifactTextBytes)
            {
                return string.Empty;
            }

            memory.Write(buffer, 0, bytesRead);
        }

        if (memory.Length == 0)
        {
            return string.Empty;
        }

        return Encoding.UTF8.GetString(memory.ToArray());
    }

    private static bool IsTextArtifact(string extension)
    {
        return extension is ".txt" or ".log" or ".md" or ".json" or ".xml" or ".yaml" or ".yml" or ".diff" or ".patch" or ".csv" or ".html" or ".css" or ".js" or ".ts" or ".cs" or ".py" or ".go" or ".rs" or ".java";
    }

    private static bool IsImageArtifact(string extension)
    {
        return extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".svg";
    }

    private static string NormalizeContent(string content)
    {
        return content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Trim();
    }

    private static List<string> SegmentContent(string content, int maxSegmentLength)
    {
        if (content.Length <= maxSegmentLength)
        {
            return [content];
        }

        var segments = new List<string>();
        var offset = 0;
        while (offset < content.Length)
        {
            var maxLength = Math.Min(maxSegmentLength, content.Length - offset);
            var end = offset + maxLength;

            if (end < content.Length)
            {
                var newlineIndex = content.LastIndexOf('\n', end - 1, maxLength);
                if (newlineIndex > offset + 200)
                {
                    end = newlineIndex + 1;
                }
                else
                {
                    var whitespaceIndex = content.LastIndexOf(' ', end - 1, maxLength);
                    if (whitespaceIndex > offset + 200)
                    {
                        end = whitespaceIndex + 1;
                    }
                }
            }

            var segment = content[offset..end].Trim();
            if (segment.Length > 0)
            {
                segments.Add(segment);
            }

            offset = end;
        }

        return segments;
    }

    private static string ComputeContentHash(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private static int CountTokens(string text)
    {
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private sealed record PendingTaskEmbedding(
        string RepositoryId,
        string TaskId,
        string Reason,
        string? RunId,
        string? PromptEntryId,
        DateTime LastQueuedAtUtc,
        long Sequence);
}
