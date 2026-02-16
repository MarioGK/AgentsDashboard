using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;

namespace AgentsDashboard.ControlPlane.Services;

public interface IWorkspaceSearchService
{
    Task<WorkspaceSearchResult> SearchAsync(WorkspaceSearchRequest request, CancellationToken cancellationToken);
}

public sealed record WorkspaceSearchRequest(
    string RepositoryId,
    string Query,
    int Limit = 25,
    bool IncludeRunLogs = true);

public sealed record WorkspaceSearchResult(
    string RepositoryId,
    string Query,
    bool SqliteVecAvailable,
    string? SqliteVecDetail,
    IReadOnlyList<WorkspaceSearchHit> Hits);

public sealed record WorkspaceSearchHit(
    string Kind,
    string Id,
    string Title,
    string Snippet,
    double Score,
    double KeywordScore,
    double SemanticScore,
    DateTime? TimestampUtc,
    string? RunId,
    string? TaskId);

public sealed class WorkspaceSearchService(
    IOrchestratorStore store,
    IWorkspaceAiService workspaceAiService,
    IHarnessOutputParserService parserService,
    ISqliteVecBootstrapService sqliteVecBootstrapService,
    ILogger<WorkspaceSearchService> logger) : IWorkspaceSearchService
{
    private static readonly Regex s_tokenRegex = new("[a-z0-9_]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private const int MaxTaskCandidates = 200;
    private const int MaxRunCandidates = 200;
    private const int MaxFindingCandidates = 120;

    private static readonly IReadOnlyDictionary<string, string[]> s_semanticAliases =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["bug"] = ["error", "failure", "exception", "defect"],
            ["fix"] = ["patch", "repair", "resolve"],
            ["test"] = ["unit", "integration", "assert", "verification"],
            ["build"] = ["compile", "restore", "pipeline"],
            ["deploy"] = ["release", "rollout", "ship"],
            ["performance"] = ["latency", "throughput", "cpu", "memory"],
            ["security"] = ["vulnerability", "auth", "permission", "token"],
            ["prompt"] = ["instruction", "context", "task"],
            ["artifact"] = ["output", "report", "file"],
            ["timeout"] = ["slow", "deadline", "cancelled"],
        };

    private readonly ConcurrentDictionary<string, string> _indexedChunkHashes = new(StringComparer.Ordinal);

    public async Task<WorkspaceSearchResult> SearchAsync(WorkspaceSearchRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RepositoryId) || string.IsNullOrWhiteSpace(request.Query))
        {
            return new WorkspaceSearchResult(
                request.RepositoryId,
                request.Query,
                sqliteVecBootstrapService.IsAvailable,
                sqliteVecBootstrapService.Status.Detail,
                []);
        }

        var repository = await store.GetRepositoryAsync(request.RepositoryId, cancellationToken);
        if (repository is null)
        {
            return new WorkspaceSearchResult(
                request.RepositoryId,
                request.Query,
                sqliteVecBootstrapService.IsAvailable,
                sqliteVecBootstrapService.Status.Detail,
                []);
        }

        var tasksTask = store.ListTasksAsync(request.RepositoryId, cancellationToken);
        var runsTask = store.ListRunsByRepositoryAsync(request.RepositoryId, cancellationToken);
        var findingsTask = store.ListFindingsAsync(request.RepositoryId, cancellationToken);
        await Task.WhenAll(tasksTask, runsTask, findingsTask);

        var tasks = tasksTask.Result
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(MaxTaskCandidates)
            .ToList();
        var runs = runsTask.Result
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(MaxRunCandidates)
            .ToList();
        var findings = findingsTask.Result
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(MaxFindingCandidates)
            .ToList();

        var logsByRun = await LoadRunLogsAsync(runs, request.IncludeRunLogs, cancellationToken);
        var queryEmbedding = await workspaceAiService.CreateEmbeddingAsync(request.RepositoryId, request.Query, cancellationToken);

        var semanticHits = await SearchSemanticHitsAsync(
            request,
            tasks,
            queryEmbedding.Success ? queryEmbedding.Payload : null,
            cancellationToken);

        var candidates = BuildCandidates(tasks, runs, findings, logsByRun);
        var keywordHits = ScoreCandidates(candidates, request, sqliteVecBootstrapService.IsAvailable);
        var hits = MergeSearchHits(keywordHits, semanticHits, request.Limit);
        logger.LogDebug(
            "Workspace search query '{Query}' for repository {RepositoryId} produced {HitCount} hits",
            request.Query,
            request.RepositoryId,
            hits.Count);

        return new WorkspaceSearchResult(
            request.RepositoryId,
            request.Query,
            sqliteVecBootstrapService.IsAvailable,
            BuildSearchDetail(queryEmbedding),
            hits);
    }

    private string? BuildSearchDetail(WorkspaceEmbeddingResult queryEmbedding)
    {
        var sqliteDetail = sqliteVecBootstrapService.Status.Detail;
        if (!queryEmbedding.Success)
        {
            return sqliteDetail;
        }

        if (!queryEmbedding.UsedFallback)
        {
            return sqliteDetail;
        }

        if (string.IsNullOrWhiteSpace(sqliteDetail))
        {
            return queryEmbedding.Message;
        }

        if (string.IsNullOrWhiteSpace(queryEmbedding.Message))
        {
            return sqliteDetail;
        }

        return $"{sqliteDetail} | {queryEmbedding.Message}";
    }

    private async Task IndexSemanticChunksAsync(
        string repositoryId,
        IReadOnlyList<TaskDocument> tasks,
        IReadOnlyList<RunDocument> runs,
        IReadOnlyDictionary<string, IReadOnlyList<RunLogEvent>> logsByRun,
        CancellationToken cancellationToken)
    {
        if (tasks.Count == 0)
        {
            return;
        }

        var indexedTaskCount = 0;
        foreach (var task in tasks
                     .OrderByDescending(x => x.CreatedAtUtc)
                     .Take(12))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunks = new List<SemanticChunkDocument>();
            await TryAddTaskChunkAsync(repositoryId, task, chunks, cancellationToken);

            var recentTaskRuns = runs
                .Where(run => run.TaskId == task.Id)
                .OrderByDescending(run => run.CreatedAtUtc)
                .Take(2)
                .ToList();

            foreach (var run in recentTaskRuns)
            {
                logsByRun.TryGetValue(run.Id, out var runLogs);
                await TryAddRunChunkAsync(repositoryId, task.Id, run, runLogs ?? [], chunks, cancellationToken);
            }

            if (chunks.Count > 0)
            {
                await store.UpsertSemanticChunksAsync(task.Id, chunks, cancellationToken);
                indexedTaskCount++;
            }
        }

        if (indexedTaskCount > 0)
        {
            logger.LogDebug("Indexed semantic chunks for {TaskCount} tasks in repository {RepositoryId}", indexedTaskCount, repositoryId);
        }
    }

    private async Task TryAddTaskChunkAsync(
        string repositoryId,
        TaskDocument task,
        IList<SemanticChunkDocument> chunks,
        CancellationToken cancellationToken)
    {
        var content = new StringBuilder()
            .AppendLine(task.Name)
            .AppendLine($"Harness: {task.Harness}")
            .AppendLine($"Kind: {task.Kind}")
            .AppendLine(task.Prompt)
            .AppendLine(task.Command)
            .ToString();

        var chunkKey = $"task:{task.Id}";
        await TryAddChunkAsync(
            repositoryId,
            task.Id,
            runId: string.Empty,
            chunkKey,
            sourceType: "task",
            sourceRef: task.Id,
            chunkIndex: 0,
            content,
            chunks,
            cancellationToken);
    }

    private async Task TryAddRunChunkAsync(
        string repositoryId,
        string taskId,
        RunDocument run,
        IReadOnlyList<RunLogEvent> runLogs,
        IList<SemanticChunkDocument> chunks,
        CancellationToken cancellationToken)
    {
        var logChunk = string.Join(
            Environment.NewLine,
            runLogs
                .OrderBy(x => x.TimestampUtc)
                .TakeLast(16)
                .Select(x => $"[{x.TimestampUtc:O}] {x.Level}: {x.Message}"));

        var content = new StringBuilder()
            .AppendLine($"Run {run.Id}")
            .AppendLine($"State: {run.State}")
            .AppendLine($"Summary: {run.Summary}")
            .AppendLine("Recent logs:")
            .AppendLine(logChunk)
            .ToString();

        var chunkKey = $"run:{run.Id}:messages";
        await TryAddChunkAsync(
            repositoryId,
            taskId,
            run.Id,
            chunkKey,
            sourceType: "run-message",
            sourceRef: run.Id,
            chunkIndex: 0,
            content,
            chunks,
            cancellationToken);
    }

    private async Task TryAddChunkAsync(
        string repositoryId,
        string taskId,
        string runId,
        string chunkKey,
        string sourceType,
        string sourceRef,
        int chunkIndex,
        string content,
        IList<SemanticChunkDocument> chunks,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        var normalized = NormalizeWhitespace(content);
        var hash = ComputeContentHash(normalized);
        var cacheKey = $"{taskId}:{chunkKey}";

        if (_indexedChunkHashes.TryGetValue(cacheKey, out var existingHash) &&
            string.Equals(existingHash, hash, StringComparison.Ordinal))
        {
            return;
        }

        var embedding = await workspaceAiService.CreateEmbeddingAsync(repositoryId, normalized, cancellationToken);
        if (!embedding.Success || string.IsNullOrWhiteSpace(embedding.Payload))
        {
            return;
        }

        chunks.Add(new SemanticChunkDocument
        {
            RepositoryId = repositoryId,
            TaskId = taskId,
            RunId = runId,
            ChunkKey = chunkKey,
            SourceType = sourceType,
            SourceRef = sourceRef,
            ChunkIndex = chunkIndex,
            Content = normalized,
            ContentHash = hash,
            TokenCount = Tokenize(normalized).Count(),
            EmbeddingModel = embedding.Model,
            EmbeddingDimensions = embedding.Dimensions,
            EmbeddingPayload = embedding.Payload,
            UpdatedAtUtc = DateTime.UtcNow,
        });

        _indexedChunkHashes[cacheKey] = hash;
    }

    private async Task<IReadOnlyList<WorkspaceSearchHit>> SearchSemanticHitsAsync(
        WorkspaceSearchRequest request,
        IReadOnlyList<TaskDocument> tasks,
        string? queryEmbeddingPayload,
        CancellationToken cancellationToken)
    {
        if (tasks.Count == 0)
        {
            return [];
        }

        var perTaskLimit = Math.Clamp(Math.Max(2, request.Limit / 3), 2, 8);
        var hits = new List<WorkspaceSearchHit>();

        foreach (var task in tasks.Take(20))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var chunks = await store.SearchWorkspaceSemanticAsync(
                task.Id,
                request.Query,
                queryEmbeddingPayload,
                perTaskLimit,
                cancellationToken);

            for (var index = 0; index < chunks.Count; index++)
            {
                var chunk = chunks[index];
                var semanticScore = Math.Max(0.05, 1d - (index / (double)Math.Max(1, chunks.Count)));
                var keywordScore = CalculateKeywordScore(
                    NormalizeForSearch(chunk.Content),
                    NormalizeForSearch(request.Query),
                    Tokenize(NormalizeForSearch(request.Query)).ToHashSet(StringComparer.OrdinalIgnoreCase));

                var score = (semanticScore * 8.5) + (keywordScore * 0.35) + CalculateRecencyBonus(chunk.UpdatedAtUtc);
                hits.Add(new WorkspaceSearchHit(
                    Kind: chunk.SourceType,
                    Id: chunk.Id,
                    Title: BuildSemanticHitTitle(chunk),
                    Snippet: BuildSnippet(chunk.Content, Tokenize(NormalizeForSearch(request.Query)).ToHashSet(StringComparer.OrdinalIgnoreCase)),
                    Score: Math.Round(score, 3),
                    KeywordScore: Math.Round(keywordScore, 3),
                    SemanticScore: Math.Round(semanticScore, 3),
                    TimestampUtc: chunk.UpdatedAtUtc,
                    RunId: string.IsNullOrWhiteSpace(chunk.RunId) ? null : chunk.RunId,
                    TaskId: chunk.TaskId));
            }
        }

        return hits
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.SemanticScore)
            .ThenByDescending(x => x.TimestampUtc)
            .Take(Math.Clamp(request.Limit, 1, 100))
            .ToList();
    }

    private static string BuildSemanticHitTitle(SemanticChunkDocument chunk)
    {
        return chunk.SourceType switch
        {
            "task" => $"Task {chunk.SourceRef}",
            "run-message" => $"Run {chunk.SourceRef} messages",
            _ => $"{chunk.SourceType} {chunk.SourceRef}"
        };
    }

    private static List<WorkspaceSearchHit> MergeSearchHits(
        IReadOnlyList<WorkspaceSearchHit> keywordHits,
        IReadOnlyList<WorkspaceSearchHit> semanticHits,
        int requestedLimit)
    {
        var limit = requestedLimit <= 0 ? 25 : Math.Min(requestedLimit, 100);
        var merged = new Dictionary<string, WorkspaceSearchHit>(StringComparer.Ordinal);

        foreach (var hit in keywordHits.Concat(semanticHits))
        {
            var dedupeKey = $"{hit.Kind}:{hit.Id}";
            if (!merged.TryGetValue(dedupeKey, out var existing))
            {
                merged[dedupeKey] = hit;
                continue;
            }

            if (hit.Score > existing.Score)
            {
                merged[dedupeKey] = hit;
            }
        }

        return merged.Values
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.SemanticScore)
            .ThenByDescending(x => x.KeywordScore)
            .Take(limit)
            .ToList();
    }

    private static string ComputeContentHash(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    private async Task<Dictionary<string, IReadOnlyList<AgentsDashboard.Contracts.Domain.RunLogEvent>>> LoadRunLogsAsync(
        IReadOnlyList<AgentsDashboard.Contracts.Domain.RunDocument> runs,
        bool includeRunLogs,
        CancellationToken cancellationToken)
    {
        if (!includeRunLogs || runs.Count == 0)
        {
            return new Dictionary<string, IReadOnlyList<AgentsDashboard.Contracts.Domain.RunLogEvent>>(StringComparer.OrdinalIgnoreCase);
        }

        var selectedRuns = runs.Take(20).ToList();
        var logTasks = selectedRuns
            .Select(run => store.ListRunLogsAsync(run.Id, cancellationToken))
            .ToList();

        await Task.WhenAll(logTasks);

        var logsByRun = new Dictionary<string, IReadOnlyList<AgentsDashboard.Contracts.Domain.RunLogEvent>>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < selectedRuns.Count; index++)
        {
            logsByRun[selectedRuns[index].Id] = logTasks[index].Result;
        }

        return logsByRun;
    }

    private IReadOnlyList<SearchCandidate> BuildCandidates(
        IReadOnlyList<AgentsDashboard.Contracts.Domain.TaskDocument> tasks,
        IReadOnlyList<AgentsDashboard.Contracts.Domain.RunDocument> runs,
        IReadOnlyList<AgentsDashboard.Contracts.Domain.FindingDocument> findings,
        IReadOnlyDictionary<string, IReadOnlyList<AgentsDashboard.Contracts.Domain.RunLogEvent>> logsByRun)
    {
        var candidates = new List<SearchCandidate>(tasks.Count + runs.Count + findings.Count + 200);

        foreach (var task in tasks)
        {
            var body = new StringBuilder()
                .AppendLine(task.Name)
                .AppendLine(task.Prompt)
                .AppendLine(task.Command)
                .AppendLine($"Harness: {task.Harness}")
                .AppendLine($"Kind: {task.Kind}")
                .ToString();

            candidates.Add(new SearchCandidate(
                Kind: "task",
                Id: task.Id,
                Title: task.Name,
                Body: body,
                TimestampUtc: task.CreatedAtUtc,
                RunId: null,
                TaskId: task.Id));
        }

        foreach (var run in runs)
        {
            logsByRun.TryGetValue(run.Id, out var runLogs);
            var parsed = parserService.Parse(run.OutputJson, runLogs ?? []);

            var body = new StringBuilder()
                .AppendLine($"Run {run.Id}")
                .AppendLine($"State: {run.State}")
                .AppendLine($"Summary: {run.Summary}")
                .AppendLine($"Parsed summary: {parsed.Summary}")
                .AppendLine($"Parsed error: {parsed.Error}")
                .AppendLine($"Failure class: {run.FailureClass}")
                .AppendLine(parsed.NormalizedOutputJson)
                .ToString();

            candidates.Add(new SearchCandidate(
                Kind: "run",
                Id: run.Id,
                Title: $"Run {run.Id[..Math.Min(8, run.Id.Length)]} ({run.State})",
                Body: body,
                TimestampUtc: run.EndedAtUtc ?? run.StartedAtUtc ?? run.CreatedAtUtc,
                RunId: run.Id,
                TaskId: run.TaskId));

            if (runLogs is null || runLogs.Count == 0)
            {
                continue;
            }

            foreach (var log in runLogs.TakeLast(40))
            {
                if (string.IsNullOrWhiteSpace(log.Message))
                {
                    continue;
                }

                candidates.Add(new SearchCandidate(
                    Kind: "run-log",
                    Id: log.Id,
                    Title: $"Run log {run.Id[..Math.Min(8, run.Id.Length)]}",
                    Body: $"{log.Level} {log.Message}",
                    TimestampUtc: log.TimestampUtc,
                    RunId: run.Id,
                    TaskId: run.TaskId));
            }
        }

        foreach (var finding in findings)
        {
            var body = new StringBuilder()
                .AppendLine(finding.Title)
                .AppendLine(finding.Description)
                .AppendLine($"State: {finding.State}")
                .AppendLine($"Severity: {finding.Severity}")
                .AppendLine($"Assigned: {finding.AssignedTo}")
                .ToString();

            candidates.Add(new SearchCandidate(
                Kind: "finding",
                Id: finding.Id,
                Title: finding.Title,
                Body: body,
                TimestampUtc: finding.CreatedAtUtc,
                RunId: finding.RunId,
                TaskId: null));
        }

        return candidates;
    }

    private IReadOnlyList<WorkspaceSearchHit> ScoreCandidates(
        IReadOnlyList<SearchCandidate> candidates,
        WorkspaceSearchRequest request,
        bool sqliteVecAvailable)
    {
        var normalizedQuery = NormalizeForSearch(request.Query);
        var queryTokens = Tokenize(normalizedQuery).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var semanticTokens = ExpandSemanticTokens(queryTokens);

        if (queryTokens.Count == 0)
        {
            return [];
        }

        var results = new List<ScoredCandidate>(candidates.Count);

        foreach (var candidate in candidates)
        {
            var normalizedBody = NormalizeForSearch(candidate.Body);
            if (normalizedBody.Length == 0)
            {
                continue;
            }

            var bodyTokens = Tokenize(normalizedBody).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var keywordScore = CalculateKeywordScore(normalizedBody, normalizedQuery, queryTokens);
            var semanticScore = CalculateSemanticScore(bodyTokens, semanticTokens, queryTokens);

            if (keywordScore <= 0 && semanticScore <= 0)
            {
                continue;
            }

            var recencyBonus = CalculateRecencyBonus(candidate.TimestampUtc);
            var semanticWeight = sqliteVecAvailable ? 3.6 : 3.0;
            var score = (keywordScore * 0.72) + (semanticScore * semanticWeight) + recencyBonus;

            if (score <= 0)
            {
                continue;
            }

            results.Add(new ScoredCandidate(candidate, score, keywordScore, semanticScore));
        }

        var limit = request.Limit <= 0 ? 25 : Math.Min(request.Limit, 100);

        return results
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.KeywordScore)
            .ThenByDescending(x => x.SemanticScore)
            .Take(limit)
            .Select(x => new WorkspaceSearchHit(
                x.Candidate.Kind,
                x.Candidate.Id,
                x.Candidate.Title,
                BuildSnippet(x.Candidate.Body, queryTokens),
                Math.Round(x.Score, 3),
                Math.Round(x.KeywordScore, 3),
                Math.Round(x.SemanticScore, 3),
                x.Candidate.TimestampUtc,
                x.Candidate.RunId,
                x.Candidate.TaskId))
            .ToList();
    }

    private static double CalculateKeywordScore(
        string normalizedBody,
        string normalizedQuery,
        IReadOnlyCollection<string> queryTokens)
    {
        var score = 0.0;

        if (normalizedBody.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase))
        {
            score += 8.0;
        }

        foreach (var token in queryTokens)
        {
            var searchIndex = 0;
            while (searchIndex < normalizedBody.Length)
            {
                var foundIndex = normalizedBody.IndexOf(token, searchIndex, StringComparison.OrdinalIgnoreCase);
                if (foundIndex < 0)
                {
                    break;
                }

                score += 1.4;
                searchIndex = foundIndex + token.Length;

                if (score >= 30)
                {
                    return score;
                }
            }
        }

        return score;
    }

    private static double CalculateSemanticScore(
        IReadOnlySet<string> bodyTokens,
        IReadOnlySet<string> semanticTokens,
        IReadOnlySet<string> queryTokens)
    {
        if (bodyTokens.Count == 0 || semanticTokens.Count == 0)
        {
            return 0;
        }

        var overlap = semanticTokens.Count(bodyTokens.Contains);
        if (overlap == 0)
        {
            return 0;
        }

        var queryCoverage = queryTokens.Count(bodyTokens.Contains);
        var overlapScore = (double)overlap / semanticTokens.Count;
        var coverageScore = queryTokens.Count == 0
            ? 0
            : (double)queryCoverage / queryTokens.Count;

        return (overlapScore * 0.65) + (coverageScore * 0.35);
    }

    private static double CalculateRecencyBonus(DateTime? timestampUtc)
    {
        if (timestampUtc is null)
        {
            return 0;
        }

        var age = DateTime.UtcNow - timestampUtc.Value;
        if (age.TotalDays <= 0)
        {
            return 0.45;
        }

        var decay = Math.Exp(-age.TotalDays / 30d);
        return 0.45 * decay;
    }

    private static string BuildSnippet(string body, IReadOnlySet<string> queryTokens)
    {
        var normalizedBody = NormalizeWhitespace(body);
        if (normalizedBody.Length == 0)
        {
            return string.Empty;
        }

        foreach (var token in queryTokens)
        {
            var index = normalizedBody.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                continue;
            }

            var start = Math.Max(0, index - 70);
            var length = Math.Min(240, normalizedBody.Length - start);
            return normalizedBody.Substring(start, length).Trim();
        }

        return normalizedBody.Length <= 240
            ? normalizedBody
            : normalizedBody[..240].Trim();
    }

    private static string NormalizeForSearch(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        return NormalizeWhitespace(text).ToLowerInvariant();
    }

    private static string NormalizeWhitespace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var previousWhitespace = false;

        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!previousWhitespace)
                {
                    builder.Append(' ');
                    previousWhitespace = true;
                }

                continue;
            }

            builder.Append(ch);
            previousWhitespace = false;
        }

        return builder.ToString().Trim();
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            yield break;
        }

        foreach (Match match in s_tokenRegex.Matches(text))
        {
            var token = match.Value.ToLowerInvariant();
            if (token.Length < 2)
            {
                continue;
            }

            yield return Stem(token);
        }
    }

    private static HashSet<string> ExpandSemanticTokens(IEnumerable<string> queryTokens)
    {
        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var token in queryTokens)
        {
            if (!expanded.Add(token))
            {
                continue;
            }

            if (s_semanticAliases.TryGetValue(token, out var aliases))
            {
                foreach (var alias in aliases)
                {
                    expanded.Add(alias);
                }
            }
        }

        return expanded;
    }

    private static string Stem(string token)
    {
        if (token.Length > 5 && token.EndsWith("ing", StringComparison.OrdinalIgnoreCase))
        {
            return token[..^3];
        }

        if (token.Length > 4 && token.EndsWith("ed", StringComparison.OrdinalIgnoreCase))
        {
            return token[..^2];
        }

        if (token.Length > 3 && token.EndsWith("es", StringComparison.OrdinalIgnoreCase))
        {
            return token[..^2];
        }

        if (token.Length > 3 && token.EndsWith('s'))
        {
            return token[..^1];
        }

        return token;
    }

    private sealed record SearchCandidate(
        string Kind,
        string Id,
        string Title,
        string Body,
        DateTime? TimestampUtc,
        string? RunId,
        string? TaskId);

    private sealed record ScoredCandidate(
        SearchCandidate Candidate,
        double Score,
        double KeywordScore,
        double SemanticScore);
}
