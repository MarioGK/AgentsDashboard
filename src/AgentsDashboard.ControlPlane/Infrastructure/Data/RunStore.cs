using System.Globalization;
using System.Text.Json;

namespace AgentsDashboard.ControlPlane.Infrastructure.Data;

public sealed class RunStore(
    IOrchestratorRepositorySessionFactory liteDbScopeFactory,
    LiteDbExecutor liteDbExecutor) : IRunStore
{
    private static readonly RunState[] ActiveStates = [RunState.Queued, RunState.Running, RunState.PendingApproval];
    private const string ArtifactFileStorageRoot = "$/run-artifacts";

    public async Task<RunDocument> CreateRunAsync(
        TaskDocument task,
        CancellationToken cancellationToken,
        int attempt = 1,
        HarnessExecutionMode? executionModeOverride = null,
        string? sessionProfileId = null,
        string? mcpConfigSnapshotJson = null)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var run = new RunDocument
        {
            RepositoryId = task.RepositoryId,
            TaskId = task.Id,
            State = RunState.Queued,
            ExecutionMode = executionModeOverride ?? task.ExecutionModeDefault ?? HarnessExecutionMode.Default,
            StructuredProtocol = "harness-structured-event-v2",
            SessionProfileId = sessionProfileId?.Trim() ?? task.SessionProfileId,
            McpConfigSnapshotJson = mcpConfigSnapshotJson?.Trim() ?? string.Empty,
            Summary = "Queued",
            Attempt = attempt,
        };

        db.Runs.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        return run;
    }

    public async Task<List<RunDocument>> ListRunsByRepositoryAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.Runs.AsNoTracking().Where(x => x.RepositoryId == repositoryId).OrderByDescending(x => x.CreatedAtUtc).Take(200).ToListAsync(cancellationToken);
    }

    public async Task<List<RunDocument>> ListRecentRunsAsync(CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.Runs.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(100).ToListAsync(cancellationToken);
    }

    public async Task<List<RepositoryDocument>> ListRepositoriesWithRecentTasksAsync(int limit, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var normalizedLimit = Math.Clamp(limit, 1, 500);

        var repositoriesWithTasks = await db.Tasks.AsNoTracking()
            .GroupBy(x => x.RepositoryId)
            .Select(group => new
            {
                RepositoryId = group.Key,
                LastTaskAtUtc = group.Max(x => x.CreatedAtUtc)
            })
            .OrderByDescending(x => x.LastTaskAtUtc)
            .Take(normalizedLimit)
            .ToListAsync(cancellationToken);

        var orderedRepositoryIds = repositoriesWithTasks.Select(x => x.RepositoryId).ToList();
        if (orderedRepositoryIds.Count == 0)
        {
            return await db.Repositories.AsNoTracking()
                .OrderByDescending(x => x.LastViewedAtUtc)
                .ThenBy(x => x.Name)
                .Take(normalizedLimit)
                .ToListAsync(cancellationToken);
        }

        var repositories = await db.Repositories.AsNoTracking()
            .Where(x => orderedRepositoryIds.Contains(x.Id))
            .ToListAsync(cancellationToken);

        var byRepositoryId = repositories.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var orderedRepositories = orderedRepositoryIds
            .Select(id => byRepositoryId.GetValueOrDefault(id))
            .Where(x => x is not null)
            .Cast<RepositoryDocument>()
            .ToList();

        if (orderedRepositories.Count >= normalizedLimit)
        {
            return orderedRepositories;
        }

        var remainingLimit = normalizedLimit - orderedRepositories.Count;
        var alreadyIncluded = orderedRepositories.Select(x => x.Id).ToList();
        var remainingRepositories = await db.Repositories.AsNoTracking()
            .Where(x => !alreadyIncluded.Contains(x.Id))
            .OrderByDescending(x => x.LastViewedAtUtc)
            .ThenBy(x => x.Name)
            .Take(remainingLimit)
            .ToListAsync(cancellationToken);

        orderedRepositories.AddRange(remainingRepositories);
        return orderedRepositories;
    }

    public async Task<List<RunDocument>> ListRunsByTaskAsync(string taskId, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return [];
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var normalizedLimit = Math.Clamp(limit, 1, 500);

        return await db.Runs.AsNoTracking()
            .Where(x => x.TaskId == taskId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(normalizedLimit)
            .ToListAsync(cancellationToken);
    }

    public async Task<Dictionary<string, RunDocument>> GetLatestRunsByTaskIdsAsync(List<string> taskIds, CancellationToken cancellationToken)
    {
        if (taskIds.Count == 0)
        {
            return [];
        }

        var normalizedTaskIds = taskIds
            .Where(taskId => !string.IsNullOrWhiteSpace(taskId))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (normalizedTaskIds.Count == 0)
        {
            return [];
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var latestRunCandidates = await (
            from run in db.Runs.AsNoTracking()
            where normalizedTaskIds.Contains(run.TaskId)
            join latestRun in (
                from candidate in db.Runs.AsNoTracking()
                where normalizedTaskIds.Contains(candidate.TaskId)
                group candidate by candidate.TaskId into grouped
                select new
                {
                    TaskId = grouped.Key,
                    LatestCreatedAtUtc = grouped.Max(x => x.CreatedAtUtc)
                })
                on new { run.TaskId, run.CreatedAtUtc } equals new { latestRun.TaskId, CreatedAtUtc = latestRun.LatestCreatedAtUtc }
            select run)
            .ToListAsync(cancellationToken);

        return latestRunCandidates
            .GroupBy(x => x.TaskId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(x => x.Id, StringComparer.Ordinal)
                    .First(),
                StringComparer.Ordinal);
    }

    public async Task<List<RunDocument>> ListCompletedRunsByTaskForEmbeddingAsync(string taskId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return [];
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.Runs.AsNoTracking()
            .Where(x =>
                x.TaskId == taskId &&
                x.State != RunState.Queued &&
                x.State != RunState.Running &&
                x.State != RunState.PendingApproval &&
                x.OutputJson != string.Empty)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<Dictionary<string, RunState>> GetLatestRunStatesByTaskIdsAsync(List<string> taskIds, CancellationToken cancellationToken)
    {
        var latestRunsByTaskId = await GetLatestRunsByTaskIdsAsync(taskIds, cancellationToken);
        return latestRunsByTaskId.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.State,
            StringComparer.Ordinal);
    }

    public async Task<List<WorkspacePromptEntryDocument>> ListWorkspacePromptHistoryAsync(string taskId, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return [];
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var normalizedLimit = Math.Clamp(limit, 1, 1000);

        return await db.WorkspacePromptEntries.AsNoTracking()
            .Where(x => x.TaskId == taskId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(normalizedLimit)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<WorkspacePromptEntryDocument>> ListWorkspacePromptEntriesForEmbeddingAsync(string taskId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return [];
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.WorkspacePromptEntries.AsNoTracking()
            .Where(x => x.TaskId == taskId)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<WorkspacePromptEntryDocument> AppendWorkspacePromptEntryAsync(WorkspacePromptEntryDocument promptEntry, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(promptEntry.TaskId))
        {
            throw new ArgumentException("TaskId is required.", nameof(promptEntry));
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(promptEntry.Id))
        {
            promptEntry.Id = Guid.NewGuid().ToString("N");
        }

        if (promptEntry.CreatedAtUtc == default)
        {
            promptEntry.CreatedAtUtc = DateTime.UtcNow;
        }

        if (string.IsNullOrWhiteSpace(promptEntry.RepositoryId))
        {
            promptEntry.RepositoryId = await db.Tasks.AsNoTracking()
                .Where(x => x.Id == promptEntry.TaskId)
                .Select(x => x.RepositoryId)
                .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
        }

        db.WorkspacePromptEntries.Add(promptEntry);
        await db.SaveChangesAsync(cancellationToken);
        return promptEntry;
    }

    public async Task<WorkspacePromptEntryDocument?> UpdateWorkspacePromptEntryContentAsync(
        string promptEntryId,
        string newContent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(promptEntryId))
        {
            return null;
        }

        var normalizedContent = newContent?.Trim() ?? string.Empty;
        if (normalizedContent.Length == 0)
        {
            return null;
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var promptEntry = await db.WorkspacePromptEntries.FirstOrDefaultAsync(x => x.Id == promptEntryId, cancellationToken);
        if (promptEntry is null)
        {
            return null;
        }

        promptEntry.Content = normalizedContent;
        await db.SaveChangesAsync(cancellationToken);
        return promptEntry;
    }

    public async Task<int> DeleteWorkspacePromptEntriesAsync(IReadOnlyList<string> promptEntryIds, CancellationToken cancellationToken)
    {
        if (promptEntryIds.Count == 0)
        {
            return 0;
        }

        var normalizedPromptEntryIds = promptEntryIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (normalizedPromptEntryIds.Count == 0)
        {
            return 0;
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var deleted = await db.WorkspacePromptEntries.DeleteWhereAsync(x => normalizedPromptEntryIds.Contains(x.Id), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return deleted;
    }

    public async Task<List<WorkspaceQueuedMessageDocument>> ListWorkspaceQueuedMessagesAsync(string taskId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return [];
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.WorkspaceQueuedMessages.AsNoTracking()
            .Where(x => x.TaskId == taskId)
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Order)
            .ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<string>> ListTaskIdsWithQueuedMessagesAsync(CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.WorkspaceQueuedMessages.AsNoTracking()
            .Where(x => x.TaskId != string.Empty)
            .GroupBy(x => x.TaskId)
            .OrderBy(group => group.Min(x => x.CreatedAtUtc))
            .Select(group => group.Key)
            .ToListAsync(cancellationToken);
    }

    public async Task<WorkspaceQueuedMessageDocument> AppendWorkspaceQueuedMessageAsync(
        WorkspaceQueuedMessageDocument queuedMessage,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(queuedMessage.TaskId))
        {
            throw new ArgumentException("TaskId is required.", nameof(queuedMessage.TaskId));
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var normalizedTaskId = queuedMessage.TaskId.Trim();
        var normalizedContent = queuedMessage.Content?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(queuedMessage.Id))
        {
            queuedMessage.Id = Guid.NewGuid().ToString("N");
        }

        if (queuedMessage.CreatedAtUtc == default)
        {
            queuedMessage.CreatedAtUtc = DateTime.UtcNow;
        }

        queuedMessage.TaskId = normalizedTaskId;
        queuedMessage.Content = normalizedContent;
        queuedMessage.ImagePayloadJson = queuedMessage.ImagePayloadJson?.Trim() ?? string.Empty;
        queuedMessage.RepositoryId = queuedMessage.RepositoryId?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(queuedMessage.RepositoryId))
        {
            queuedMessage.RepositoryId = await db.Tasks.AsNoTracking()
                .Where(x => x.Id == normalizedTaskId)
                .Select(x => x.RepositoryId)
                .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;
        }

        if (queuedMessage.Order <= 0)
        {
            var existingOrders = await db.WorkspaceQueuedMessages.AsNoTracking()
                .Where(x => x.TaskId == normalizedTaskId)
                .Select(x => x.Order)
                .ToListAsync(cancellationToken);
            var maxOrder = existingOrders.Count == 0 ? 0 : existingOrders.Max();
            queuedMessage.Order = maxOrder + 1;
        }

        db.WorkspaceQueuedMessages.Add(queuedMessage);
        await db.SaveChangesAsync(cancellationToken);
        return queuedMessage;
    }

    public async Task<int> DeleteWorkspaceQueuedMessagesAsync(IReadOnlyList<string> queuedMessageIds, CancellationToken cancellationToken)
    {
        if (queuedMessageIds.Count == 0)
        {
            return 0;
        }

        var normalizedQueuedMessageIds = queuedMessageIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (normalizedQueuedMessageIds.Count == 0)
        {
            return 0;
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var deleted = await db.WorkspaceQueuedMessages.DeleteWhereAsync(x => normalizedQueuedMessageIds.Contains(x.Id), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return deleted;
    }

    public async Task<int> DeleteWorkspaceQueuedMessagesByTaskAsync(string taskId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return 0;
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var deleted = await db.WorkspaceQueuedMessages.DeleteWhereAsync(x => x.TaskId == taskId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return deleted;
    }

    public async Task<RunQuestionRequestDocument?> UpsertRunQuestionRequestAsync(RunQuestionRequestDocument questionRequest, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(questionRequest.RunId) ||
            string.IsNullOrWhiteSpace(questionRequest.TaskId) ||
            questionRequest.SourceSequence <= 0 ||
            questionRequest.Questions.Count == 0)
        {
            return null;
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var now = DateTime.UtcNow;

        var existing = await db.RunQuestionRequests.FirstOrDefaultAsync(
            x => x.RunId == questionRequest.RunId && x.SourceSequence == questionRequest.SourceSequence,
            cancellationToken);

        var normalizedQuestions = NormalizeQuestionItems(questionRequest.Questions);
        if (normalizedQuestions.Count == 0)
        {
            return null;
        }

        if (existing is null)
        {
            if (string.IsNullOrWhiteSpace(questionRequest.Id))
            {
                questionRequest.Id = Guid.NewGuid().ToString("N");
            }

            questionRequest.RepositoryId = questionRequest.RepositoryId?.Trim() ?? string.Empty;
            questionRequest.TaskId = questionRequest.TaskId?.Trim() ?? string.Empty;
            questionRequest.RunId = questionRequest.RunId?.Trim() ?? string.Empty;
            questionRequest.SourceToolCallId = questionRequest.SourceToolCallId?.Trim() ?? string.Empty;
            questionRequest.SourceToolName = questionRequest.SourceToolName?.Trim() ?? string.Empty;
            questionRequest.SourceSchemaVersion = questionRequest.SourceSchemaVersion?.Trim() ?? string.Empty;
            questionRequest.Status = RunQuestionRequestStatus.Pending;
            questionRequest.Questions = normalizedQuestions;
            questionRequest.Answers = [];
            questionRequest.AnsweredRunId = string.Empty;
            questionRequest.AnsweredAtUtc = null;
            questionRequest.CreatedAtUtc = questionRequest.CreatedAtUtc == default ? now : questionRequest.CreatedAtUtc;
            questionRequest.UpdatedAtUtc = now;

            db.RunQuestionRequests.Add(questionRequest);
            await db.SaveChangesAsync(cancellationToken);
            return questionRequest;
        }

        if (existing.Status == RunQuestionRequestStatus.Answered)
        {
            return existing;
        }

        existing.RepositoryId = string.IsNullOrWhiteSpace(questionRequest.RepositoryId) ? existing.RepositoryId : questionRequest.RepositoryId.Trim();
        existing.TaskId = string.IsNullOrWhiteSpace(questionRequest.TaskId) ? existing.TaskId : questionRequest.TaskId.Trim();
        existing.RunId = string.IsNullOrWhiteSpace(questionRequest.RunId) ? existing.RunId : questionRequest.RunId.Trim();
        existing.SourceToolCallId = questionRequest.SourceToolCallId?.Trim() ?? existing.SourceToolCallId;
        existing.SourceToolName = questionRequest.SourceToolName?.Trim() ?? existing.SourceToolName;
        existing.SourceSchemaVersion = questionRequest.SourceSchemaVersion?.Trim() ?? existing.SourceSchemaVersion;
        existing.Questions = normalizedQuestions;
        existing.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<List<RunQuestionRequestDocument>> ListPendingRunQuestionRequestsAsync(string taskId, string runId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId) || string.IsNullOrWhiteSpace(runId))
        {
            return [];
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.RunQuestionRequests.AsNoTracking()
            .Where(x => x.TaskId == taskId && x.RunId == runId && x.Status == RunQuestionRequestStatus.Pending)
            .OrderBy(x => x.SourceSequence)
            .ThenBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<RunQuestionRequestDocument?> GetRunQuestionRequestAsync(string questionRequestId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(questionRequestId))
        {
            return null;
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.RunQuestionRequests.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == questionRequestId, cancellationToken);
    }

    public async Task<RunQuestionRequestDocument?> MarkRunQuestionRequestAnsweredAsync(
        string questionRequestId,
        IReadOnlyList<RunQuestionAnswerDocument> answers,
        string answeredRunId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(questionRequestId))
        {
            return null;
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var existing = await db.RunQuestionRequests.FirstOrDefaultAsync(x => x.Id == questionRequestId, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        existing.Status = RunQuestionRequestStatus.Answered;
        existing.AnsweredRunId = answeredRunId?.Trim() ?? string.Empty;
        existing.AnsweredAtUtc = now;
        existing.UpdatedAtUtc = now;
        existing.Answers = NormalizeQuestionAnswers(answers);

        await db.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<RunAiSummaryDocument> UpsertRunAiSummaryAsync(RunAiSummaryDocument summary, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(summary.RunId))
        {
            throw new ArgumentException("RunId is required.", nameof(summary));
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var now = DateTime.UtcNow;

        var runMetadata = await db.Runs.AsNoTracking()
            .Where(x => x.Id == summary.RunId)
            .Select(x => new
            {
                x.RepositoryId,
                x.TaskId,
                SourceUpdatedAtUtc = x.EndedAtUtc ?? x.CreatedAtUtc
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (runMetadata is not null)
        {
            if (string.IsNullOrWhiteSpace(summary.RepositoryId))
            {
                summary.RepositoryId = runMetadata.RepositoryId;
            }

            if (string.IsNullOrWhiteSpace(summary.TaskId))
            {
                summary.TaskId = runMetadata.TaskId;
            }

            if (summary.SourceUpdatedAtUtc == default)
            {
                summary.SourceUpdatedAtUtc = runMetadata.SourceUpdatedAtUtc;
            }
        }

        if (summary.GeneratedAtUtc == default)
        {
            summary.GeneratedAtUtc = now;
        }

        var existing = await db.RunAiSummaries.FirstOrDefaultAsync(x => x.RunId == summary.RunId, cancellationToken);
        if (existing is null)
        {
            db.RunAiSummaries.Add(summary);
            await db.SaveChangesAsync(cancellationToken);
            return summary;
        }

        existing.RepositoryId = summary.RepositoryId;
        existing.TaskId = summary.TaskId;
        existing.Title = summary.Title;
        existing.Summary = summary.Summary;
        existing.Model = summary.Model;
        existing.SourceFingerprint = summary.SourceFingerprint;
        existing.SourceUpdatedAtUtc = summary.SourceUpdatedAtUtc;
        existing.GeneratedAtUtc = summary.GeneratedAtUtc;
        existing.ExpiresAtUtc = summary.ExpiresAtUtc;

        await db.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<RunAiSummaryDocument?> GetRunAiSummaryAsync(string runId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.RunAiSummaries.AsNoTracking()
            .FirstOrDefaultAsync(x => x.RunId == runId, cancellationToken);
    }

    public async Task UpsertSemanticChunksAsync(string taskId, List<SemanticChunkDocument> chunks, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId) || chunks.Count == 0)
        {
            return;
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var repositoryId = await db.Tasks.AsNoTracking()
            .Where(x => x.Id == taskId)
            .Select(x => x.RepositoryId)
            .FirstOrDefaultAsync(cancellationToken) ?? string.Empty;

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
        var chunkKeys = normalizedChunksByKey.Select(x => x.ChunkKey).ToList();
        var existingChunks = await db.SemanticChunks
            .Where(x => x.TaskId == taskId && chunkKeys.Contains(x.ChunkKey))
            .ToListAsync(cancellationToken);
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
                continue;
            }

            db.SemanticChunks.Add(chunk);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<SemanticChunkDocument>> SearchWorkspaceSemanticAsync(string taskId, string queryText, string? queryEmbeddingPayload, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return [];
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var normalizedLimit = Math.Clamp(limit, 1, 200);
        var chunks = await db.SemanticChunks.AsNoTracking()
            .Where(x => x.TaskId == taskId)
            .ToListAsync(cancellationToken);

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

    public async Task<ReliabilityMetrics> GetReliabilityMetricsByRepositoryAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var sevenDaysAgo = now.AddDays(-7);
        var thirtyDaysAgo = now.AddDays(-30);
        var fourteenDaysAgo = now.AddDays(-14);

        var recentRuns = await db.Runs.AsNoTracking()
            .Where(x => x.RepositoryId == repositoryId && x.CreatedAtUtc >= thirtyDaysAgo)
            .ToListAsync(cancellationToken);

        return CalculateMetricsFromRuns(recentRuns, sevenDaysAgo, thirtyDaysAgo, fourteenDaysAgo, now);
    }

    private static ReliabilityMetrics CalculateMetricsFromRuns(List<RunDocument> recentRuns, DateTime sevenDaysAgo, DateTime thirtyDaysAgo, DateTime fourteenDaysAgo, DateTime now)
    {
        var runs7Days = recentRuns.Where(r => r.CreatedAtUtc >= sevenDaysAgo).ToList();
        var runs30Days = recentRuns.ToList();

        var successRate7Days = CalculateSuccessRate(runs7Days);
        var successRate30Days = CalculateSuccessRate(runs30Days);

        var runsByState = recentRuns
            .GroupBy(r => r.State.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        var failureTrend = CalculateFailureTrend(recentRuns.Where(r => r.CreatedAtUtc >= fourteenDaysAgo).ToList(), fourteenDaysAgo, now);
        var avgDuration = CalculateAverageDuration(recentRuns);

        return new ReliabilityMetrics(successRate7Days, successRate30Days, runs7Days.Count, runs30Days.Count, runsByState, failureTrend, avgDuration, []);
    }

    public async Task<RunDocument?> GetRunAsync(string runId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.Runs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == runId, cancellationToken);
    }

    public async Task<List<RunDocument>> ListRunsByStateAsync(RunState state, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.Runs.AsNoTracking().Where(x => x.State == state).OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task<List<string>> ListTaskIdsWithQueuedRunsAsync(CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.Runs.AsNoTracking()
            .Where(x => x.State == RunState.Queued && x.TaskId != string.Empty)
            .GroupBy(x => x.TaskId)
            .OrderBy(group => group.Min(x => x.CreatedAtUtc))
            .Select(group => group.Key)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<string>> ListAllRunIdsAsync(CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.Runs.AsNoTracking().Select(x => x.Id).ToListAsync(cancellationToken);
    }

    public async Task<long> CountRunsByStateAsync(RunState state, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.Runs.LongCountAsync(x => x.State == state, cancellationToken);
    }

    public async Task<long> CountActiveRunsAsync(CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.Runs.LongCountAsync(x => ActiveStates.Contains(x.State), cancellationToken);
    }

    public async Task<long> CountActiveRunsByRepoAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.Runs.LongCountAsync(x => x.RepositoryId == repositoryId && ActiveStates.Contains(x.State), cancellationToken);
    }

    public async Task<long> CountActiveRunsByTaskAsync(string taskId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.Runs.LongCountAsync(x => x.TaskId == taskId && ActiveStates.Contains(x.State), cancellationToken);
    }

    public async Task<RunDocument?> MarkRunStartedAsync(
        string runId,
        string workerId,
        CancellationToken cancellationToken,
        string? workerImageRef = null,
        string? workerImageDigest = null,
        string? workerImageSource = null)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var run = await db.Runs.FirstOrDefaultAsync(x => x.Id == runId && x.State != RunState.Obsolete, cancellationToken);
        if (run is null)
            return null;

        run.State = RunState.Running;
        run.TaskRuntimeId = workerId;
        run.StartedAtUtc = DateTime.UtcNow;
        run.Summary = "Running";
        if (!string.IsNullOrWhiteSpace(workerImageRef))
        {
            run.TaskRuntimeImageRef = workerImageRef;
        }

        if (!string.IsNullOrWhiteSpace(workerImageDigest))
        {
            run.TaskRuntimeImageDigest = workerImageDigest;
        }

        if (!string.IsNullOrWhiteSpace(workerImageSource))
        {
            run.TaskRuntimeImageSource = workerImageSource;
        }

        await db.SaveChangesAsync(cancellationToken);
        return run;
    }

    public async Task<RunDocument?> MarkRunCompletedAsync(string runId, bool succeeded, string summary, string outputJson, CancellationToken cancellationToken, string? failureClass = null, string? prUrl = null)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var run = await db.Runs.FirstOrDefaultAsync(x => x.Id == runId && x.State != RunState.Obsolete, cancellationToken);
        if (run is null)
            return null;

        run.State = succeeded ? RunState.Succeeded : RunState.Failed;
        run.EndedAtUtc = DateTime.UtcNow;
        run.Summary = summary;
        run.OutputJson = outputJson;

        if (!string.IsNullOrEmpty(failureClass))
            run.FailureClass = failureClass;
        if (!string.IsNullOrEmpty(prUrl))
            run.PrUrl = prUrl;

        await db.SaveChangesAsync(cancellationToken);
        return run;
    }

    public async Task<RunDocument?> MarkRunCancelledAsync(string runId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var run = await db.Runs.FirstOrDefaultAsync(x => x.Id == runId && ActiveStates.Contains(x.State), cancellationToken);
        if (run is null)
            return null;

        run.State = RunState.Cancelled;
        run.EndedAtUtc = DateTime.UtcNow;
        run.Summary = "Cancelled";
        await db.SaveChangesAsync(cancellationToken);
        return run;
    }

    public async Task<RunDocument?> MarkRunObsoleteAsync(string runId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var run = await db.Runs.FirstOrDefaultAsync(
            x => x.Id == runId && (ActiveStates.Contains(x.State) || x.State == RunState.Succeeded),
            cancellationToken);
        if (run is null)
            return null;

        run.State = RunState.Obsolete;
        run.EndedAtUtc ??= DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(run.Summary))
        {
            run.Summary = "No changes produced";
        }
        await db.SaveChangesAsync(cancellationToken);
        return run;
    }

    public async Task<int> DeleteRunsCascadeAsync(IReadOnlyList<string> runIds, CancellationToken cancellationToken)
    {
        if (runIds.Count == 0)
        {
            return 0;
        }

        var normalizedRunIds = runIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (normalizedRunIds.Count == 0)
        {
            return 0;
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        _ = await db.RunEvents.DeleteWhereAsync(x => normalizedRunIds.Contains(x.RunId), cancellationToken);
        _ = await db.RunStructuredEvents.DeleteWhereAsync(x => normalizedRunIds.Contains(x.RunId), cancellationToken);
        _ = await db.RunDiffSnapshots.DeleteWhereAsync(x => normalizedRunIds.Contains(x.RunId), cancellationToken);
        _ = await db.RunToolProjections.DeleteWhereAsync(x => normalizedRunIds.Contains(x.RunId), cancellationToken);
        _ = await db.RunQuestionRequests.DeleteWhereAsync(x => normalizedRunIds.Contains(x.RunId), cancellationToken);
        _ = await db.WorkspacePromptEntries.DeleteWhereAsync(x => normalizedRunIds.Contains(x.RunId), cancellationToken);
        _ = await db.RunAiSummaries.DeleteWhereAsync(x => normalizedRunIds.Contains(x.RunId), cancellationToken);
        _ = await db.SemanticChunks.DeleteWhereAsync(x => normalizedRunIds.Contains(x.RunId), cancellationToken);
        _ = await db.RunInstructionStacks.DeleteWhereAsync(x => normalizedRunIds.Contains(x.RunId), cancellationToken);
        _ = await db.RunShareBundles.DeleteWhereAsync(x => normalizedRunIds.Contains(x.RunId), cancellationToken);
        var deletedRuns = await db.Runs.DeleteWhereAsync(x => normalizedRunIds.Contains(x.Id), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        await DeleteStoredArtifactsByRunIdsAsync(normalizedRunIds, cancellationToken);
        return deletedRuns;
    }

    public async Task<RunDocument?> MarkRunPendingApprovalAsync(string runId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var run = await db.Runs.FirstOrDefaultAsync(x => x.Id == runId && x.State != RunState.Obsolete, cancellationToken);
        if (run is null)
            return null;

        run.State = RunState.PendingApproval;
        run.Summary = "Pending approval";
        await db.SaveChangesAsync(cancellationToken);
        return run;
    }

    public async Task<RunDocument?> ApproveRunAsync(string runId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var run = await db.Runs.FirstOrDefaultAsync(x => x.Id == runId && x.State == RunState.PendingApproval, cancellationToken);
        if (run is null)
            return null;

        run.State = RunState.Queued;
        run.Summary = "Approved and queued";
        await db.SaveChangesAsync(cancellationToken);
        return run;
    }

    public async Task<RunDocument?> RejectRunAsync(string runId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var run = await db.Runs.FirstOrDefaultAsync(x => x.Id == runId && x.State == RunState.PendingApproval, cancellationToken);
        if (run is null)
            return null;

        run.State = RunState.Cancelled;
        run.EndedAtUtc = DateTime.UtcNow;
        run.Summary = "Rejected";
        await db.SaveChangesAsync(cancellationToken);
        return run;
    }

    public async Task<int> BulkCancelRunsAsync(List<string> runIds, CancellationToken cancellationToken)
    {
        if (runIds.Count == 0)
            return 0;

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var runs = await db.Runs.Where(x => runIds.Contains(x.Id) && ActiveStates.Contains(x.State)).ToListAsync(cancellationToken);
        foreach (var run in runs)
        {
            run.State = RunState.Cancelled;
            run.EndedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return runs.Count;
    }

    public async Task SaveArtifactAsync(string runId, string fileName, Stream stream, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            throw new ArgumentException("Run id is required.", nameof(runId));
        }

        var normalizedFileName = NormalizeArtifactFileName(fileName);
        await using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken);
        memory.Position = 0;

        var metadata = new RunArtifactDocument
        {
            Id = BuildArtifactId(runId, normalizedFileName),
            RunId = runId,
            FileName = normalizedFileName,
            FileStorageId = BuildArtifactFileStorageId(runId, normalizedFileName),
            CreatedAtUtc = DateTime.UtcNow
        };

        await liteDbExecutor.ExecuteAsync(
            db =>
            {
                db.FileStorage.Upload(metadata.FileStorageId, normalizedFileName, memory);
                var collection = db.GetCollection<RunArtifactDocument>("run_artifacts");
                collection.EnsureIndex(x => x.RunId);
                collection.EnsureIndex(x => x.FileName);
                collection.Upsert(metadata);
            },
            cancellationToken);
    }

    public Task<List<string>> ListArtifactsAsync(string runId, CancellationToken cancellationToken)
    {
        return liteDbExecutor.ExecuteAsync(
            db =>
            {
                if (string.IsNullOrWhiteSpace(runId))
                {
                    return new List<string>();
                }

                var metadataCollection = db.GetCollection<RunArtifactDocument>("run_artifacts");
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

    public async Task<Stream?> GetArtifactAsync(string runId, string fileName, CancellationToken cancellationToken)
    {
        var normalizedFileName = NormalizeArtifactFileName(fileName);
        var payload = await liteDbExecutor.ExecuteAsync(
            db =>
            {
                var metadataCollection = db.GetCollection<RunArtifactDocument>("run_artifacts");
                var metadata = metadataCollection.FindById(BuildArtifactId(runId, normalizedFileName));
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

    public async Task AddRunLogAsync(RunLogEvent logEvent, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        db.RunEvents.Add(logEvent);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<RunLogEvent>> ListRunLogsAsync(string runId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.RunEvents.AsNoTracking().Where(x => x.RunId == runId).OrderBy(x => x.TimestampUtc).ToListAsync(cancellationToken);
    }

    public async Task<RunStructuredEventDocument> AppendRunStructuredEventAsync(RunStructuredEventDocument structuredEvent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(structuredEvent.RunId))
        {
            throw new ArgumentException("RunId is required.", nameof(structuredEvent));
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var now = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(structuredEvent.Id))
        {
            structuredEvent.Id = Guid.NewGuid().ToString("N");
        }

        if (structuredEvent.CreatedAtUtc == default)
        {
            structuredEvent.CreatedAtUtc = now;
        }

        if (string.IsNullOrWhiteSpace(structuredEvent.EventType))
        {
            structuredEvent.EventType = "unknown";
        }

        structuredEvent.Category = structuredEvent.Category?.Trim() ?? string.Empty;
        structuredEvent.Summary = structuredEvent.Summary?.Trim() ?? string.Empty;
        structuredEvent.Error = structuredEvent.Error?.Trim() ?? string.Empty;
        structuredEvent.PayloadJson = string.IsNullOrWhiteSpace(structuredEvent.PayloadJson)
            ? null
            : structuredEvent.PayloadJson.Trim();
        structuredEvent.SchemaVersion = structuredEvent.SchemaVersion?.Trim() ?? string.Empty;
        if (structuredEvent.TimestampUtc == default)
        {
            structuredEvent.TimestampUtc = structuredEvent.CreatedAtUtc;
        }

        var runMetadata = await db.Runs.AsNoTracking()
            .Where(x => x.Id == structuredEvent.RunId)
            .Select(x => new
            {
                x.RepositoryId,
                x.TaskId,
                x.ExecutionMode,
            })
            .FirstOrDefaultAsync(cancellationToken);
        if (runMetadata is not null)
        {
            if (string.IsNullOrWhiteSpace(structuredEvent.RepositoryId))
            {
                structuredEvent.RepositoryId = runMetadata.RepositoryId;
            }

            if (string.IsNullOrWhiteSpace(structuredEvent.TaskId))
            {
                structuredEvent.TaskId = runMetadata.TaskId;
            }
        }

        var stored = await db.RunStructuredEvents.FirstOrDefaultAsync(
            x => x.RunId == structuredEvent.RunId && x.Sequence == structuredEvent.Sequence,
            cancellationToken);

        if (stored is null)
        {
            db.RunStructuredEvents.Add(structuredEvent);
            stored = structuredEvent;
        }
        else
        {
            stored.RepositoryId = structuredEvent.RepositoryId;
            stored.TaskId = structuredEvent.TaskId;
            stored.EventType = structuredEvent.EventType;
            stored.Category = structuredEvent.Category;
            stored.Summary = structuredEvent.Summary;
            stored.Error = structuredEvent.Error;
            stored.PayloadJson = structuredEvent.PayloadJson;
            stored.SchemaVersion = structuredEvent.SchemaVersion;
            stored.TimestampUtc = structuredEvent.TimestampUtc;
            stored.CreatedAtUtc = structuredEvent.CreatedAtUtc;
        }

        var projection = CreateToolProjection(stored);
        if (projection is not null)
        {
            RunToolProjectionDocument? existingProjection;
            if (!string.IsNullOrWhiteSpace(projection.ToolCallId))
            {
                existingProjection = await db.RunToolProjections.FirstOrDefaultAsync(
                    x => x.RunId == projection.RunId && x.ToolCallId == projection.ToolCallId,
                    cancellationToken);
            }
            else
            {
                existingProjection = await db.RunToolProjections.FirstOrDefaultAsync(
                    x => x.RunId == projection.RunId &&
                         x.SequenceStart <= projection.SequenceStart &&
                         x.SequenceEnd >= projection.SequenceEnd,
                    cancellationToken);
            }

            if (existingProjection is null)
            {
                db.RunToolProjections.Add(projection);
            }
            else
            {
                if (existingProjection.SequenceStart == 0 || projection.SequenceStart < existingProjection.SequenceStart)
                {
                    existingProjection.SequenceStart = projection.SequenceStart;
                }

                if (projection.SequenceEnd > existingProjection.SequenceEnd)
                {
                    existingProjection.SequenceEnd = projection.SequenceEnd;
                }

                existingProjection.RepositoryId = projection.RepositoryId;
                existingProjection.TaskId = projection.TaskId;
                existingProjection.ToolName = projection.ToolName;
                existingProjection.Status = projection.Status;
                existingProjection.InputJson = projection.InputJson;
                existingProjection.OutputJson = projection.OutputJson;
                existingProjection.Error = projection.Error;
                existingProjection.SchemaVersion = projection.SchemaVersion;
                existingProjection.TimestampUtc = projection.TimestampUtc;
                existingProjection.CreatedAtUtc = projection.CreatedAtUtc;
                if (!string.IsNullOrWhiteSpace(projection.ToolCallId))
                {
                    existingProjection.ToolCallId = projection.ToolCallId;
                }
            }
        }

        var questionRequest = CreateQuestionRequestProjection(
            stored,
            runMetadata?.ExecutionMode ?? HarnessExecutionMode.Default);
        if (questionRequest is not null)
        {
            var existingQuestionRequest = await db.RunQuestionRequests.FirstOrDefaultAsync(
                x => x.RunId == questionRequest.RunId && x.SourceSequence == questionRequest.SourceSequence,
                cancellationToken);
            if (existingQuestionRequest is null)
            {
                db.RunQuestionRequests.Add(questionRequest);
            }
            else if (existingQuestionRequest.Status != RunQuestionRequestStatus.Answered)
            {
                existingQuestionRequest.RepositoryId = questionRequest.RepositoryId;
                existingQuestionRequest.TaskId = questionRequest.TaskId;
                existingQuestionRequest.SourceToolCallId = questionRequest.SourceToolCallId;
                existingQuestionRequest.SourceToolName = questionRequest.SourceToolName;
                existingQuestionRequest.SourceSchemaVersion = questionRequest.SourceSchemaVersion;
                existingQuestionRequest.Questions = questionRequest.Questions;
                existingQuestionRequest.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return stored;
    }

    public async Task<List<RunStructuredEventDocument>> ListRunStructuredEventsAsync(string runId, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return [];
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var normalizedLimit = limit <= 0 ? 500 : Math.Clamp(limit, 1, 5000);

        return await db.RunStructuredEvents.AsNoTracking()
            .Where(x => x.RunId == runId)
            .OrderBy(x => x.Sequence)
            .ThenBy(x => x.TimestampUtc)
            .Take(normalizedLimit)
            .ToListAsync(cancellationToken);
    }

    public async Task<RunDiffSnapshotDocument> UpsertRunDiffSnapshotAsync(RunDiffSnapshotDocument snapshot, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(snapshot.RunId))
        {
            throw new ArgumentException("RunId is required.", nameof(snapshot));
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var now = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(snapshot.Id))
        {
            snapshot.Id = Guid.NewGuid().ToString("N");
        }

        if (snapshot.CreatedAtUtc == default)
        {
            snapshot.CreatedAtUtc = now;
        }

        if (snapshot.TimestampUtc == default)
        {
            snapshot.TimestampUtc = snapshot.CreatedAtUtc;
        }

        snapshot.Summary = snapshot.Summary?.Trim() ?? string.Empty;
        snapshot.DiffStat = snapshot.DiffStat?.Trim() ?? string.Empty;
        snapshot.DiffPatch = snapshot.DiffPatch?.Trim() ?? string.Empty;
        snapshot.SchemaVersion = snapshot.SchemaVersion?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(snapshot.RepositoryId) || string.IsNullOrWhiteSpace(snapshot.TaskId))
        {
            var runMetadata = await db.Runs.AsNoTracking()
                .Where(x => x.Id == snapshot.RunId)
                .Select(x => new
                {
                    x.RepositoryId,
                    x.TaskId
                })
                .FirstOrDefaultAsync(cancellationToken);
            if (runMetadata is not null)
            {
                if (string.IsNullOrWhiteSpace(snapshot.RepositoryId))
                {
                    snapshot.RepositoryId = runMetadata.RepositoryId;
                }

                if (string.IsNullOrWhiteSpace(snapshot.TaskId))
                {
                    snapshot.TaskId = runMetadata.TaskId;
                }
            }
        }

        var existing = await db.RunDiffSnapshots.FirstOrDefaultAsync(
            x => x.RunId == snapshot.RunId && x.Sequence == snapshot.Sequence,
            cancellationToken);

        if (existing is null)
        {
            db.RunDiffSnapshots.Add(snapshot);
            await db.SaveChangesAsync(cancellationToken);
            return snapshot;
        }

        existing.RepositoryId = snapshot.RepositoryId;
        existing.TaskId = snapshot.TaskId;
        existing.Summary = snapshot.Summary;
        existing.DiffStat = snapshot.DiffStat;
        existing.DiffPatch = snapshot.DiffPatch;
        existing.SchemaVersion = snapshot.SchemaVersion;
        existing.TimestampUtc = snapshot.TimestampUtc;
        existing.CreatedAtUtc = snapshot.CreatedAtUtc;

        await db.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<RunDiffSnapshotDocument?> GetLatestRunDiffSnapshotAsync(string runId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return null;
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.RunDiffSnapshots.AsNoTracking()
            .Where(x => x.RunId == runId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Sequence)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<RunToolProjectionDocument>> ListRunToolProjectionsAsync(string runId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return [];
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.RunToolProjections.AsNoTracking()
            .Where(x => x.RunId == runId)
            .OrderBy(x => x.SequenceStart)
            .ThenBy(x => x.SequenceEnd)
            .ThenBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<RunInstructionStackDocument> UpsertRunInstructionStackAsync(RunInstructionStackDocument stack, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stack.RunId))
        {
            throw new ArgumentException("RunId is required.", nameof(stack));
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var now = DateTime.UtcNow;
        stack.Hash = stack.Hash?.Trim() ?? string.Empty;
        stack.ResolvedText = stack.ResolvedText?.Trim() ?? string.Empty;
        stack.GlobalRules = stack.GlobalRules?.Trim() ?? string.Empty;
        stack.RepositoryRules = stack.RepositoryRules?.Trim() ?? string.Empty;
        stack.TaskRules = stack.TaskRules?.Trim() ?? string.Empty;
        stack.RunOverrides = stack.RunOverrides?.Trim() ?? string.Empty;

        if (stack.CreatedAtUtc == default)
        {
            stack.CreatedAtUtc = now;
        }

        var existing = await db.RunInstructionStacks.FirstOrDefaultAsync(x => x.RunId == stack.RunId, cancellationToken);
        if (existing is null)
        {
            if (string.IsNullOrWhiteSpace(stack.Id))
            {
                stack.Id = Guid.NewGuid().ToString("N");
            }

            db.RunInstructionStacks.Add(stack);
            await db.SaveChangesAsync(cancellationToken);
            return stack;
        }

        existing.RepositoryId = stack.RepositoryId;
        existing.TaskId = stack.TaskId;
        existing.SessionProfileId = stack.SessionProfileId;
        existing.GlobalRules = stack.GlobalRules;
        existing.RepositoryRules = stack.RepositoryRules;
        existing.TaskRules = stack.TaskRules;
        existing.RunOverrides = stack.RunOverrides;
        existing.ResolvedText = stack.ResolvedText;
        existing.Hash = stack.Hash;
        existing.CreatedAtUtc = stack.CreatedAtUtc;

        await db.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<RunInstructionStackDocument?> GetRunInstructionStackAsync(string runId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return null;
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.RunInstructionStacks.AsNoTracking()
            .Where(x => x.RunId == runId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<RunShareBundleDocument> UpsertRunShareBundleAsync(RunShareBundleDocument bundle, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bundle.RunId))
        {
            throw new ArgumentException("RunId is required.", nameof(bundle));
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        bundle.BundleJson = bundle.BundleJson?.Trim() ?? string.Empty;
        if (bundle.CreatedAtUtc == default)
        {
            bundle.CreatedAtUtc = DateTime.UtcNow;
        }

        var existing = await db.RunShareBundles.FirstOrDefaultAsync(x => x.RunId == bundle.RunId, cancellationToken);
        if (existing is null)
        {
            if (string.IsNullOrWhiteSpace(bundle.Id))
            {
                bundle.Id = Guid.NewGuid().ToString("N");
            }

            db.RunShareBundles.Add(bundle);
            await db.SaveChangesAsync(cancellationToken);
            return bundle;
        }

        existing.RepositoryId = bundle.RepositoryId;
        existing.TaskId = bundle.TaskId;
        existing.BundleJson = bundle.BundleJson;
        existing.CreatedAtUtc = bundle.CreatedAtUtc;
        await db.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<RunShareBundleDocument?> GetRunShareBundleAsync(string runId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return null;
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.RunShareBundles.AsNoTracking()
            .Where(x => x.RunId == runId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<StructuredRunDataPruneResult> PruneStructuredRunDataAsync(
        DateTime olderThanUtc,
        int maxRuns,
        CancellationToken cancellationToken)
    {
        var normalizedMaxRuns = Math.Clamp(maxRuns, 1, 5000);
        var scanLimit = Math.Clamp(normalizedMaxRuns * 5, normalizedMaxRuns, 20_000);

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var runSeeds = await db.Runs.AsNoTracking()
            .Where(x =>
                (x.State == RunState.Succeeded ||
                 x.State == RunState.Failed ||
                 x.State == RunState.Cancelled ||
                 x.State == RunState.Obsolete) &&
                (x.EndedAtUtc ?? x.CreatedAtUtc) < olderThanUtc)
            .OrderBy(x => x.EndedAtUtc ?? x.CreatedAtUtc)
            .Select(x => new RunPruneSeed(x.Id, x.TaskId, x.RepositoryId))
            .Take(scanLimit)
            .ToListAsync(cancellationToken);

        if (runSeeds.Count == 0)
        {
            return new StructuredRunDataPruneResult(
                RunsScanned: 0,
                DeletedStructuredEvents: 0,
                DeletedDiffSnapshots: 0,
                DeletedToolProjections: 0);
        }

        var candidateRunSeeds = runSeeds;

        var runIds = candidateRunSeeds
            .Select(x => x.RunId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Take(normalizedMaxRuns)
            .ToList();

        if (runIds.Count == 0)
        {
            return new StructuredRunDataPruneResult(
                RunsScanned: 0,
                DeletedStructuredEvents: 0,
                DeletedDiffSnapshots: 0,
                DeletedToolProjections: 0);
        }

        var deletedStructuredEvents = await db.RunStructuredEvents.DeleteWhereAsync(x => runIds.Contains(x.RunId), cancellationToken);
        var deletedDiffSnapshots = await db.RunDiffSnapshots.DeleteWhereAsync(x => runIds.Contains(x.RunId), cancellationToken);
        var deletedToolProjections = await db.RunToolProjections.DeleteWhereAsync(x => runIds.Contains(x.RunId), cancellationToken);
        _ = await db.RunQuestionRequests.DeleteWhereAsync(x => runIds.Contains(x.RunId), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return new StructuredRunDataPruneResult(
            RunsScanned: runIds.Count,
            DeletedStructuredEvents: deletedStructuredEvents,
            DeletedDiffSnapshots: deletedDiffSnapshots,
            DeletedToolProjections: deletedToolProjections);
    }


    public async Task<ReliabilityMetrics> GetReliabilityMetricsAsync(CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var now = DateTime.UtcNow;
        var sevenDaysAgo = now.AddDays(-7);
        var thirtyDaysAgo = now.AddDays(-30);
        var fourteenDaysAgo = now.AddDays(-14);

        var recentRuns = await db.Runs.AsNoTracking().Where(x => x.CreatedAtUtc >= thirtyDaysAgo).ToListAsync(cancellationToken);

        var runs7Days = recentRuns.Where(r => r.CreatedAtUtc >= sevenDaysAgo).ToList();
        var runs30Days = recentRuns.ToList();

        var successRate7Days = CalculateSuccessRate(runs7Days);
        var successRate30Days = CalculateSuccessRate(runs30Days);

        var runsByState = recentRuns
            .GroupBy(r => r.State.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        var failureTrend = CalculateFailureTrend(recentRuns.Where(r => r.CreatedAtUtc >= fourteenDaysAgo).ToList(), fourteenDaysAgo, now);
        var avgDuration = CalculateAverageDuration(recentRuns);

        var repositories = await db.Repositories.AsNoTracking().ToListAsync(cancellationToken);
        var repositoryMetrics = CalculateRepositoryMetrics(recentRuns, repositories);

        return new ReliabilityMetrics(successRate7Days, successRate30Days, runs7Days.Count, runs30Days.Count, runsByState, failureTrend, avgDuration, repositoryMetrics);
    }

    private static double CalculateSuccessRate(List<RunDocument> runs)
    {
        if (runs.Count == 0)
            return 0;

        var succeeded = runs.Count(r => IsCompletionSuccessState(r.State));
        var failed = runs.Count(r => IsCompletionErrorState(r.State));
        var successEligibleCount = succeeded + failed;
        if (successEligibleCount == 0)
            return 0;

        return Math.Round((double)succeeded / successEligibleCount * 100, 1);
    }

    private static List<DailyFailureCount> CalculateFailureTrend(List<RunDocument> runs, DateTime start, DateTime end)
    {
        var result = new List<DailyFailureCount>();
        var failedRuns = runs.Where(r => r.State == RunState.Failed).ToList();

        for (var date = start.Date; date <= end.Date; date = date.AddDays(1))
        {
            var count = failedRuns.Count(r => r.CreatedAtUtc.Date == date);
            result.Add(new DailyFailureCount(date, count));
        }

        return result;
    }

    private static double? CalculateAverageDuration(List<RunDocument> runs)
    {
        var completedRuns = runs.Where(r => r.StartedAtUtc.HasValue && r.EndedAtUtc.HasValue).ToList();
        if (completedRuns.Count == 0)
            return null;

        var avgSeconds = completedRuns.Average(r => (r.EndedAtUtc!.Value - r.StartedAtUtc!.Value).TotalSeconds);
        return Math.Round(avgSeconds, 1);
    }

    private static List<RepositoryReliabilityMetrics> CalculateRepositoryMetrics(List<RunDocument> runs, List<RepositoryDocument> repositories)
    {
        var repositoryDict = repositories.ToDictionary(r => r.Id, r => r.Name);
        var repositoryRuns = runs.GroupBy(r => r.RepositoryId).ToList();

        return repositoryRuns.Select(g =>
        {
            var repositoryRunsList = g.ToList();
            var total = repositoryRunsList.Count;
            var succeeded = repositoryRunsList.Count(r => IsCompletionSuccessState(r.State));
            var failed = repositoryRunsList.Count(r => IsCompletionErrorState(r.State));
            var successEligibleCount = succeeded + failed;
            var rate = successEligibleCount > 0 ? Math.Round((double)succeeded / successEligibleCount * 100, 1) : 0;

            return new RepositoryReliabilityMetrics(
                g.Key,
                repositoryDict.GetValueOrDefault(g.Key, "Unknown"),
                total,
                succeeded,
                failed,
                rate);
        }).OrderByDescending(p => p.TotalRuns).ToList();
    }

    private static bool IsCompletionState(RunState state)
    {
        return state is RunState.Succeeded or RunState.Failed or RunState.Cancelled or RunState.Obsolete;
    }

    private static bool IsCompletionErrorState(RunState state)
    {
        return IsCompletionState(state) && state == RunState.Failed;
    }

    private static bool IsCompletionSuccessState(RunState state)
    {
        return IsCompletionState(state) && state == RunState.Succeeded;
    }


    private static RunToolProjectionDocument? CreateToolProjection(RunStructuredEventDocument structuredEvent)
    {
        var eventType = structuredEvent.EventType?.Trim() ?? string.Empty;
        var category = structuredEvent.Category?.Trim() ?? string.Empty;
        var payloadJson = structuredEvent.PayloadJson?.Trim() ?? string.Empty;
        var toolCallId = string.Empty;
        var toolName = string.Empty;
        var status = string.Empty;
        var inputJson = string.Empty;
        var outputJson = string.Empty;
        var error = structuredEvent.Error?.Trim() ?? string.Empty;
        var isToolEvent =
            eventType.Contains("tool", StringComparison.OrdinalIgnoreCase) ||
            category.Contains("tool", StringComparison.OrdinalIgnoreCase);

        if (payloadJson.Length > 0)
        {
            try
            {
                using var payloadDocument = JsonDocument.Parse(payloadJson);
                if (payloadDocument.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var payloadRoot = payloadDocument.RootElement;
                    toolCallId = ReadJsonString(payloadRoot, "toolCallId", "tool_call_id", "callId", "call_id", "id");
                    toolName = ReadJsonString(payloadRoot, "toolName", "tool_name", "name", "tool", "tool_name_normalized");
                    status = ReadJsonString(payloadRoot, "status", "state", "phase");
                    inputJson = ReadJsonRaw(payloadRoot, "input", "arguments", "inputJson", "input_json");
                    outputJson = ReadJsonRaw(payloadRoot, "output", "result", "outputJson", "output_json");
                    if (error.Length == 0)
                    {
                        error = ReadJsonString(payloadRoot, "error", "message", "failure");
                    }

                    isToolEvent = isToolEvent || !string.IsNullOrWhiteSpace(toolCallId) || !string.IsNullOrWhiteSpace(toolName);
                }
            }
            catch (JsonException)
            {
            }
        }

        if (!isToolEvent)
        {
            return null;
        }

        return new RunToolProjectionDocument
        {
            RunId = structuredEvent.RunId,
            RepositoryId = structuredEvent.RepositoryId,
            TaskId = structuredEvent.TaskId,
            ToolCallId = toolCallId,
            SequenceStart = structuredEvent.Sequence,
            SequenceEnd = structuredEvent.Sequence,
            ToolName = toolName,
            Status = status.Length == 0 ? (category.Length == 0 ? eventType : category) : status,
            InputJson = inputJson.Length == 0 ? payloadJson : inputJson,
            OutputJson = outputJson,
            Error = error,
            SchemaVersion = structuredEvent.SchemaVersion,
            TimestampUtc = structuredEvent.TimestampUtc,
            CreatedAtUtc = structuredEvent.CreatedAtUtc,
        };
    }

    private static RunQuestionRequestDocument? CreateQuestionRequestProjection(
        RunStructuredEventDocument structuredEvent,
        HarnessExecutionMode executionMode)
    {
        if (executionMode != HarnessExecutionMode.Plan || string.IsNullOrWhiteSpace(structuredEvent.PayloadJson))
        {
            return null;
        }

        try
        {
            using var payloadDocument = JsonDocument.Parse(structuredEvent.PayloadJson);
            if (payloadDocument.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var payloadRoot = payloadDocument.RootElement;
            var toolName = ReadJsonString(
                payloadRoot,
                "toolName",
                "tool_name",
                "name",
                "tool",
                "function",
                "function_name");
            if (!string.Equals(toolName, "request_user_input", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var questions = ParseQuestionItems(payloadRoot);
            if (questions.Count == 0)
            {
                return null;
            }

            var toolCallId = ReadJsonString(payloadRoot, "toolCallId", "tool_call_id", "callId", "call_id", "id");
            var createdAtUtc = structuredEvent.CreatedAtUtc == default
                ? DateTime.UtcNow
                : structuredEvent.CreatedAtUtc;
            var timestampUtc = structuredEvent.TimestampUtc == default
                ? createdAtUtc
                : structuredEvent.TimestampUtc;

            return new RunQuestionRequestDocument
            {
                RepositoryId = structuredEvent.RepositoryId,
                TaskId = structuredEvent.TaskId,
                RunId = structuredEvent.RunId,
                SourceToolCallId = toolCallId,
                SourceToolName = toolName,
                SourceSequence = structuredEvent.Sequence,
                SourceSchemaVersion = structuredEvent.SchemaVersion?.Trim() ?? string.Empty,
                Status = RunQuestionRequestStatus.Pending,
                Questions = questions,
                Answers = [],
                CreatedAtUtc = timestampUtc,
                UpdatedAtUtc = timestampUtc,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<RunQuestionItemDocument> ParseQuestionItems(JsonElement payloadRoot)
    {
        if (!TryResolveQuestionArray(payloadRoot, out var questionsArray))
        {
            return [];
        }

        var questions = new List<RunQuestionItemDocument>();
        var index = 0;
        foreach (var questionElement in questionsArray.EnumerateArray())
        {
            if (questionElement.ValueKind != JsonValueKind.Object)
            {
                index++;
                continue;
            }

            var questionId = ReadJsonString(questionElement, "id", "questionId", "question_id");
            if (string.IsNullOrWhiteSpace(questionId))
            {
                questionId = $"question-{index + 1}";
            }

            var prompt = ReadJsonString(questionElement, "question", "prompt", "text");
            if (string.IsNullOrWhiteSpace(prompt))
            {
                prompt = ReadJsonRaw(questionElement, "question", "prompt", "text");
            }

            var header = ReadJsonString(questionElement, "header", "title", "name");
            var options = ParseQuestionOptions(questionElement);
            if (string.IsNullOrWhiteSpace(prompt) || options.Count == 0)
            {
                index++;
                continue;
            }

            questions.Add(new RunQuestionItemDocument
            {
                Id = questionId.Trim(),
                Header = header.Trim(),
                Prompt = prompt.Trim(),
                Order = index,
                Options = options,
            });

            index++;
        }

        return NormalizeQuestionItems(questions);
    }

    private static List<RunQuestionOptionDocument> ParseQuestionOptions(JsonElement questionElement)
    {
        if (!TryResolveOptionsArray(questionElement, out var optionsArray))
        {
            return [];
        }

        var options = new List<RunQuestionOptionDocument>();
        var index = 0;
        foreach (var optionElement in optionsArray.EnumerateArray())
        {
            if (optionElement.ValueKind != JsonValueKind.Object &&
                optionElement.ValueKind != JsonValueKind.String)
            {
                index++;
                continue;
            }

            var value = string.Empty;
            var label = string.Empty;
            var description = string.Empty;
            if (optionElement.ValueKind == JsonValueKind.String)
            {
                label = optionElement.GetString() ?? string.Empty;
                value = label;
            }
            else
            {
                value = ReadJsonString(optionElement, "value", "id", "key", "label");
                label = ReadJsonString(optionElement, "label", "title", "name", "value");
                description = ReadJsonString(optionElement, "description", "detail", "hint");
            }

            if (string.IsNullOrWhiteSpace(label))
            {
                index++;
                continue;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                value = label;
            }

            options.Add(new RunQuestionOptionDocument
            {
                Value = value.Trim(),
                Label = label.Trim(),
                Description = description.Trim(),
            });

            index++;
        }

        return options;
    }

    private static bool TryResolveQuestionArray(JsonElement payloadRoot, out JsonElement questionsArray)
    {
        if (TryGetArrayProperty(payloadRoot, "questions", out questionsArray))
        {
            return true;
        }

        if (TryGetObjectProperty(payloadRoot, "input", out var inputObject) &&
            TryGetArrayProperty(inputObject, "questions", out questionsArray))
        {
            return true;
        }

        if (TryGetObjectProperty(payloadRoot, "arguments", out var argumentsObject) &&
            TryGetArrayProperty(argumentsObject, "questions", out questionsArray))
        {
            return true;
        }

        if (TryGetObjectProperty(payloadRoot, "params", out var paramsObject) &&
            TryGetArrayProperty(paramsObject, "questions", out questionsArray))
        {
            return true;
        }

        if (TryGetObjectProperty(payloadRoot, "request", out var requestObject) &&
            TryGetArrayProperty(requestObject, "questions", out questionsArray))
        {
            return true;
        }

        questionsArray = default;
        return false;
    }

    private static bool TryResolveOptionsArray(JsonElement questionElement, out JsonElement optionsArray)
    {
        if (TryGetArrayProperty(questionElement, "options", out optionsArray))
        {
            return true;
        }

        if (TryGetArrayProperty(questionElement, "choices", out optionsArray))
        {
            return true;
        }

        optionsArray = default;
        return false;
    }

    private static bool TryGetObjectProperty(JsonElement element, string propertyName, out JsonElement propertyValue)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            propertyValue = default;
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                break;
            }

            propertyValue = property.Value;
            return true;
        }

        propertyValue = default;
        return false;
    }

    private static bool TryGetArrayProperty(JsonElement element, string propertyName, out JsonElement propertyValue)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            propertyValue = default;
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind != JsonValueKind.Array)
            {
                break;
            }

            propertyValue = property.Value;
            return true;
        }

        propertyValue = default;
        return false;
    }

    private static List<RunQuestionItemDocument> NormalizeQuestionItems(IReadOnlyList<RunQuestionItemDocument> source)
    {
        if (source.Count == 0)
        {
            return [];
        }

        var normalized = new List<RunQuestionItemDocument>(source.Count);
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < source.Count; index++)
        {
            var item = source[index];
            var id = item.Id?.Trim() ?? string.Empty;
            if (id.Length == 0 || !seenIds.Add(id))
            {
                id = $"question-{index + 1}";
                while (!seenIds.Add(id))
                {
                    id = $"{id}-x";
                }
            }

            var prompt = item.Prompt?.Trim() ?? string.Empty;
            if (prompt.Length == 0)
            {
                continue;
            }

            var options = item.Options
                .Where(option => !string.IsNullOrWhiteSpace(option.Label))
                .Select(option =>
                {
                    var value = option.Value?.Trim() ?? string.Empty;
                    var label = option.Label.Trim();
                    if (value.Length == 0)
                    {
                        value = label;
                    }

                    return new RunQuestionOptionDocument
                    {
                        Value = value,
                        Label = label,
                        Description = option.Description?.Trim() ?? string.Empty,
                    };
                })
                .ToList();
            if (options.Count == 0)
            {
                continue;
            }

            normalized.Add(new RunQuestionItemDocument
            {
                Id = id,
                Header = item.Header?.Trim() ?? string.Empty,
                Prompt = prompt,
                Order = index,
                Options = options,
            });
        }

        return normalized;
    }

    private static List<RunQuestionAnswerDocument> NormalizeQuestionAnswers(IReadOnlyList<RunQuestionAnswerDocument> answers)
    {
        if (answers.Count == 0)
        {
            return [];
        }

        var normalized = new List<RunQuestionAnswerDocument>(answers.Count);
        foreach (var answer in answers)
        {
            var questionId = answer.QuestionId?.Trim() ?? string.Empty;
            var selectedOptionLabel = answer.SelectedOptionLabel?.Trim() ?? string.Empty;
            if (questionId.Length == 0 || selectedOptionLabel.Length == 0)
            {
                continue;
            }

            normalized.Add(new RunQuestionAnswerDocument
            {
                QuestionId = questionId,
                SelectedOptionValue = answer.SelectedOptionValue?.Trim() ?? string.Empty,
                SelectedOptionLabel = selectedOptionLabel,
                SelectedOptionDescription = answer.SelectedOptionDescription?.Trim() ?? string.Empty,
                AdditionalContext = answer.AdditionalContext?.Trim() ?? string.Empty,
            });
        }

        return normalized;
    }

    private static string ReadJsonString(JsonElement root, params string[] propertyNames)
    {
        if (root.ValueKind != JsonValueKind.Object || propertyNames.Length == 0)
        {
            return string.Empty;
        }

        foreach (var property in root.EnumerateObject())
        {
            foreach (var propertyName in propertyNames)
            {
                if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return property.Value.ValueKind switch
                {
                    JsonValueKind.Null => string.Empty,
                    JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                    _ => property.Value.GetRawText()
                };
            }
        }

        return string.Empty;
    }

    private static string ReadJsonRaw(JsonElement root, params string[] propertyNames)
    {
        if (root.ValueKind != JsonValueKind.Object || propertyNames.Length == 0)
        {
            return string.Empty;
        }

        foreach (var property in root.EnumerateObject())
        {
            foreach (var propertyName in propertyNames)
            {
                if (!string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (property.Value.ValueKind == JsonValueKind.Null)
                {
                    return string.Empty;
                }

                return property.Value.GetRawText();
            }
        }

        return string.Empty;
    }

    private static string NormalizeHarnessValue(string harness)
    {
        return NormalizeRequiredValue(harness, nameof(harness)).ToLowerInvariant();
    }

    private static string NormalizeRequiredValue(string value, string parameterName)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Value is required.", parameterName);
        }

        return normalized;
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
                return System.Text.Json.JsonSerializer.Deserialize<double[]>(trimmed, (JsonSerializerOptions?)null);
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

    private Task DeleteStoredArtifactsByRunIdsAsync(IReadOnlyList<string> runIds, CancellationToken cancellationToken)
    {
        if (runIds.Count == 0)
        {
            return Task.CompletedTask;
        }

        return liteDbExecutor.ExecuteAsync(
            db =>
            {
                var metadataCollection = db.GetCollection<RunArtifactDocument>("run_artifacts");
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

    private static string NormalizeArtifactFileName(string fileName)
    {
        var normalized = Path.GetFileName(fileName?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Artifact file name is required.", nameof(fileName));
        }

        return normalized;
    }

    private static string BuildArtifactId(string runId, string fileName)
    {
        return $"{runId.Trim()}::{fileName}";
    }

    private static string BuildArtifactFileStorageId(string runId, string fileName)
    {
        return $"{ArtifactFileStorageRoot}/{runId.Trim()}/{fileName}";
    }

    private sealed record RunPruneSeed(
        string RunId,
        string TaskId,
        string RepositoryId);

}
