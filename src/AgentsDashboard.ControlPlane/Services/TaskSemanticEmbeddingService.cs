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
            if (string.IsNullOrWhiteSpace(run.OutputJson))
            {
                continue;
            }

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

        if (chunks.Count == 0)
        {
            logger.ZLogDebug("Task semantic embedding skipped for task {TaskId}: no changed chunks", taskId);
            return;
        }

        await store.UpsertSemanticChunksAsync(taskId, chunks, cancellationToken);
        logger.ZLogDebug(
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
            logger.ZLogDebug(
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
            logger.ZLogWarning(
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
            .AppendLine($"Kind: {task.Kind}")
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
