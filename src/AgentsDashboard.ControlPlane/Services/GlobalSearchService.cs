using System.Text;
using System.Text.RegularExpressions;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;

namespace AgentsDashboard.ControlPlane.Services;

public interface IGlobalSearchService
{
    Task<GlobalSearchResult> SearchAsync(GlobalSearchRequest request, CancellationToken cancellationToken);
}

public sealed class GlobalSearchService(
    IOrchestratorStore store,
    IWorkspaceAiService workspaceAiService,
    IHarnessOutputParserService parserService,
    ISqliteVecBootstrapService sqliteVecBootstrapService,
    ILogger<GlobalSearchService> logger) : IGlobalSearchService
{
    private static readonly Regex s_tokenRegex = new("[a-z0-9_]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

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
            ["timeout"] = ["slow", "deadline", "cancelled"],
            ["approval"] = ["review", "gate", "pending", "manual"],
            ["finding"] = ["issue", "risk", "alert", "incident"]
        };

    private static readonly HashSet<GlobalSearchKind> s_allKinds =
    [
        GlobalSearchKind.Task,
        GlobalSearchKind.Run,
        GlobalSearchKind.Finding,
        GlobalSearchKind.RunLog
    ];

    private const int MaxTaskCandidates = 320;
    private const int MaxRunCandidates = 900;
    private const int MaxFindingCandidates = 500;
    private const int MaxRunLogRuns = 90;
    private const int MaxRunLogsPerRun = 64;
    private const int MaxSemanticTaskCandidates = 90;
    private const int MaxRunOutputLength = 2200;
    private const int MaxTaskPromptLength = 1800;
    private const int MaxFindingDescriptionLength = 1400;

    public async Task<GlobalSearchResult> SearchAsync(GlobalSearchRequest request, CancellationToken cancellationToken)
    {
        var normalizedQuery = request.Query?.Trim() ?? string.Empty;
        if (normalizedQuery.Length == 0)
        {
            return BuildEmptyResult(normalizedQuery);
        }

        var normalizedLimit = NormalizeLimit(request.Limit);
        var repositories = await store.ListRepositoriesAsync(cancellationToken);
        var scopedRepositories = repositories
            .Where(repo =>
                string.IsNullOrWhiteSpace(request.RepositoryId) ||
                string.Equals(repo.Id, request.RepositoryId, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (scopedRepositories.Count == 0)
        {
            return BuildEmptyResult(normalizedQuery);
        }

        var repositoryById = repositories.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var scopedRepositoryIds = scopedRepositories.Select(repo => repo.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var kinds = NormalizeKinds(request.Kinds);

        var tasksByRepositoryTasks = scopedRepositories
            .Select(repository => store.ListTasksAsync(repository.Id, cancellationToken))
            .ToList();
        var findingsByRepositoryTasks = scopedRepositories
            .Select(repository => store.ListFindingsAsync(repository.Id, cancellationToken))
            .ToList();
        await Task.WhenAll(tasksByRepositoryTasks.Cast<Task>().Concat(findingsByRepositoryTasks));

        var tasks = tasksByRepositoryTasks
            .SelectMany(task => task.Result)
            .Where(task => scopedRepositoryIds.Contains(task.RepositoryId))
            .ToList();
        var findings = findingsByRepositoryTasks
            .SelectMany(finding => finding.Result)
            .Where(finding => scopedRepositoryIds.Contains(finding.RepositoryId))
            .ToList();

        if (!string.IsNullOrWhiteSpace(request.TaskId) &&
            tasks.All(task => !string.Equals(task.Id, request.TaskId, StringComparison.OrdinalIgnoreCase)))
        {
            return BuildEmptyResult(normalizedQuery);
        }

        var runs = await LoadRunsAsync(request, scopedRepositories, cancellationToken);
        runs = runs
            .Where(run => scopedRepositoryIds.Contains(run.RepositoryId))
            .ToList();

        if (!string.IsNullOrWhiteSpace(request.TaskId))
        {
            tasks = tasks
                .Where(task => string.Equals(task.Id, request.TaskId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            runs = runs
                .Where(run => string.Equals(run.TaskId, request.TaskId, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        tasks = tasks
            .Where(task => MatchesTimeFilter(task.CreatedAtUtc, request.FromUtc, request.ToUtc))
            .OrderByDescending(task => task.CreatedAtUtc)
            .Take(MaxTaskCandidates)
            .ToList();

        runs = runs
            .Where(run => request.RunStateFilter is null || run.State == request.RunStateFilter)
            .Where(run => MatchesTimeFilter(GetRunTimestamp(run), request.FromUtc, request.ToUtc))
            .OrderByDescending(GetRunTimestamp)
            .Take(MaxRunCandidates)
            .ToList();

        var taskById = tasks.ToDictionary(task => task.Id, StringComparer.OrdinalIgnoreCase);
        var runById = runs.ToDictionary(run => run.Id, StringComparer.OrdinalIgnoreCase);

        findings = findings
            .Where(finding => request.FindingStateFilter is null || finding.State == request.FindingStateFilter)
            .Where(finding => MatchesTimeFilter(finding.CreatedAtUtc, request.FromUtc, request.ToUtc))
            .Where(finding =>
            {
                if (string.IsNullOrWhiteSpace(request.TaskId))
                {
                    return true;
                }

                return runById.TryGetValue(finding.RunId, out var findingRun) &&
                       string.Equals(findingRun.TaskId, request.TaskId, StringComparison.OrdinalIgnoreCase);
            })
            .OrderByDescending(finding => finding.CreatedAtUtc)
            .Take(MaxFindingCandidates)
            .ToList();

        var logsByRun = await LoadRunLogsAsync(runs, request, kinds, cancellationToken);

        var embeddingRepositoryId = ResolveEmbeddingRepositoryId(request.RepositoryId, scopedRepositories, tasks, runs, findings);
        var queryEmbedding = await workspaceAiService.CreateEmbeddingAsync(embeddingRepositoryId, normalizedQuery, cancellationToken);

        var semanticTasks = tasks
            .OrderByDescending(task => task.CreatedAtUtc)
            .Take(MaxSemanticTaskCandidates)
            .ToList();
        var semanticSignals = await SearchSemanticSignalsAsync(
            semanticTasks,
            normalizedQuery,
            queryEmbedding.Success ? queryEmbedding.Payload : null,
            normalizedLimit,
            cancellationToken);

        var candidates = BuildCandidates(
            kinds,
            repositoryById,
            taskById,
            runById,
            tasks,
            runs,
            findings,
            logsByRun);

        var scoredHits = ScoreCandidates(
            candidates,
            normalizedQuery,
            semanticSignals,
            sqliteVecBootstrapService.IsAvailable);

        var orderedHits = scoredHits
            .OrderByDescending(hit => hit.Score)
            .ThenByDescending(hit => hit.SemanticScore)
            .ThenByDescending(hit => hit.KeywordScore)
            .ThenByDescending(hit => hit.TimestampUtc)
            .ToList();

        var countsByKind = orderedHits
            .GroupBy(hit => hit.Kind)
            .OrderBy(group => group.Key)
            .Select(group => new GlobalSearchKindCount(group.Key, group.Count()))
            .ToList();

        var hits = orderedHits
            .Take(normalizedLimit)
            .ToList();

        logger.LogDebug(
            "Global search '{Query}' produced {HitCount} hits ({TotalMatches} total matches)",
            normalizedQuery,
            hits.Count,
            orderedHits.Count);

        return new GlobalSearchResult(
            Query: normalizedQuery,
            SqliteVecAvailable: sqliteVecBootstrapService.IsAvailable,
            SqliteVecDetail: BuildSearchDetail(queryEmbedding),
            TotalMatches: orderedHits.Count,
            CountsByKind: countsByKind,
            Hits: hits);
    }

    private static int NormalizeLimit(int limit)
    {
        if (limit <= 0)
        {
            return 50;
        }

        return Math.Clamp(limit, 1, 200);
    }

    private static HashSet<GlobalSearchKind> NormalizeKinds(IReadOnlyList<GlobalSearchKind>? kinds)
    {
        if (kinds is null || kinds.Count == 0)
        {
            return [.. s_allKinds];
        }

        var normalized = new HashSet<GlobalSearchKind>(kinds.Where(Enum.IsDefined));
        return normalized.Count == 0
            ? [.. s_allKinds]
            : normalized;
    }

    private async Task<List<RunDocument>> LoadRunsAsync(
        GlobalSearchRequest request,
        IReadOnlyList<RepositoryDocument> scopedRepositories,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.TaskId))
        {
            return await store.ListRunsByTaskAsync(request.TaskId, 500, cancellationToken);
        }

        var runTasks = scopedRepositories
            .Select(repository => store.ListRunsByRepositoryAsync(repository.Id, cancellationToken))
            .ToList();
        await Task.WhenAll(runTasks);

        return runTasks
            .SelectMany(run => run.Result)
            .ToList();
    }

    private async Task<Dictionary<string, IReadOnlyList<RunLogEvent>>> LoadRunLogsAsync(
        IReadOnlyList<RunDocument> runs,
        GlobalSearchRequest request,
        IReadOnlySet<GlobalSearchKind> kinds,
        CancellationToken cancellationToken)
    {
        if (!request.IncludeRunLogs || !kinds.Contains(GlobalSearchKind.RunLog) || runs.Count == 0)
        {
            return new Dictionary<string, IReadOnlyList<RunLogEvent>>(StringComparer.OrdinalIgnoreCase);
        }

        var selectedRuns = runs
            .OrderByDescending(GetRunTimestamp)
            .Take(MaxRunLogRuns)
            .ToList();
        var logTasks = selectedRuns
            .Select(run => store.ListRunLogsAsync(run.Id, cancellationToken))
            .ToList();
        await Task.WhenAll(logTasks);

        var logsByRun = new Dictionary<string, IReadOnlyList<RunLogEvent>>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < selectedRuns.Count; index++)
        {
            var logs = logTasks[index].Result
                .Where(log => !string.IsNullOrWhiteSpace(log.Message))
                .Where(log => MatchesTimeFilter(log.TimestampUtc, request.FromUtc, request.ToUtc))
                .TakeLast(MaxRunLogsPerRun)
                .ToList();

            logsByRun[selectedRuns[index].Id] = logs;
        }

        return logsByRun;
    }

    private async Task<Dictionary<string, double>> SearchSemanticSignalsAsync(
        IReadOnlyList<TaskDocument> tasks,
        string queryText,
        string? queryEmbeddingPayload,
        int requestedLimit,
        CancellationToken cancellationToken)
    {
        var signals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (tasks.Count == 0)
        {
            return signals;
        }

        var perTaskLimit = Math.Clamp(Math.Max(2, requestedLimit / 4), 2, 8);
        foreach (var task in tasks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var chunks = await store.SearchWorkspaceSemanticAsync(
                    task.Id,
                    queryText,
                    queryEmbeddingPayload,
                    perTaskLimit,
                    cancellationToken);

                for (var index = 0; index < chunks.Count; index++)
                {
                    var chunk = chunks[index];
                    var rankScore = Math.Max(0.05, 1d - (index / (double)Math.Max(1, chunks.Count)));

                    if (!string.IsNullOrWhiteSpace(chunk.TaskId))
                    {
                        AddSignal(signals, BuildTaskSignalKey(chunk.TaskId), rankScore);
                    }

                    if (!string.IsNullOrWhiteSpace(chunk.RunId))
                    {
                        AddSignal(signals, BuildRunSignalKey(chunk.RunId), rankScore);
                        if (!string.IsNullOrWhiteSpace(chunk.TaskId))
                        {
                            AddSignal(signals, BuildTaskSignalKey(chunk.TaskId), rankScore * 0.6);
                        }

                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(chunk.SourceRef) &&
                        chunk.SourceType.Contains("run", StringComparison.OrdinalIgnoreCase))
                    {
                        AddSignal(signals, BuildRunSignalKey(chunk.SourceRef), rankScore);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Semantic chunk lookup failed for task {TaskId}", task.Id);
            }
        }

        return signals;
    }

    private static void AddSignal(IDictionary<string, double> signals, string key, double score)
    {
        if (string.IsNullOrWhiteSpace(key) || score <= 0)
        {
            return;
        }

        if (!signals.TryGetValue(key, out var existing) || score > existing)
        {
            signals[key] = score;
        }
    }

    private static string BuildTaskSignalKey(string taskId) => $"task:{taskId}";

    private static string BuildRunSignalKey(string runId) => $"run:{runId}";

    private List<SearchCandidate> BuildCandidates(
        IReadOnlySet<GlobalSearchKind> kinds,
        IReadOnlyDictionary<string, RepositoryDocument> repositoryById,
        IReadOnlyDictionary<string, TaskDocument> taskById,
        IReadOnlyDictionary<string, RunDocument> runById,
        IReadOnlyList<TaskDocument> tasks,
        IReadOnlyList<RunDocument> runs,
        IReadOnlyList<FindingDocument> findings,
        IReadOnlyDictionary<string, IReadOnlyList<RunLogEvent>> logsByRun)
    {
        var candidates = new List<SearchCandidate>(tasks.Count + runs.Count + findings.Count + 200);

        if (kinds.Contains(GlobalSearchKind.Task))
        {
            foreach (var task in tasks)
            {
                repositoryById.TryGetValue(task.RepositoryId, out var repository);
                var repositoryName = repository?.Name ?? task.RepositoryId;
                var body = new StringBuilder()
                    .AppendLine($"Repository: {repositoryName}")
                    .AppendLine($"Task: {task.Name}")
                    .AppendLine($"Harness: {task.Harness}")
                    .AppendLine($"Kind: {task.Kind}")
                    .AppendLine($"Enabled: {task.Enabled}")
                    .AppendLine("Prompt:")
                    .AppendLine(TruncateForIndex(task.Prompt, MaxTaskPromptLength))
                    .AppendLine("Command:")
                    .AppendLine(task.Command)
                    .ToString();

                candidates.Add(new SearchCandidate(
                    Kind: GlobalSearchKind.Task,
                    Id: task.Id,
                    RepositoryId: task.RepositoryId,
                    RepositoryName: repositoryName,
                    TaskId: task.Id,
                    TaskName: task.Name,
                    RunId: null,
                    Title: task.Name,
                    Body: body,
                    State: task.Enabled ? "Enabled" : "Disabled",
                    TimestampUtc: task.CreatedAtUtc,
                    SemanticKey: BuildTaskSignalKey(task.Id)));
            }
        }

        if (kinds.Contains(GlobalSearchKind.Run) || kinds.Contains(GlobalSearchKind.RunLog))
        {
            foreach (var run in runs)
            {
                repositoryById.TryGetValue(run.RepositoryId, out var repository);
                var repositoryName = repository?.Name ?? run.RepositoryId;
                taskById.TryGetValue(run.TaskId, out var task);
                var taskName = task?.Name;

                if (kinds.Contains(GlobalSearchKind.Run))
                {
                    var parsed = parserService.Parse(run.OutputJson);
                    var body = new StringBuilder()
                        .AppendLine($"Repository: {repositoryName}")
                        .AppendLine($"Task: {taskName ?? run.TaskId}")
                        .AppendLine($"Run: {run.Id}")
                        .AppendLine($"State: {run.State}")
                        .AppendLine($"Summary: {run.Summary}")
                        .AppendLine($"Parsed summary: {parsed.Summary}")
                        .AppendLine($"Parsed error: {parsed.Error}")
                        .AppendLine($"Failure class: {run.FailureClass}")
                        .AppendLine(TruncateForIndex(parsed.NormalizedOutputJson, MaxRunOutputLength))
                        .ToString();

                    var shortRunId = run.Id[..Math.Min(8, run.Id.Length)];
                    var title = string.IsNullOrWhiteSpace(taskName)
                        ? $"Run {shortRunId} ({run.State})"
                        : $"Run {shortRunId} ({run.State}) - {taskName}";

                    candidates.Add(new SearchCandidate(
                        Kind: GlobalSearchKind.Run,
                        Id: run.Id,
                        RepositoryId: run.RepositoryId,
                        RepositoryName: repositoryName,
                        TaskId: run.TaskId,
                        TaskName: taskName,
                        RunId: run.Id,
                        Title: title,
                        Body: body,
                        State: run.State.ToString(),
                        TimestampUtc: GetRunTimestamp(run),
                        SemanticKey: BuildRunSignalKey(run.Id)));
                }

                if (kinds.Contains(GlobalSearchKind.RunLog) &&
                    logsByRun.TryGetValue(run.Id, out var logs) &&
                    logs.Count > 0)
                {
                    foreach (var log in logs)
                    {
                        var body = new StringBuilder()
                            .AppendLine($"Repository: {repositoryName}")
                            .AppendLine($"Task: {taskName ?? run.TaskId}")
                            .AppendLine($"Run: {run.Id}")
                            .AppendLine($"State: {run.State}")
                            .AppendLine($"Level: {log.Level}")
                            .AppendLine(log.Message)
                            .ToString();

                        var shortRunId = run.Id[..Math.Min(8, run.Id.Length)];
                        var title = $"Run log {shortRunId} [{log.Level}]";

                        candidates.Add(new SearchCandidate(
                            Kind: GlobalSearchKind.RunLog,
                            Id: log.Id,
                            RepositoryId: run.RepositoryId,
                            RepositoryName: repositoryName,
                            TaskId: run.TaskId,
                            TaskName: taskName,
                            RunId: run.Id,
                            Title: title,
                            Body: body,
                            State: run.State.ToString(),
                            TimestampUtc: log.TimestampUtc,
                            SemanticKey: BuildRunSignalKey(run.Id)));
                    }
                }
            }
        }

        if (kinds.Contains(GlobalSearchKind.Finding))
        {
            foreach (var finding in findings)
            {
                repositoryById.TryGetValue(finding.RepositoryId, out var repository);
                var repositoryName = repository?.Name ?? finding.RepositoryId;

                string? taskId = null;
                string? taskName = null;
                if (!string.IsNullOrWhiteSpace(finding.RunId) &&
                    runById.TryGetValue(finding.RunId, out var run))
                {
                    taskId = run.TaskId;
                    if (!string.IsNullOrWhiteSpace(taskId) && taskById.TryGetValue(taskId, out var task))
                    {
                        taskName = task.Name;
                    }
                }

                var body = new StringBuilder()
                    .AppendLine($"Repository: {repositoryName}")
                    .AppendLine($"Run: {finding.RunId}")
                    .AppendLine($"Severity: {finding.Severity}")
                    .AppendLine($"State: {finding.State}")
                    .AppendLine($"Assigned: {finding.AssignedTo}")
                    .AppendLine(finding.Title)
                    .AppendLine(TruncateForIndex(finding.Description, MaxFindingDescriptionLength))
                    .ToString();

                candidates.Add(new SearchCandidate(
                    Kind: GlobalSearchKind.Finding,
                    Id: finding.Id,
                    RepositoryId: finding.RepositoryId,
                    RepositoryName: repositoryName,
                    TaskId: taskId,
                    TaskName: taskName,
                    RunId: finding.RunId,
                    Title: finding.Title,
                    Body: body,
                    State: finding.State.ToString(),
                    TimestampUtc: finding.CreatedAtUtc,
                    SemanticKey: string.IsNullOrWhiteSpace(finding.RunId) ? null : BuildRunSignalKey(finding.RunId)));
            }
        }

        return candidates;
    }

    private static IReadOnlyList<GlobalSearchHit> ScoreCandidates(
        IReadOnlyList<SearchCandidate> candidates,
        string queryText,
        IReadOnlyDictionary<string, double> semanticSignals,
        bool sqliteVecAvailable)
    {
        var normalizedQuery = NormalizeForSearch(queryText);
        var queryTokens = Tokenize(normalizedQuery).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (queryTokens.Count == 0 && normalizedQuery.Length > 0)
        {
            queryTokens.Add(normalizedQuery);
        }

        var semanticTokens = ExpandSemanticTokens(queryTokens);
        var scored = new List<GlobalSearchHit>(candidates.Count);

        foreach (var candidate in candidates)
        {
            var normalizedBody = NormalizeForSearch(candidate.Body);
            if (normalizedBody.Length == 0)
            {
                continue;
            }

            var keywordScore = CalculateKeywordScore(normalizedBody, normalizedQuery, queryTokens);
            var normalizedTitle = NormalizeForSearch(candidate.Title);
            if (normalizedTitle.Length > 0)
            {
                keywordScore += CalculateKeywordScore(normalizedTitle, normalizedQuery, queryTokens) * 1.25;
            }

            var bodyTokens = Tokenize(normalizedBody).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var lexicalSemanticScore = CalculateSemanticScore(bodyTokens, semanticTokens, queryTokens);
            var vectorSemanticScore = 0d;
            if (!string.IsNullOrWhiteSpace(candidate.SemanticKey) &&
                semanticSignals.TryGetValue(candidate.SemanticKey, out var signalScore))
            {
                vectorSemanticScore = signalScore;
            }

            var semanticScore = Math.Max(lexicalSemanticScore, vectorSemanticScore);
            if (keywordScore <= 0 && semanticScore <= 0)
            {
                continue;
            }

            var recencyBonus = CalculateRecencyBonus(candidate.TimestampUtc);
            var vectorWeight = sqliteVecAvailable ? 5.6 : 3.6;
            var score = (keywordScore * 0.74) + (lexicalSemanticScore * 2.2) + (vectorSemanticScore * vectorWeight) + recencyBonus;

            if (score <= 0)
            {
                continue;
            }

            scored.Add(new GlobalSearchHit(
                Kind: candidate.Kind,
                Id: candidate.Id,
                RepositoryId: candidate.RepositoryId,
                RepositoryName: candidate.RepositoryName,
                TaskId: candidate.TaskId,
                TaskName: candidate.TaskName,
                RunId: candidate.RunId,
                Title: candidate.Title,
                Snippet: BuildSnippet(candidate.Body, queryTokens, normalizedQuery),
                State: candidate.State,
                TimestampUtc: candidate.TimestampUtc,
                Score: Math.Round(score, 3),
                KeywordScore: Math.Round(keywordScore, 3),
                SemanticScore: Math.Round(semanticScore, 3)));
        }

        return scored;
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

    private GlobalSearchResult BuildEmptyResult(string query)
    {
        return new GlobalSearchResult(
            Query: query,
            SqliteVecAvailable: sqliteVecBootstrapService.IsAvailable,
            SqliteVecDetail: sqliteVecBootstrapService.Status.Detail,
            TotalMatches: 0,
            CountsByKind: [],
            Hits: []);
    }

    private static string ResolveEmbeddingRepositoryId(
        string? requestedRepositoryId,
        IReadOnlyList<RepositoryDocument> scopedRepositories,
        IReadOnlyList<TaskDocument> tasks,
        IReadOnlyList<RunDocument> runs,
        IReadOnlyList<FindingDocument> findings)
    {
        if (!string.IsNullOrWhiteSpace(requestedRepositoryId))
        {
            return requestedRepositoryId;
        }

        if (scopedRepositories.Count > 0)
        {
            return scopedRepositories[0].Id;
        }

        if (tasks.Count > 0)
        {
            return tasks[0].RepositoryId;
        }

        if (runs.Count > 0)
        {
            return runs[0].RepositoryId;
        }

        if (findings.Count > 0)
        {
            return findings[0].RepositoryId;
        }

        return string.Empty;
    }

    private static DateTime GetRunTimestamp(RunDocument run)
    {
        return run.EndedAtUtc ?? run.StartedAtUtc ?? run.CreatedAtUtc;
    }

    private static bool MatchesTimeFilter(DateTime timestampUtc, DateTime? fromUtc, DateTime? toUtc)
    {
        if (fromUtc.HasValue && timestampUtc < fromUtc.Value)
        {
            return false;
        }

        if (toUtc.HasValue && timestampUtc > toUtc.Value)
        {
            return false;
        }

        return true;
    }

    private static string TruncateForIndex(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = NormalizeWhitespace(value);
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..maxLength];
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

                score += 1.45;
                searchIndex = foundIndex + token.Length;

                if (score >= 32)
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

    private static double CalculateRecencyBonus(DateTime timestampUtc)
    {
        var age = DateTime.UtcNow - timestampUtc;
        if (age.TotalDays <= 0)
        {
            return 0.45;
        }

        var decay = Math.Exp(-age.TotalDays / 30d);
        return 0.45 * decay;
    }

    private static string BuildSnippet(
        string body,
        IReadOnlySet<string> queryTokens,
        string normalizedQuery)
    {
        var normalizedBody = NormalizeWhitespace(body);
        if (normalizedBody.Length == 0)
        {
            return string.Empty;
        }

        var tokens = queryTokens
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .OrderByDescending(token => token.Length)
            .ToList();

        if (tokens.Count == 0 && normalizedQuery.Length > 0)
        {
            tokens.Add(normalizedQuery);
        }

        foreach (var token in tokens)
        {
            var index = normalizedBody.IndexOf(token, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                continue;
            }

            var start = Math.Max(0, index - 85);
            var length = Math.Min(280, normalizedBody.Length - start);
            return normalizedBody.Substring(start, length).Trim();
        }

        return normalizedBody.Length <= 280
            ? normalizedBody
            : normalizedBody[..280].Trim();
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
        GlobalSearchKind Kind,
        string Id,
        string RepositoryId,
        string RepositoryName,
        string? TaskId,
        string? TaskName,
        string? RunId,
        string Title,
        string Body,
        string? State,
        DateTime TimestampUtc,
        string? SemanticKey);
}
