namespace AgentsDashboard.ControlPlane.Infrastructure.Data;

public sealed class TaskStore(
    IOrchestratorRepositorySessionFactory liteDbScopeFactory,
    LiteDbExecutor liteDbExecutor,
    LiteDbDatabase liteDbDatabase) : ITaskStore
{
    private static readonly RunState[] ActiveStates = [RunState.Queued, RunState.Running, RunState.PendingApproval];
    private const string TaskWorkspacesRootPath = "/workspaces/repos";
    private const string ArtifactFileStorageRoot = "$/run-artifacts";

    public async Task<TaskDocument> CreateTaskAsync(CreateTaskRequest request, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var repository = await db.Repositories.FirstOrDefaultAsync(x => x.Id == request.RepositoryId, cancellationToken);
        if (repository is null)
        {
            throw new InvalidOperationException($"Repository '{request.RepositoryId}' was not found.");
        }

        repository.TaskDefaults = NormalizeRepositoryTaskDefaults(repository.TaskDefaults);
        var normalizedPrompt = request.Prompt.Trim();
        if (normalizedPrompt.Length == 0)
        {
            throw new ArgumentException("Prompt is required.", nameof(request.Prompt));
        }

        var normalizedName = string.IsNullOrWhiteSpace(request.Name)
            ? BuildTaskNameFromPrompt(normalizedPrompt)
            : request.Name.Trim();

        var task = new TaskDocument
        {
            RepositoryId = request.RepositoryId,
            Name = normalizedName,
            Harness = repository.TaskDefaults.Harness,
            ExecutionModeDefault = repository.TaskDefaults.ExecutionModeDefault,
            SessionProfileId = repository.TaskDefaults.SessionProfileId,
            Prompt = normalizedPrompt,
            Command = repository.TaskDefaults.Command,
            AutoCreatePullRequest = repository.TaskDefaults.AutoCreatePullRequest,
            Enabled = repository.TaskDefaults.Enabled,
            RetryPolicy = new RetryPolicyConfig(),
            Timeouts = new TimeoutConfig(),
            SandboxProfile = new SandboxProfileConfig(),
            ArtifactPolicy = new ArtifactPolicyConfig(),
            ApprovalProfile = new ApprovalProfileConfig(),
            ConcurrencyLimit = 0,
            InstructionFiles = [],
            ArtifactPatterns = [],
            LinkedFailureRuns = request.LinkedFailureRuns ?? [],
        };

        task.NextRunAtUtc = ComputeNextRun(task, DateTime.UtcNow);
        db.Tasks.Add(task);
        await db.SaveChangesAsync(cancellationToken);
        return task;
    }

    public async Task<List<TaskDocument>> ListTasksAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.Tasks.AsNoTracking().Where(x => x.RepositoryId == repositoryId).OrderBy(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task<TaskDocument?> GetTaskAsync(string taskId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.Tasks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == taskId, cancellationToken);
    }

    public async Task<List<TaskDocument>> ListDueTasksAsync(DateTime utcNow, int limit, CancellationToken cancellationToken)
    {
        _ = utcNow;
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.Tasks.AsNoTracking()
            .Where(x => x.Enabled)
            .OrderBy(x => x.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<TaskDocument?> UpdateTaskGitMetadataAsync(
        string taskId,
        DateTime? lastGitSyncAtUtc,
        string? lastGitSyncError,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return null;
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var task = await db.Tasks.FirstOrDefaultAsync(x => x.Id == taskId, cancellationToken);
        if (task is null)
        {
            return null;
        }

        if (lastGitSyncAtUtc.HasValue)
        {
            task.LastGitSyncAtUtc = lastGitSyncAtUtc.Value;
        }

        if (lastGitSyncError is not null)
        {
            task.LastGitSyncError = lastGitSyncError;
        }

        await db.SaveChangesAsync(cancellationToken);
        return task;
    }

    public async Task<TaskDocument?> UpdateTaskAsync(string taskId, UpdateTaskRequest request, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var task = await db.Tasks.FirstOrDefaultAsync(x => x.Id == taskId, cancellationToken);
        if (task is null)
            return null;

        task.Name = request.Name;
        task.Harness = request.Harness.Trim().ToLowerInvariant();
        task.ExecutionModeDefault = request.ExecutionModeDefault;
        task.SessionProfileId = request.SessionProfileId?.Trim() ?? string.Empty;
        task.Prompt = request.Prompt;
        task.Command = request.Command;
        task.AutoCreatePullRequest = request.AutoCreatePullRequest;
        task.Enabled = request.Enabled;
        task.RetryPolicy = request.RetryPolicy ?? new RetryPolicyConfig();
        task.Timeouts = request.Timeouts ?? new TimeoutConfig();
        task.SandboxProfile = request.SandboxProfile ?? new SandboxProfileConfig();
        task.ArtifactPolicy = request.ArtifactPolicy ?? new ArtifactPolicyConfig();
        task.ApprovalProfile = request.ApprovalProfile ?? new ApprovalProfileConfig();
        task.ConcurrencyLimit = request.ConcurrencyLimit ?? 0;
        task.InstructionFiles = request.InstructionFiles ?? [];
        task.ArtifactPatterns = request.ArtifactPatterns ?? [];
        task.LinkedFailureRuns = request.LinkedFailureRuns ?? [];
        task.NextRunAtUtc = ComputeNextRun(task, DateTime.UtcNow);

        await db.SaveChangesAsync(cancellationToken);
        return task;
    }

    public async Task<bool> DeleteTaskAsync(string taskId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var task = await db.Tasks.FirstOrDefaultAsync(x => x.Id == taskId, cancellationToken);
        if (task is null)
            return false;

        var repositoryId = task.RepositoryId;
        db.Tasks.Remove(task);
        await db.SaveChangesAsync(cancellationToken);

        TryDeleteTaskWorkspaceDirectory(repositoryId, taskId, out _, out _);
        return true;
    }

    public async Task<DbStorageSnapshot> GetStorageSnapshotAsync(CancellationToken cancellationToken)
    {
        var measuredAtUtc = DateTime.UtcNow;
        var databasePath = liteDbDatabase.DatabasePath;
        if (string.IsNullOrWhiteSpace(databasePath) || string.Equals(databasePath, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return new DbStorageSnapshot(
                string.Empty,
                0,
                0,
                0,
                false,
                measuredAtUtc);
        }

        if (!Path.IsPathRooted(databasePath))
        {
            databasePath = Path.GetFullPath(databasePath);
        }

        var mainFileBytes = 0L;
        if (File.Exists(databasePath))
        {
            mainFileBytes = new FileInfo(databasePath).Length;
        }

        return new DbStorageSnapshot(
            databasePath,
            mainFileBytes,
            0,
            mainFileBytes,
            File.Exists(databasePath),
            measuredAtUtc);
    }

    public async Task<List<TaskCleanupCandidate>> ListTaskCleanupCandidatesAsync(TaskCleanupQuery query, CancellationToken cancellationToken)
    {
        var normalizedLimit = Math.Clamp(query.Limit, 1, 1000);
        var normalizedScanLimit = Math.Clamp(
            query.ScanLimit > 0 ? query.ScanLimit : normalizedLimit * 20,
            normalizedLimit,
            800);
        var olderThanUtc = query.OlderThanUtc == default ? DateTime.UtcNow : query.OlderThanUtc;
        var protectedSinceUtc = query.ProtectedSinceUtc;
        var includeRetentionEligibility = query.IncludeRetentionEligibility;
        var includeDisabledInactiveEligibility = query.IncludeDisabledInactiveEligibility;
        var disabledInactiveOlderThanUtc = includeDisabledInactiveEligibility
            ? (query.DisabledInactiveOlderThanUtc == default ? olderThanUtc : query.DisabledInactiveOlderThanUtc)
            : default;

        if (!includeRetentionEligibility && !includeDisabledInactiveEligibility)
        {
            return [];
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);

        var taskQuery = db.Tasks.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query.RepositoryId))
        {
            taskQuery = taskQuery.Where(x => x.RepositoryId == query.RepositoryId);
        }

        var seedBeforeUtc = olderThanUtc;
        if (includeDisabledInactiveEligibility && disabledInactiveOlderThanUtc > seedBeforeUtc)
        {
            seedBeforeUtc = disabledInactiveOlderThanUtc;
        }

        taskQuery = taskQuery.Where(x => x.CreatedAtUtc < seedBeforeUtc);
        if (protectedSinceUtc != default)
        {
            taskQuery = taskQuery.Where(x => x.CreatedAtUtc < protectedSinceUtc);
        }

        var taskSeeds = await taskQuery
            .OrderBy(x => x.CreatedAtUtc)
            .Take(normalizedScanLimit)
            .Select(x => new TaskCleanupSeed(x.Id, x.RepositoryId, x.CreatedAtUtc, x.Enabled))
            .ToListAsync(cancellationToken);

        if (taskSeeds.Count == 0)
        {
            return [];
        }

        var taskIds = taskSeeds.Select(x => x.TaskId).ToList();

        var runAggregates = await db.Runs.AsNoTracking()
            .Where(x => taskIds.Contains(x.TaskId))
            .GroupBy(x => x.TaskId)
            .Select(group => new TaskRunAggregate(
                group.Key,
                group.Count(),
                group.Min(x => (DateTime?)(x.EndedAtUtc ?? x.StartedAtUtc ?? x.CreatedAtUtc)),
                group.Max(x => (DateTime?)(x.EndedAtUtc ?? x.StartedAtUtc ?? x.CreatedAtUtc)),
                group.Any(x => ActiveStates.Contains(x.State))))
            .ToListAsync(cancellationToken);

        var logAggregates = await (
                from log in db.RunEvents.AsNoTracking()
                join run in db.Runs.AsNoTracking() on log.RunId equals run.Id
                where taskIds.Contains(run.TaskId)
                group log by run.TaskId into grouped
                select new TaskTimestampAggregate(
                    grouped.Key,
                    grouped.Max(x => (DateTime?)x.TimestampUtc)))
            .ToListAsync(cancellationToken);

        var promptAggregates = await db.WorkspacePromptEntries.AsNoTracking()
            .Where(x => taskIds.Contains(x.TaskId))
            .GroupBy(x => x.TaskId)
            .Select(group => new TaskTimestampAggregate(
                group.Key,
                group.Max(x => (DateTime?)x.CreatedAtUtc)))
            .ToListAsync(cancellationToken);

        var summaryAggregates = await db.RunAiSummaries.AsNoTracking()
            .Where(x => taskIds.Contains(x.TaskId))
            .GroupBy(x => x.TaskId)
            .Select(group => new TaskTimestampAggregate(
                group.Key,
                group.Max(x => (DateTime?)x.GeneratedAtUtc)))
            .ToListAsync(cancellationToken);

        var runByTask = runAggregates.ToDictionary(x => x.TaskId, StringComparer.Ordinal);
        var logsByTask = logAggregates.ToDictionary(x => x.TaskId, x => x.TimestampUtc, StringComparer.Ordinal);
        var promptsByTask = promptAggregates.ToDictionary(x => x.TaskId, x => x.TimestampUtc, StringComparer.Ordinal);
        var summariesByTask = summaryAggregates.ToDictionary(x => x.TaskId, x => x.TimestampUtc, StringComparer.Ordinal);

        var candidates = new List<TaskCleanupCandidate>(taskSeeds.Count);
        foreach (var task in taskSeeds)
        {
            runByTask.TryGetValue(task.TaskId, out var runAggregate);
            logsByTask.TryGetValue(task.TaskId, out var latestLogAtUtc);
            promptsByTask.TryGetValue(task.TaskId, out var latestPromptAtUtc);
            summariesByTask.TryGetValue(task.TaskId, out var latestSummaryAtUtc);

            var lastActivityUtc = MaxDateTime(
                task.CreatedAtUtc,
                runAggregate?.LatestRunAtUtc,
                latestLogAtUtc,
                latestPromptAtUtc,
                latestSummaryAtUtc);

            if (protectedSinceUtc != default && lastActivityUtc >= protectedSinceUtc)
            {
                continue;
            }

            if (query.OnlyWithNoActiveRuns && runAggregate?.HasActiveRuns == true)
            {
                continue;
            }

            var isRetentionEligible = includeRetentionEligibility && lastActivityUtc < olderThanUtc;
            var isDisabledInactiveEligible = includeDisabledInactiveEligibility &&
                                             !task.Enabled &&
                                             lastActivityUtc < disabledInactiveOlderThanUtc;
            if (!isRetentionEligible && !isDisabledInactiveEligible)
            {
                continue;
            }

            candidates.Add(new TaskCleanupCandidate(
                task.TaskId,
                task.RepositoryId,
                task.CreatedAtUtc,
                lastActivityUtc,
                runAggregate?.HasActiveRuns ?? false,
                runAggregate?.RunCount ?? 0,
                runAggregate?.OldestRunAtUtc,
                isRetentionEligible,
                isDisabledInactiveEligible));
        }

        return candidates
            .OrderBy(x => x.LastActivityUtc)
            .ThenBy(x => x.CreatedAtUtc)
            .Take(normalizedLimit)
            .ToList();
    }

    public async Task<TaskCascadeDeleteResult> DeleteTaskCascadeAsync(string taskId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return new TaskCascadeDeleteResult(
                TaskId: string.Empty,
                RepositoryId: string.Empty,
                TaskDeleted: false,
                DeletedRuns: 0,
                DeletedRunLogs: 0,
                DeletedPromptEntries: 0,
                DeletedRunSummaries: 0,
                DeletedSemanticChunks: 0,
                DeletedArtifactDirectories: 0,
                ArtifactDeleteErrors: 0,
                DeletedTaskWorkspaceDirectories: 0,
                TaskWorkspaceDeleteErrors: 0);
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var task = await db.Tasks.AsNoTracking()
            .Where(x => x.Id == taskId)
            .Select(x => new { x.Id, x.RepositoryId })
            .FirstOrDefaultAsync(cancellationToken);

        if (task is null)
        {
            return new TaskCascadeDeleteResult(
                TaskId: taskId,
                RepositoryId: string.Empty,
                TaskDeleted: false,
                DeletedRuns: 0,
                DeletedRunLogs: 0,
                DeletedPromptEntries: 0,
                DeletedRunSummaries: 0,
                DeletedSemanticChunks: 0,
                DeletedArtifactDirectories: 0,
                ArtifactDeleteErrors: 0,
                DeletedTaskWorkspaceDirectories: 0,
                TaskWorkspaceDeleteErrors: 0);
        }

        var runIds = await db.Runs.AsNoTracking()
            .Where(x => x.TaskId == taskId)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
        runIds = runIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var deletedRunLogs = 0;
        var deletedPromptEntries = 0;
        var deletedRunSummaries = 0;
        var deletedSemanticChunks = 0;
        var deletedRuns = 0;
        var taskDeleted = false;

        if (runIds.Count > 0)
        {
            deletedRunLogs = await db.RunEvents.DeleteWhereAsync(x => runIds.Contains(x.RunId), cancellationToken);
            _ = await db.RunStructuredEvents.DeleteWhereAsync(x => runIds.Contains(x.RunId), cancellationToken);
            _ = await db.RunDiffSnapshots.DeleteWhereAsync(x => runIds.Contains(x.RunId), cancellationToken);
            _ = await db.RunToolProjections.DeleteWhereAsync(x => runIds.Contains(x.RunId), cancellationToken);
            _ = await db.RunQuestionRequests.DeleteWhereAsync(x => runIds.Contains(x.RunId), cancellationToken);
        }

        deletedPromptEntries = await db.WorkspacePromptEntries.DeleteWhereAsync(x => x.TaskId == taskId, cancellationToken);
        if (runIds.Count > 0)
        {
            deletedPromptEntries += await db.WorkspacePromptEntries.DeleteWhereAsync(x => runIds.Contains(x.RunId), cancellationToken);
        }

        deletedRunSummaries = await db.RunAiSummaries.DeleteWhereAsync(x => x.TaskId == taskId, cancellationToken);
        if (runIds.Count > 0)
        {
            deletedRunSummaries += await db.RunAiSummaries.DeleteWhereAsync(x => runIds.Contains(x.RunId), cancellationToken);
        }

        deletedSemanticChunks = await db.SemanticChunks.DeleteWhereAsync(x => x.TaskId == taskId, cancellationToken);
        if (runIds.Count > 0)
        {
            deletedSemanticChunks += await db.SemanticChunks.DeleteWhereAsync(x => runIds.Contains(x.RunId), cancellationToken);
        }

        deletedRuns = await db.Runs.DeleteWhereAsync(x => x.TaskId == taskId, cancellationToken);
        taskDeleted = await db.Tasks.DeleteWhereAsync(x => x.Id == taskId, cancellationToken) > 0;
        await db.SaveChangesAsync(cancellationToken);

        var deletedArtifactDirectories = 0;
        var artifactDeleteErrors = 0;
        try
        {
            await DeleteStoredArtifactsByRunIdsAsync(runIds, cancellationToken);
        }
        catch
        {
            artifactDeleteErrors++;
        }

        var deletedTaskWorkspaceDirectories = 0;
        var taskWorkspaceDeleteErrors = 0;
        if (taskDeleted)
        {
            TryDeleteTaskWorkspaceDirectory(task.RepositoryId, taskId, out deletedTaskWorkspaceDirectories, out taskWorkspaceDeleteErrors);
        }

        return new TaskCascadeDeleteResult(
            TaskId: taskId,
            RepositoryId: task.RepositoryId,
            TaskDeleted: taskDeleted,
            DeletedRuns: deletedRuns,
            DeletedRunLogs: deletedRunLogs,
            DeletedPromptEntries: deletedPromptEntries,
            DeletedRunSummaries: deletedRunSummaries,
            DeletedSemanticChunks: deletedSemanticChunks,
            DeletedArtifactDirectories: deletedArtifactDirectories,
            ArtifactDeleteErrors: artifactDeleteErrors,
            DeletedTaskWorkspaceDirectories: deletedTaskWorkspaceDirectories,
            TaskWorkspaceDeleteErrors: taskWorkspaceDeleteErrors);
    }

    public async Task<CleanupBatchResult> DeleteTasksCascadeAsync(IReadOnlyList<string> taskIds, CancellationToken cancellationToken)
    {
        if (taskIds.Count == 0)
        {
            return new CleanupBatchResult(
                TasksRequested: 0,
                TasksDeleted: 0,
                FailedTasks: 0,
                DeletedRuns: 0,
                DeletedRunLogs: 0,
                DeletedPromptEntries: 0,
                DeletedRunSummaries: 0,
                DeletedSemanticChunks: 0,
                DeletedArtifactDirectories: 0,
                ArtifactDeleteErrors: 0,
                DeletedTaskWorkspaceDirectories: 0,
                TaskWorkspaceDeleteErrors: 0);
        }

        var normalizedTaskIds = taskIds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var tasksDeleted = 0;
        var failedTasks = 0;
        var deletedRuns = 0;
        var deletedRunLogs = 0;
        var deletedPromptEntries = 0;
        var deletedRunSummaries = 0;
        var deletedSemanticChunks = 0;
        var deletedArtifactDirectories = 0;
        var artifactDeleteErrors = 0;
        var deletedTaskWorkspaceDirectories = 0;
        var taskWorkspaceDeleteErrors = 0;

        foreach (var taskId in normalizedTaskIds)
        {
            try
            {
                var result = await DeleteTaskCascadeAsync(taskId, cancellationToken);
                if (result.TaskDeleted)
                {
                    tasksDeleted++;
                }

                deletedRuns += result.DeletedRuns;
                deletedRunLogs += result.DeletedRunLogs;
                deletedPromptEntries += result.DeletedPromptEntries;
                deletedRunSummaries += result.DeletedRunSummaries;
                deletedSemanticChunks += result.DeletedSemanticChunks;
                deletedArtifactDirectories += result.DeletedArtifactDirectories;
                artifactDeleteErrors += result.ArtifactDeleteErrors;
                deletedTaskWorkspaceDirectories += result.DeletedTaskWorkspaceDirectories;
                taskWorkspaceDeleteErrors += result.TaskWorkspaceDeleteErrors;
            }
            catch
            {
                failedTasks++;
            }
        }

        return new CleanupBatchResult(
            TasksRequested: normalizedTaskIds.Count,
            TasksDeleted: tasksDeleted,
            FailedTasks: failedTasks,
            DeletedRuns: deletedRuns,
            DeletedRunLogs: deletedRunLogs,
            DeletedPromptEntries: deletedPromptEntries,
            DeletedRunSummaries: deletedRunSummaries,
            DeletedSemanticChunks: deletedSemanticChunks,
            DeletedArtifactDirectories: deletedArtifactDirectories,
            ArtifactDeleteErrors: artifactDeleteErrors,
            DeletedTaskWorkspaceDirectories: deletedTaskWorkspaceDirectories,
            TaskWorkspaceDeleteErrors: taskWorkspaceDeleteErrors);
    }

    public async Task VacuumAsync(CancellationToken cancellationToken)

    private static string BuildTaskNameFromPrompt(string prompt)
    {
        var firstLine = prompt
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;

        var normalized = Regex.Replace(firstLine, @"\s+", " ").Trim();
        if (normalized.Length == 0)
        {
            return $"Task {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
        }

        return normalized.Length <= 80
            ? normalized
            : normalized[..80].TrimEnd();
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

    private static DateTime MaxDateTime(params DateTime?[] values)
    {
        var max = DateTime.MinValue;
        foreach (var value in values)
        {
            if (!value.HasValue)
            {
                continue;
            }

            if (value.Value > max)
            {
                max = value.Value;
            }
        }

        return max == DateTime.MinValue ? DateTime.UtcNow : max;
    }

    private static void TryDeleteTaskWorkspaceDirectory(
        string repositoryId,
        string taskId,
        out int deletedDirectories,
        out int deleteErrors)
    {
        deletedDirectories = 0;
        deleteErrors = 0;

        var workspaceDirectory = BuildTaskWorkspaceDirectoryPath(repositoryId, taskId);
        if (!Directory.Exists(workspaceDirectory))
        {
            return;
        }

        try
        {
            Directory.Delete(workspaceDirectory, true);
            deletedDirectories = 1;
        }
        catch
        {
            deleteErrors = 1;
        }
    }

    private static string BuildTaskWorkspaceDirectoryPath(string repositoryId, string taskId)
    {
        return Path.Combine(
            TaskWorkspacesRootPath,
            SanitizePathSegment(repositoryId),
            "tasks",
            SanitizePathSegment(taskId));
    }

    private static string SanitizePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        return value.Trim().Replace('/', '-').Replace('\\', '-');
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

    public static DateTime? ComputeNextRun(TaskDocument task, DateTime nowUtc)
    {
        if (!task.Enabled)
        {
            return null;
        }

        return nowUtc;
    }


            document[nestedDocumentField] = nestedDocument;
            collection.Update(document);
        }
    }

    private sealed record TaskCleanupSeed(
        string TaskId,
        string RepositoryId,
        DateTime CreatedAtUtc,
        bool Enabled);

    private sealed record TaskRunAggregate(
        string TaskId,
        int RunCount,
        DateTime? OldestRunAtUtc,
        DateTime? LatestRunAtUtc,
        bool HasActiveRuns);

    private sealed record TaskTimestampAggregate(
        string TaskId,
        DateTime? TimestampUtc);

    private sealed record RunPruneSeed(
        string RunId,
        string TaskId,
        string RepositoryId);
}

}
