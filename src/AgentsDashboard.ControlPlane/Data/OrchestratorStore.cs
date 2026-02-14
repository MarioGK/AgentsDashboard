using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Configuration;
using Cronos;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace AgentsDashboard.ControlPlane.Data;

public class OrchestratorStore : IOrchestratorStore
{
    private readonly IMongoCollection<ProjectDocument> _projects;
    private readonly IMongoCollection<RepositoryDocument> _repositories;
    private readonly IMongoCollection<TaskDocument> _tasks;
    private readonly IMongoCollection<RunDocument> _runs;
    private readonly IMongoCollection<FindingDocument> _findings;
    private readonly IMongoCollection<RunLogEvent> _runEvents;
    private readonly IMongoCollection<ProviderSecretDocument> _providerSecrets;
    private readonly IMongoCollection<WorkerRegistration> _workers;
    private readonly IMongoCollection<WebhookRegistration> _webhooks;
    private readonly IMongoCollection<ProxyAuditDocument> _proxyAudits;
    private readonly IMongoCollection<SystemSettingsDocument> _settings;
    private readonly IMongoCollection<WorkflowDocument> _workflows;
    private readonly IMongoCollection<WorkflowExecutionDocument> _workflowExecutions;
    private readonly IMongoCollection<AlertRuleDocument> _alertRules;
    private readonly IMongoCollection<AlertEventDocument> _alertEvents;
    private readonly IMongoCollection<RepositoryInstructionDocument> _repositoryInstructions;
    private readonly IMongoCollection<HarnessProviderSettingsDocument> _harnessProviderSettings;
    private readonly OrchestratorOptions _options;

    public OrchestratorStore(IMongoClient mongoClient, IOptions<OrchestratorOptions> options)
    {
        _options = options.Value;
        var database = mongoClient.GetDatabase(_options.MongoDatabase);
        _projects = database.GetCollection<ProjectDocument>("projects");
        _repositories = database.GetCollection<RepositoryDocument>("repositories");
        _tasks = database.GetCollection<TaskDocument>("tasks");
        _runs = database.GetCollection<RunDocument>("runs");
        _findings = database.GetCollection<FindingDocument>("findings");
        _runEvents = database.GetCollection<RunLogEvent>("run_events");
        _providerSecrets = database.GetCollection<ProviderSecretDocument>("provider_secrets");
        _workers = database.GetCollection<WorkerRegistration>("workers");
        _webhooks = database.GetCollection<WebhookRegistration>("webhooks");
        _proxyAudits = database.GetCollection<ProxyAuditDocument>("proxy_audits");
        _settings = database.GetCollection<SystemSettingsDocument>("settings");
        _workflows = database.GetCollection<WorkflowDocument>("workflows");
        _workflowExecutions = database.GetCollection<WorkflowExecutionDocument>("workflow_executions");
        _alertRules = database.GetCollection<AlertRuleDocument>("alert_rules");
        _alertEvents = database.GetCollection<AlertEventDocument>("alert_events");
        _repositoryInstructions = database.GetCollection<RepositoryInstructionDocument>("repository_instructions");
        _harnessProviderSettings = database.GetCollection<HarnessProviderSettingsDocument>("harness_provider_settings");
    }

    public virtual async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _projects.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<ProjectDocument>(Builders<ProjectDocument>.IndexKeys.Ascending(x => x.Name)),
            ],
            cancellationToken: cancellationToken);

        await _repositories.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<RepositoryDocument>(Builders<RepositoryDocument>.IndexKeys.Ascending(x => x.ProjectId)),
            ],
            cancellationToken: cancellationToken);

        await _tasks.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<TaskDocument>(Builders<TaskDocument>.IndexKeys.Ascending(x => x.RepositoryId)),
                new CreateIndexModel<TaskDocument>(Builders<TaskDocument>.IndexKeys.Ascending(x => x.NextRunAtUtc)),
            ],
            cancellationToken: cancellationToken);

        await _runs.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<RunDocument>(Builders<RunDocument>.IndexKeys.Ascending(x => x.RepositoryId).Descending(x => x.CreatedAtUtc)),
                new CreateIndexModel<RunDocument>(Builders<RunDocument>.IndexKeys.Ascending(x => x.State)),
                new CreateIndexModel<RunDocument>(Builders<RunDocument>.IndexKeys.Ascending(x => x.ProjectId).Ascending(x => x.State)),
                new CreateIndexModel<RunDocument>(Builders<RunDocument>.IndexKeys.Ascending(x => x.TaskId).Ascending(x => x.State)),
                new CreateIndexModel<RunDocument>(
                    Builders<RunDocument>.IndexKeys.Ascending(x => x.CreatedAtUtc),
                    new CreateIndexOptions { ExpireAfter = TimeSpan.FromDays(_options.TtlDays.Runs) }),
            ],
            cancellationToken: cancellationToken);

        await _findings.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<FindingDocument>(Builders<FindingDocument>.IndexKeys.Ascending(x => x.RepositoryId).Descending(x => x.CreatedAtUtc)),
                new CreateIndexModel<FindingDocument>(Builders<FindingDocument>.IndexKeys.Ascending(x => x.State)),
            ],
            cancellationToken: cancellationToken);

        await _runEvents.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<RunLogEvent>(
                    Builders<RunLogEvent>.IndexKeys.Ascending(x => x.RunId).Ascending(x => x.TimestampUtc)),
                new CreateIndexModel<RunLogEvent>(
                    Builders<RunLogEvent>.IndexKeys.Ascending(x => x.TimestampUtc),
                    new CreateIndexOptions { ExpireAfter = TimeSpan.FromDays(_options.TtlDays.Logs) }),
            ],
            cancellationToken: cancellationToken);

        await _providerSecrets.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<ProviderSecretDocument>(
                    Builders<ProviderSecretDocument>.IndexKeys.Ascending(x => x.RepositoryId).Ascending(x => x.Provider),
                    new CreateIndexOptions { Unique = true }),
            ],
            cancellationToken: cancellationToken);

        await _workers.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<WorkerRegistration>(
                    Builders<WorkerRegistration>.IndexKeys.Ascending(x => x.WorkerId),
                    new CreateIndexOptions { Unique = true }),
            ],
            cancellationToken: cancellationToken);

        await _webhooks.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<WebhookRegistration>(
                    Builders<WebhookRegistration>.IndexKeys.Ascending(x => x.RepositoryId)),
            ],
            cancellationToken: cancellationToken);

        await _proxyAudits.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<ProxyAuditDocument>(
                    Builders<ProxyAuditDocument>.IndexKeys.Ascending(x => x.RunId).Descending(x => x.TimestampUtc)),
                new CreateIndexModel<ProxyAuditDocument>(
                    Builders<ProxyAuditDocument>.IndexKeys.Ascending(x => x.TimestampUtc),
                    new CreateIndexOptions { ExpireAfter = TimeSpan.FromDays(_options.TtlDays.Logs) }),
            ],
            cancellationToken: cancellationToken);

        await _workflows.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<WorkflowDocument>(
                    Builders<WorkflowDocument>.IndexKeys.Ascending(x => x.RepositoryId)),
            ],
            cancellationToken: cancellationToken);

        await _workflowExecutions.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<WorkflowExecutionDocument>(
                    Builders<WorkflowExecutionDocument>.IndexKeys.Ascending(x => x.WorkflowId).Descending(x => x.CreatedAtUtc)),
                new CreateIndexModel<WorkflowExecutionDocument>(
                    Builders<WorkflowExecutionDocument>.IndexKeys.Ascending(x => x.State)),
                new CreateIndexModel<WorkflowExecutionDocument>(
                    Builders<WorkflowExecutionDocument>.IndexKeys.Ascending(x => x.CreatedAtUtc),
                    new CreateIndexOptions { ExpireAfter = TimeSpan.FromDays(_options.TtlDays.Runs) }),
            ],
            cancellationToken: cancellationToken);

        await _alertRules.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<AlertRuleDocument>(
                    Builders<AlertRuleDocument>.IndexKeys.Ascending(x => x.Enabled)),
            ],
            cancellationToken: cancellationToken);

        await _alertEvents.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<AlertEventDocument>(
                    Builders<AlertEventDocument>.IndexKeys.Descending(x => x.FiredAtUtc)),
                new CreateIndexModel<AlertEventDocument>(
                    Builders<AlertEventDocument>.IndexKeys.Ascending(x => x.RuleId)),
                new CreateIndexModel<AlertEventDocument>(
                    Builders<AlertEventDocument>.IndexKeys.Ascending(x => x.FiredAtUtc),
                    new CreateIndexOptions { ExpireAfter = TimeSpan.FromDays(_options.TtlDays.Logs) }),
            ],
            cancellationToken: cancellationToken);

        await _repositoryInstructions.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<RepositoryInstructionDocument>(
                    Builders<RepositoryInstructionDocument>.IndexKeys.Ascending(x => x.RepositoryId)),
                new CreateIndexModel<RepositoryInstructionDocument>(
                    Builders<RepositoryInstructionDocument>.IndexKeys.Ascending(x => x.RepositoryId).Ascending(x => x.Priority)),
            ],
            cancellationToken: cancellationToken);

        await _harnessProviderSettings.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<HarnessProviderSettingsDocument>(
                    Builders<HarnessProviderSettingsDocument>.IndexKeys.Ascending(x => x.RepositoryId).Ascending(x => x.Harness),
                    new CreateIndexOptions { Unique = true }),
            ],
            cancellationToken: cancellationToken);
    }

    // --- Projects ---

    public virtual async Task<ProjectDocument> CreateProjectAsync(CreateProjectRequest request, CancellationToken cancellationToken)
    {
        var project = new ProjectDocument
        {
            Name = request.Name,
            Description = request.Description,
        };

        await _projects.InsertOneAsync(project, cancellationToken: cancellationToken);
        return project;
    }

    public virtual Task<List<ProjectDocument>> ListProjectsAsync(CancellationToken cancellationToken)
        => _projects.Find(FilterDefinition<ProjectDocument>.Empty).SortByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken);

    public virtual async Task<ProjectDocument?> GetProjectAsync(string projectId, CancellationToken cancellationToken)
        => await _projects.Find(x => x.Id == projectId).FirstOrDefaultAsync(cancellationToken);

    public virtual async Task<ProjectDocument?> UpdateProjectAsync(string projectId, UpdateProjectRequest request, CancellationToken cancellationToken)
    {
        var update = Builders<ProjectDocument>.Update
            .Set(x => x.Name, request.Name)
            .Set(x => x.Description, request.Description);

        return await _projects.FindOneAndUpdateAsync(
            x => x.Id == projectId,
            update,
            new FindOneAndUpdateOptions<ProjectDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken);
    }

    public virtual async Task<bool> DeleteProjectAsync(string projectId, CancellationToken cancellationToken)
    {
        var result = await _projects.DeleteOneAsync(x => x.Id == projectId, cancellationToken);
        return result.DeletedCount > 0;
    }

    // --- Repositories ---

    public virtual async Task<RepositoryDocument> CreateRepositoryAsync(CreateRepositoryRequest request, CancellationToken cancellationToken)
    {
        var repository = new RepositoryDocument
        {
            ProjectId = request.ProjectId,
            Name = request.Name,
            GitUrl = request.GitUrl,
            DefaultBranch = string.IsNullOrWhiteSpace(request.DefaultBranch) ? "main" : request.DefaultBranch,
        };

        await _repositories.InsertOneAsync(repository, cancellationToken: cancellationToken);
        return repository;
    }

    public virtual Task<List<RepositoryDocument>> ListRepositoriesAsync(string projectId, CancellationToken cancellationToken)
        => _repositories.Find(x => x.ProjectId == projectId).SortBy(x => x.Name).ToListAsync(cancellationToken);

    public virtual async Task<RepositoryDocument?> GetRepositoryAsync(string repositoryId, CancellationToken cancellationToken)
        => await _repositories.Find(x => x.Id == repositoryId).FirstOrDefaultAsync(cancellationToken);

    public virtual async Task<RepositoryDocument?> UpdateRepositoryAsync(string repositoryId, UpdateRepositoryRequest request, CancellationToken cancellationToken)
    {
        var update = Builders<RepositoryDocument>.Update
            .Set(x => x.Name, request.Name)
            .Set(x => x.GitUrl, request.GitUrl)
            .Set(x => x.DefaultBranch, string.IsNullOrWhiteSpace(request.DefaultBranch) ? "main" : request.DefaultBranch);

        return await _repositories.FindOneAndUpdateAsync(
            x => x.Id == repositoryId,
            update,
            new FindOneAndUpdateOptions<RepositoryDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken);
    }

    public virtual async Task<bool> DeleteRepositoryAsync(string repositoryId, CancellationToken cancellationToken)
    {
        var result = await _repositories.DeleteOneAsync(x => x.Id == repositoryId, cancellationToken);
        return result.DeletedCount > 0;
    }

    public virtual async Task<List<InstructionFile>> GetRepositoryInstructionFilesAsync(string repositoryId, CancellationToken cancellationToken)
    {
        var repo = await _repositories.Find(x => x.Id == repositoryId).FirstOrDefaultAsync(cancellationToken);
        return repo?.InstructionFiles ?? [];
    }

    public virtual async Task<RepositoryDocument?> UpdateRepositoryInstructionFilesAsync(string repositoryId, List<InstructionFile> instructionFiles, CancellationToken cancellationToken)
    {
        var update = Builders<RepositoryDocument>.Update
            .Set(x => x.InstructionFiles, instructionFiles);

        return await _repositories.FindOneAndUpdateAsync(
            x => x.Id == repositoryId,
            update,
            new FindOneAndUpdateOptions<RepositoryDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken);
    }

    public virtual async Task<List<RepositoryInstructionDocument>> GetInstructionsAsync(string repositoryId, CancellationToken cancellationToken)
        => await _repositoryInstructions.Find(x => x.RepositoryId == repositoryId)
            .SortBy(x => x.Priority)
            .ToListAsync(cancellationToken);

    public virtual async Task<RepositoryInstructionDocument?> GetInstructionAsync(string instructionId, CancellationToken cancellationToken)
        => await _repositoryInstructions.Find(x => x.Id == instructionId).FirstOrDefaultAsync(cancellationToken);

    public virtual async Task<RepositoryInstructionDocument> UpsertInstructionAsync(
        string repositoryId,
        string? instructionId,
        CreateRepositoryInstructionRequest request,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var id = instructionId ?? Guid.NewGuid().ToString("N");

        var instruction = new RepositoryInstructionDocument
        {
            Id = id,
            RepositoryId = repositoryId,
            Name = request.Name,
            Content = request.Content,
            Priority = request.Priority,
            Enabled = request.Enabled,
            UpdatedAtUtc = now,
            CreatedAtUtc = string.IsNullOrEmpty(instructionId) ? now : (await GetInstructionAsync(id, cancellationToken))?.CreatedAtUtc ?? now
        };

        var filter = Builders<RepositoryInstructionDocument>.Filter.And(
            Builders<RepositoryInstructionDocument>.Filter.Eq(x => x.Id, id),
            Builders<RepositoryInstructionDocument>.Filter.Eq(x => x.RepositoryId, repositoryId));

        var options = new ReplaceOptions { IsUpsert = true };
        await _repositoryInstructions.ReplaceOneAsync(filter, instruction, options, cancellationToken);

        return instruction;
    }

    public virtual async Task<bool> DeleteInstructionAsync(string instructionId, CancellationToken cancellationToken)
    {
        var result = await _repositoryInstructions.DeleteOneAsync(x => x.Id == instructionId, cancellationToken);
        return result.DeletedCount > 0;
    }

    public virtual async Task<HarnessProviderSettingsDocument?> GetHarnessProviderSettingsAsync(
        string repositoryId,
        string harness,
        CancellationToken cancellationToken)
        => await _harnessProviderSettings.Find(x => x.RepositoryId == repositoryId && x.Harness == harness)
            .FirstOrDefaultAsync(cancellationToken);

    public virtual async Task<HarnessProviderSettingsDocument> UpsertHarnessProviderSettingsAsync(
        string repositoryId,
        string harness,
        string model,
        double temperature,
        int maxTokens,
        Dictionary<string, string>? additionalSettings,
        CancellationToken cancellationToken)
    {
        var filter = Builders<HarnessProviderSettingsDocument>.Filter.And(
            Builders<HarnessProviderSettingsDocument>.Filter.Eq(x => x.RepositoryId, repositoryId),
            Builders<HarnessProviderSettingsDocument>.Filter.Eq(x => x.Harness, harness));

        var settings = new HarnessProviderSettingsDocument
        {
            RepositoryId = repositoryId,
            Harness = harness,
            Model = model,
            Temperature = temperature,
            MaxTokens = maxTokens,
            AdditionalSettings = additionalSettings ?? [],
            UpdatedAtUtc = DateTime.UtcNow
        };

        var existing = await _harnessProviderSettings.Find(filter).FirstOrDefaultAsync(cancellationToken);
        if (existing is not null)
        {
            settings.Id = existing.Id;
        }

        var options = new ReplaceOptions { IsUpsert = true };
        await _harnessProviderSettings.ReplaceOneAsync(filter, settings, options, cancellationToken);

        return settings;
    }

    // --- Tasks ---

    public virtual async Task<TaskDocument> CreateTaskAsync(CreateTaskRequest request, CancellationToken cancellationToken)
    {
        var task = new TaskDocument
        {
            RepositoryId = request.RepositoryId,
            Name = request.Name,
            Kind = request.Kind,
            Harness = request.Harness.Trim().ToLowerInvariant(),
            Prompt = request.Prompt,
            Command = request.Command,
            AutoCreatePullRequest = request.AutoCreatePullRequest,
            CronExpression = request.CronExpression,
            Enabled = request.Enabled,
            RetryPolicy = request.RetryPolicy ?? new RetryPolicyConfig(),
            Timeouts = request.Timeouts ?? new TimeoutConfig(),
            SandboxProfile = request.SandboxProfile ?? new SandboxProfileConfig(),
            ArtifactPolicy = request.ArtifactPolicy ?? new ArtifactPolicyConfig(),
            ApprovalProfile = request.ApprovalProfile ?? new ApprovalProfileConfig(),
            ConcurrencyLimit = request.ConcurrencyLimit ?? 0,
            InstructionFiles = request.InstructionFiles ?? [],
        };

        task.NextRunAtUtc = ComputeNextRun(task, DateTime.UtcNow);

        await _tasks.InsertOneAsync(task, cancellationToken: cancellationToken);
        return task;
    }

    public virtual Task<List<TaskDocument>> ListTasksAsync(string repositoryId, CancellationToken cancellationToken)
        => _tasks.Find(x => x.RepositoryId == repositoryId).SortBy(x => x.CreatedAtUtc).ToListAsync(cancellationToken);

    public virtual Task<List<TaskDocument>> ListEventDrivenTasksAsync(string repositoryId, CancellationToken cancellationToken)
        => _tasks.Find(x => x.RepositoryId == repositoryId && x.Enabled && x.Kind == TaskKind.EventDriven)
            .SortBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

    public virtual async Task<TaskDocument?> GetTaskAsync(string taskId, CancellationToken cancellationToken)
        => await _tasks.Find(x => x.Id == taskId).FirstOrDefaultAsync(cancellationToken);

    public virtual Task<List<TaskDocument>> ListScheduledTasksAsync(CancellationToken cancellationToken)
        => _tasks.Find(x => x.Enabled && x.Kind == TaskKind.Cron)
            .SortBy(x => x.NextRunAtUtc)
            .ToListAsync(cancellationToken);

    public virtual async Task<List<TaskDocument>> ListDueTasksAsync(DateTime utcNow, int limit, CancellationToken cancellationToken)
    {
        var filter = Builders<TaskDocument>.Filter.And(
            Builders<TaskDocument>.Filter.Eq(x => x.Enabled, true),
            Builders<TaskDocument>.Filter.Or(
                Builders<TaskDocument>.Filter.Eq(x => x.Kind, TaskKind.OneShot),
                Builders<TaskDocument>.Filter.And(
                    Builders<TaskDocument>.Filter.Eq(x => x.Kind, TaskKind.Cron),
                    Builders<TaskDocument>.Filter.Lte(x => x.NextRunAtUtc, utcNow))));

        return await _tasks.Find(filter).Limit(limit).ToListAsync(cancellationToken);
    }

    public virtual Task MarkOneShotTaskConsumedAsync(string taskId, CancellationToken cancellationToken)
        => _tasks.UpdateOneAsync(
            x => x.Id == taskId,
            Builders<TaskDocument>.Update.Set(x => x.Enabled, false),
            cancellationToken: cancellationToken);

    public virtual Task UpdateTaskNextRunAsync(string taskId, DateTime? nextRunAtUtc, CancellationToken cancellationToken)
        => _tasks.UpdateOneAsync(
            x => x.Id == taskId,
            Builders<TaskDocument>.Update.Set(x => x.NextRunAtUtc, nextRunAtUtc),
            cancellationToken: cancellationToken);

    public virtual async Task<TaskDocument?> UpdateTaskAsync(string taskId, UpdateTaskRequest request, CancellationToken cancellationToken)
    {
        var task = new TaskDocument
        {
            Kind = request.Kind,
            Enabled = request.Enabled,
            CronExpression = request.CronExpression,
        };
        var nextRun = ComputeNextRun(task, DateTime.UtcNow);

        var update = Builders<TaskDocument>.Update
            .Set(x => x.Name, request.Name)
            .Set(x => x.Kind, request.Kind)
            .Set(x => x.Harness, request.Harness.Trim().ToLowerInvariant())
            .Set(x => x.Prompt, request.Prompt)
            .Set(x => x.Command, request.Command)
            .Set(x => x.AutoCreatePullRequest, request.AutoCreatePullRequest)
            .Set(x => x.CronExpression, request.CronExpression)
            .Set(x => x.Enabled, request.Enabled)
            .Set(x => x.RetryPolicy, request.RetryPolicy ?? new RetryPolicyConfig())
            .Set(x => x.Timeouts, request.Timeouts ?? new TimeoutConfig())
            .Set(x => x.SandboxProfile, request.SandboxProfile ?? new SandboxProfileConfig())
            .Set(x => x.ArtifactPolicy, request.ArtifactPolicy ?? new ArtifactPolicyConfig())
            .Set(x => x.ApprovalProfile, request.ApprovalProfile ?? new ApprovalProfileConfig())
            .Set(x => x.ConcurrencyLimit, request.ConcurrencyLimit ?? 0)
            .Set(x => x.InstructionFiles, request.InstructionFiles ?? [])
            .Set(x => x.NextRunAtUtc, nextRun);

        return await _tasks.FindOneAndUpdateAsync(
            x => x.Id == taskId,
            update,
            new FindOneAndUpdateOptions<TaskDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken);
    }

    public virtual async Task<bool> DeleteTaskAsync(string taskId, CancellationToken cancellationToken)
    {
        var result = await _tasks.DeleteOneAsync(x => x.Id == taskId, cancellationToken);
        return result.DeletedCount > 0;
    }

    // --- Runs ---

    public virtual async Task<RunDocument> CreateRunAsync(TaskDocument task, string projectId, CancellationToken cancellationToken, int attempt = 1)
    {
        var run = new RunDocument
        {
            ProjectId = projectId,
            RepositoryId = task.RepositoryId,
            TaskId = task.Id,
            State = RunState.Queued,
            Summary = "Queued",
            Attempt = attempt,
        };

        await _runs.InsertOneAsync(run, cancellationToken: cancellationToken);
        return run;
    }

    public virtual Task<List<RunDocument>> ListRunsByRepositoryAsync(string repositoryId, CancellationToken cancellationToken)
        => _runs.Find(x => x.RepositoryId == repositoryId).SortByDescending(x => x.CreatedAtUtc).Limit(200).ToListAsync(cancellationToken);

    public virtual Task<List<RunDocument>> ListRecentRunsAsync(CancellationToken cancellationToken)
        => _runs.Find(FilterDefinition<RunDocument>.Empty).SortByDescending(x => x.CreatedAtUtc).Limit(100).ToListAsync(cancellationToken);

    public virtual Task<List<RunDocument>> ListRecentRunsByProjectAsync(string projectId, CancellationToken cancellationToken)
        => _runs.Find(x => x.ProjectId == projectId).SortByDescending(x => x.CreatedAtUtc).Limit(100).ToListAsync(cancellationToken);

    public virtual async Task<ReliabilityMetrics> GetReliabilityMetricsByProjectAsync(string projectId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var sevenDaysAgo = now.AddDays(-7);
        var thirtyDaysAgo = now.AddDays(-30);
        var fourteenDaysAgo = now.AddDays(-14);

        var recentRuns = await _runs.Find(x => x.ProjectId == projectId && x.CreatedAtUtc >= thirtyDaysAgo)
            .ToListAsync(cancellationToken);

        return CalculateMetricsFromRuns(recentRuns, sevenDaysAgo, thirtyDaysAgo, fourteenDaysAgo, now);
    }

    public virtual async Task<ReliabilityMetrics> GetReliabilityMetricsByRepositoryAsync(string repositoryId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var sevenDaysAgo = now.AddDays(-7);
        var thirtyDaysAgo = now.AddDays(-30);
        var fourteenDaysAgo = now.AddDays(-14);

        var recentRuns = await _runs.Find(x => x.RepositoryId == repositoryId && x.CreatedAtUtc >= thirtyDaysAgo)
            .ToListAsync(cancellationToken);

        return CalculateMetricsFromRuns(recentRuns, sevenDaysAgo, thirtyDaysAgo, fourteenDaysAgo, now);
    }

    private ReliabilityMetrics CalculateMetricsFromRuns(List<RunDocument> recentRuns, DateTime sevenDaysAgo, DateTime thirtyDaysAgo, DateTime fourteenDaysAgo, DateTime now)
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

        return new ReliabilityMetrics(
            successRate7Days,
            successRate30Days,
            runs7Days.Count,
            runs30Days.Count,
            runsByState,
            failureTrend,
            avgDuration,
            []);
    }

    public virtual async Task<RunDocument?> GetRunAsync(string runId, CancellationToken cancellationToken)
        => await _runs.Find(x => x.Id == runId).FirstOrDefaultAsync(cancellationToken);

    public virtual Task<List<RunDocument>> ListRunsByStateAsync(RunState state, CancellationToken cancellationToken)
        => _runs.Find(x => x.State == state).SortByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken);

    public virtual async Task<List<string>> ListAllRunIdsAsync(CancellationToken cancellationToken)
    {
        var runs = await _runs.Find(FilterDefinition<RunDocument>.Empty)
            .Project(x => x.Id)
            .ToListAsync(cancellationToken);
        return runs;
    }

    public virtual async Task<long> CountActiveRunsAsync(CancellationToken cancellationToken)
        => await _runs.CountDocumentsAsync(x => x.State == RunState.Queued || x.State == RunState.Running || x.State == RunState.PendingApproval, cancellationToken: cancellationToken);

    public virtual async Task<long> CountActiveRunsByProjectAsync(string projectId, CancellationToken cancellationToken)
        => await _runs.CountDocumentsAsync(x => x.ProjectId == projectId && (x.State == RunState.Queued || x.State == RunState.Running || x.State == RunState.PendingApproval), cancellationToken: cancellationToken);

    public virtual async Task<long> CountActiveRunsByRepoAsync(string repositoryId, CancellationToken cancellationToken)
        => await _runs.CountDocumentsAsync(x => x.RepositoryId == repositoryId && (x.State == RunState.Queued || x.State == RunState.Running || x.State == RunState.PendingApproval), cancellationToken: cancellationToken);

    public virtual async Task<long> CountActiveRunsByTaskAsync(string taskId, CancellationToken cancellationToken)
        => await _runs.CountDocumentsAsync(x => x.TaskId == taskId && (x.State == RunState.Queued || x.State == RunState.Running || x.State == RunState.PendingApproval), cancellationToken: cancellationToken);

    public virtual async Task<RunDocument?> MarkRunStartedAsync(string runId, CancellationToken cancellationToken)
    {
        var update = Builders<RunDocument>.Update
            .Set(x => x.State, RunState.Running)
            .Set(x => x.StartedAtUtc, DateTime.UtcNow)
            .Set(x => x.Summary, "Running");

        return await _runs.FindOneAndUpdateAsync(
            x => x.Id == runId,
            update,
            new FindOneAndUpdateOptions<RunDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken);
    }

    public virtual async Task<RunDocument?> MarkRunCompletedAsync(
        string runId,
        bool succeeded,
        string summary,
        string outputJson,
        CancellationToken cancellationToken,
        string? failureClass = null,
        string? prUrl = null)
    {
        var updateDef = Builders<RunDocument>.Update
            .Set(x => x.State, succeeded ? RunState.Succeeded : RunState.Failed)
            .Set(x => x.EndedAtUtc, DateTime.UtcNow)
            .Set(x => x.Summary, summary)
            .Set(x => x.OutputJson, outputJson);

        if (!string.IsNullOrEmpty(failureClass))
            updateDef = updateDef.Set(x => x.FailureClass, failureClass);
        if (!string.IsNullOrEmpty(prUrl))
            updateDef = updateDef.Set(x => x.PrUrl, prUrl);

        return await _runs.FindOneAndUpdateAsync(
            x => x.Id == runId,
            updateDef,
            new FindOneAndUpdateOptions<RunDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken);
    }

    public virtual async Task<RunDocument?> MarkRunCancelledAsync(string runId, CancellationToken cancellationToken)
    {
        var update = Builders<RunDocument>.Update
            .Set(x => x.State, RunState.Cancelled)
            .Set(x => x.EndedAtUtc, DateTime.UtcNow)
            .Set(x => x.Summary, "Cancelled");

        return await _runs.FindOneAndUpdateAsync(
            x => x.Id == runId && (x.State == RunState.Queued || x.State == RunState.Running || x.State == RunState.PendingApproval),
            update,
            new FindOneAndUpdateOptions<RunDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken);
    }

    public virtual async Task<RunDocument?> MarkRunPendingApprovalAsync(string runId, CancellationToken cancellationToken)
    {
        var update = Builders<RunDocument>.Update
            .Set(x => x.State, RunState.PendingApproval)
            .Set(x => x.Summary, "Pending approval");

        return await _runs.FindOneAndUpdateAsync(
            x => x.Id == runId,
            update,
            new FindOneAndUpdateOptions<RunDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken);
    }

    public virtual async Task<RunDocument?> ApproveRunAsync(string runId, CancellationToken cancellationToken)
    {
        var update = Builders<RunDocument>.Update
            .Set(x => x.State, RunState.Queued)
            .Set(x => x.Summary, "Approved and queued");

        return await _runs.FindOneAndUpdateAsync(
            x => x.Id == runId && x.State == RunState.PendingApproval,
            update,
            new FindOneAndUpdateOptions<RunDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken);
    }

    public virtual async Task<RunDocument?> RejectRunAsync(string runId, CancellationToken cancellationToken)
    {
        var update = Builders<RunDocument>.Update
            .Set(x => x.State, RunState.Cancelled)
            .Set(x => x.EndedAtUtc, DateTime.UtcNow)
            .Set(x => x.Summary, "Rejected");

        return await _runs.FindOneAndUpdateAsync(
            x => x.Id == runId && x.State == RunState.PendingApproval,
            update,
            new FindOneAndUpdateOptions<RunDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken);
    }

    // --- Artifacts ---

    public virtual async Task SaveArtifactAsync(string runId, string fileName, Stream stream, CancellationToken cancellationToken)
    {
        var artifactDir = Path.Combine("/data/artifacts", runId);
        Directory.CreateDirectory(artifactDir);

        var filePath = Path.Combine(artifactDir, fileName);
        using var fileStream = File.Create(filePath);
        await stream.CopyToAsync(fileStream, cancellationToken);
    }

    public virtual Task<List<string>> ListArtifactsAsync(string runId, CancellationToken cancellationToken)
    {
        var artifactDir = Path.Combine("/data/artifacts", runId);
        if (!Directory.Exists(artifactDir))
            return Task.FromResult(new List<string>());

        var files = Directory.GetFiles(artifactDir)
            .Select(Path.GetFileName)
            .Where(f => f is not null)
            .Cast<string>()
            .ToList();

        return Task.FromResult(files);
    }

    public virtual Task<FileStream?> GetArtifactAsync(string runId, string fileName, CancellationToken cancellationToken)
    {
        var artifactDir = Path.Combine("/data/artifacts", runId);
        var filePath = Path.Combine(artifactDir, fileName);

        if (!File.Exists(filePath))
            return Task.FromResult<FileStream?>(null);

        return Task.FromResult<FileStream?>(File.OpenRead(filePath));
    }

    // --- Run Logs ---

    public virtual Task AddRunLogAsync(RunLogEvent logEvent, CancellationToken cancellationToken)
        => _runEvents.InsertOneAsync(logEvent, cancellationToken: cancellationToken);

    public virtual Task<List<RunLogEvent>> ListRunLogsAsync(string runId, CancellationToken cancellationToken)
        => _runEvents.Find(x => x.RunId == runId).SortBy(x => x.TimestampUtc).ToListAsync(cancellationToken);

    // --- Findings ---

    public virtual Task<List<FindingDocument>> ListFindingsAsync(string repositoryId, CancellationToken cancellationToken)
        => _findings.Find(x => x.RepositoryId == repositoryId).SortByDescending(x => x.CreatedAtUtc).Limit(200).ToListAsync(cancellationToken);

    public virtual Task<List<FindingDocument>> ListAllFindingsAsync(CancellationToken cancellationToken)
        => _findings.Find(FilterDefinition<FindingDocument>.Empty).SortByDescending(x => x.CreatedAtUtc).Limit(500).ToListAsync(cancellationToken);

    public virtual async Task<FindingDocument?> GetFindingAsync(string findingId, CancellationToken cancellationToken)
        => await _findings.Find(x => x.Id == findingId).FirstOrDefaultAsync(cancellationToken);

    public virtual async Task<FindingDocument> CreateFindingFromFailureAsync(RunDocument run, string description, CancellationToken cancellationToken)
    {
        var finding = new FindingDocument
        {
            RepositoryId = run.RepositoryId,
            RunId = run.Id,
            Title = $"Run {run.Id[..8]} failed",
            Description = description,
            Severity = FindingSeverity.High,
            State = FindingState.New,
        };

        await _findings.InsertOneAsync(finding, cancellationToken: cancellationToken);
        return finding;
    }

    public virtual async Task<FindingDocument?> UpdateFindingStateAsync(string findingId, FindingState state, CancellationToken cancellationToken)
    {
        var update = Builders<FindingDocument>.Update.Set(x => x.State, state);

        return await _findings.FindOneAndUpdateAsync(
            x => x.Id == findingId,
            update,
            new FindOneAndUpdateOptions<FindingDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken);
    }

    // --- Provider Secrets ---

    public virtual async Task UpsertProviderSecretAsync(
        string repositoryId,
        string provider,
        string encryptedValue,
        CancellationToken cancellationToken)
    {
        var filter = Builders<ProviderSecretDocument>.Filter.And(
            Builders<ProviderSecretDocument>.Filter.Eq(x => x.RepositoryId, repositoryId),
            Builders<ProviderSecretDocument>.Filter.Eq(x => x.Provider, provider));

        var update = Builders<ProviderSecretDocument>.Update
            .Set(x => x.EncryptedValue, encryptedValue)
            .Set(x => x.UpdatedAtUtc, DateTime.UtcNow)
            .SetOnInsert(x => x.RepositoryId, repositoryId)
            .SetOnInsert(x => x.Provider, provider);

        await _providerSecrets.UpdateOneAsync(
            filter,
            update,
            new UpdateOptions { IsUpsert = true },
            cancellationToken);
    }

    public virtual Task<List<ProviderSecretDocument>> ListProviderSecretsAsync(string repositoryId, CancellationToken cancellationToken)
        => _providerSecrets.Find(x => x.RepositoryId == repositoryId).ToListAsync(cancellationToken);

    public virtual async Task<ProviderSecretDocument?> GetProviderSecretAsync(string repositoryId, string provider, CancellationToken cancellationToken)
        => await _providerSecrets.Find(x => x.RepositoryId == repositoryId && x.Provider == provider)
            .FirstOrDefaultAsync(cancellationToken);

    // --- Workers ---

    public virtual Task<List<WorkerRegistration>> ListWorkersAsync(CancellationToken cancellationToken)
        => _workers.Find(FilterDefinition<WorkerRegistration>.Empty).SortBy(x => x.WorkerId).ToListAsync(cancellationToken);

    public virtual async Task UpsertWorkerHeartbeatAsync(string workerId, string endpoint, int activeSlots, int maxSlots, CancellationToken cancellationToken)
    {
        var filter = Builders<WorkerRegistration>.Filter.Eq(x => x.WorkerId, workerId);
        var update = Builders<WorkerRegistration>.Update
            .Set(x => x.Endpoint, endpoint)
            .Set(x => x.ActiveSlots, activeSlots)
            .Set(x => x.MaxSlots, maxSlots)
            .Set(x => x.Online, true)
            .Set(x => x.LastHeartbeatUtc, DateTime.UtcNow)
            .SetOnInsert(x => x.WorkerId, workerId)
            .SetOnInsert(x => x.RegisteredAtUtc, DateTime.UtcNow);

        await _workers.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = true }, cancellationToken: cancellationToken);
    }

    public virtual async Task MarkStaleWorkersOfflineAsync(TimeSpan threshold, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow - threshold;
        await _workers.UpdateManyAsync(
            x => x.Online && x.LastHeartbeatUtc < cutoff,
            Builders<WorkerRegistration>.Update.Set(x => x.Online, false),
            cancellationToken: cancellationToken);
    }

    // --- Webhooks ---

    public virtual async Task<WebhookRegistration> CreateWebhookAsync(CreateWebhookRequest request, CancellationToken cancellationToken)
    {
        var webhook = new WebhookRegistration
        {
            RepositoryId = request.RepositoryId,
            TaskId = request.TaskId,
            EventFilter = request.EventFilter,
            Secret = request.Secret,
        };

        await _webhooks.InsertOneAsync(webhook, cancellationToken: cancellationToken);
        return webhook;
    }

    public virtual Task<List<WebhookRegistration>> ListWebhooksAsync(string repositoryId, CancellationToken cancellationToken)
        => _webhooks.Find(x => x.RepositoryId == repositoryId).ToListAsync(cancellationToken);

    public virtual async Task<bool> DeleteWebhookAsync(string webhookId, CancellationToken cancellationToken)
    {
        var result = await _webhooks.DeleteOneAsync(x => x.Id == webhookId, cancellationToken);
        return result.DeletedCount > 0;
    }

    // --- Finding Assignment ---

    public virtual async Task<FindingDocument?> AssignFindingAsync(string findingId, string assignedTo, CancellationToken cancellationToken)
    {
        var update = Builders<FindingDocument>.Update
            .Set(x => x.AssignedTo, assignedTo)
            .Set(x => x.State, FindingState.InProgress);

        return await _findings.FindOneAndUpdateAsync(
            x => x.Id == findingId,
            update,
            new FindOneAndUpdateOptions<FindingDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken);
    }

    // --- Proxy Audits ---

    public virtual Task RecordProxyRequestAsync(ProxyAuditDocument audit, CancellationToken cancellationToken)
        => _proxyAudits.InsertOneAsync(audit, cancellationToken: cancellationToken);

    public virtual Task<List<ProxyAuditDocument>> ListProxyAuditsAsync(string runId, CancellationToken cancellationToken)
        => _proxyAudits.Find(x => x.RunId == runId).SortByDescending(x => x.TimestampUtc).Limit(200).ToListAsync(cancellationToken);

    public virtual async Task<List<ProxyAuditDocument>> ListProxyAuditsAsync(
        string? projectId,
        string? repoId,
        string? taskId,
        string? runId,
        int limit,
        CancellationToken cancellationToken)
    {
        var filterBuilder = Builders<ProxyAuditDocument>.Filter;
        var filters = new List<FilterDefinition<ProxyAuditDocument>>();

        if (!string.IsNullOrEmpty(projectId))
            filters.Add(filterBuilder.Eq(x => x.ProjectId, projectId));
        if (!string.IsNullOrEmpty(repoId))
            filters.Add(filterBuilder.Eq(x => x.RepoId, repoId));
        if (!string.IsNullOrEmpty(taskId))
            filters.Add(filterBuilder.Eq(x => x.TaskId, taskId));
        if (!string.IsNullOrEmpty(runId))
            filters.Add(filterBuilder.Eq(x => x.RunId, runId));

        var filter = filters.Count > 0 ? filterBuilder.And(filters) : FilterDefinition<ProxyAuditDocument>.Empty;

        return await _proxyAudits
            .Find(filter)
            .SortByDescending(x => x.TimestampUtc)
            .Limit(limit)
            .ToListAsync(cancellationToken);
    }

    // --- System Settings ---

    public virtual async Task<SystemSettingsDocument> GetSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await _settings.Find(x => x.Id == "singleton").FirstOrDefaultAsync(cancellationToken);
        return settings ?? new SystemSettingsDocument();
    }

    public virtual async Task<SystemSettingsDocument> UpdateSettingsAsync(SystemSettingsDocument settings, CancellationToken cancellationToken)
    {
        settings.Id = "singleton";
        settings.UpdatedAtUtc = DateTime.UtcNow;
        await _settings.ReplaceOneAsync(
            x => x.Id == "singleton",
            settings,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
        return settings;
    }

    // --- Workflows ---

    public virtual async Task<WorkflowDocument> CreateWorkflowAsync(WorkflowDocument workflow, CancellationToken cancellationToken)
    {
        await _workflows.InsertOneAsync(workflow, cancellationToken: cancellationToken);
        return workflow;
    }

    public virtual Task<List<WorkflowDocument>> ListWorkflowsByRepositoryAsync(string repositoryId, CancellationToken cancellationToken)
        => _workflows.Find(x => x.RepositoryId == repositoryId).SortByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken);

    public virtual Task<List<WorkflowDocument>> ListAllWorkflowsAsync(CancellationToken cancellationToken)
        => _workflows.Find(FilterDefinition<WorkflowDocument>.Empty).SortByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken);

    public virtual async Task<WorkflowDocument?> GetWorkflowAsync(string workflowId, CancellationToken cancellationToken)
        => await _workflows.Find(x => x.Id == workflowId).FirstOrDefaultAsync(cancellationToken);

    public virtual async Task<WorkflowDocument?> UpdateWorkflowAsync(string workflowId, WorkflowDocument workflow, CancellationToken cancellationToken)
    {
        var result = await _workflows.ReplaceOneAsync(x => x.Id == workflowId, workflow, cancellationToken: cancellationToken);
        return result.ModifiedCount > 0 ? workflow : null;
    }

    public virtual async Task<bool> DeleteWorkflowAsync(string workflowId, CancellationToken cancellationToken)
    {
        var result = await _workflows.DeleteOneAsync(x => x.Id == workflowId, cancellationToken);
        return result.DeletedCount > 0;
    }

    // --- Workflow Executions ---

    public virtual async Task<WorkflowExecutionDocument> CreateWorkflowExecutionAsync(WorkflowExecutionDocument execution, CancellationToken cancellationToken)
    {
        await _workflowExecutions.InsertOneAsync(execution, cancellationToken: cancellationToken);
        return execution;
    }

    public virtual Task<List<WorkflowExecutionDocument>> ListWorkflowExecutionsAsync(string workflowId, CancellationToken cancellationToken)
        => _workflowExecutions.Find(x => x.WorkflowId == workflowId).SortByDescending(x => x.CreatedAtUtc).Limit(100).ToListAsync(cancellationToken);

    public virtual Task<List<WorkflowExecutionDocument>> ListWorkflowExecutionsByStateAsync(WorkflowExecutionState state, CancellationToken cancellationToken)
        => _workflowExecutions.Find(x => x.State == state).SortByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken);

    public virtual async Task<AlertEventDocument?> ResolveAlertEventAsync(string eventId, CancellationToken cancellationToken)
    {
        var update = Builders<AlertEventDocument>.Update.Set(x => x.Resolved, true);

        return await _alertEvents.FindOneAndUpdateAsync(
            x => x.Id == eventId,
            update,
            new FindOneAndUpdateOptions<AlertEventDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken);
    }

    public virtual async Task<int> ResolveAlertEventsAsync(List<string> eventIds, CancellationToken cancellationToken)
    {
        if (eventIds.Count == 0) return 0;

        var update = Builders<AlertEventDocument>.Update.Set(x => x.Resolved, true);
        var result = await _alertEvents.UpdateManyAsync(
            x => eventIds.Contains(x.Id),
            update,
            cancellationToken: cancellationToken);
        return (int)result.ModifiedCount;
    }

    public virtual async Task<int> BulkCancelRunsAsync(List<string> runIds, CancellationToken cancellationToken)
    {
        if (runIds.Count == 0) return 0;

        var update = Builders<RunDocument>.Update
            .Set(x => x.State, RunState.Cancelled)
            .Set(x => x.EndedAtUtc, DateTime.UtcNow);

        var result = await _runs.UpdateManyAsync(
            x => runIds.Contains(x.Id) && (x.State == RunState.Queued || x.State == RunState.Running || x.State == RunState.PendingApproval),
            update,
            cancellationToken: cancellationToken);
        return (int)result.ModifiedCount;
    }

    public virtual async Task<WorkflowExecutionDocument?> GetWorkflowExecutionAsync(string executionId, CancellationToken cancellationToken)
        => await _workflowExecutions.Find(x => x.Id == executionId).FirstOrDefaultAsync(cancellationToken);

    public virtual async Task<WorkflowExecutionDocument?> UpdateWorkflowExecutionAsync(WorkflowExecutionDocument execution, CancellationToken cancellationToken)
    {
        var result = await _workflowExecutions.ReplaceOneAsync(x => x.Id == execution.Id, execution, cancellationToken: cancellationToken);
        return result.ModifiedCount > 0 ? execution : null;
    }

    public virtual async Task<WorkflowExecutionDocument?> MarkWorkflowExecutionCompletedAsync(
        string executionId,
        WorkflowExecutionState finalState,
        string failureReason,
        CancellationToken cancellationToken)
    {
        var update = Builders<WorkflowExecutionDocument>.Update
            .Set(x => x.State, finalState)
            .Set(x => x.EndedAtUtc, DateTime.UtcNow)
            .Set(x => x.FailureReason, failureReason);

        return await _workflowExecutions.FindOneAndUpdateAsync(
            x => x.Id == executionId,
            update,
            new FindOneAndUpdateOptions<WorkflowExecutionDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken);
    }

    public virtual async Task<WorkflowExecutionDocument?> MarkWorkflowExecutionPendingApprovalAsync(
        string executionId,
        string pendingApprovalStageId,
        CancellationToken cancellationToken)
    {
        var update = Builders<WorkflowExecutionDocument>.Update
            .Set(x => x.State, WorkflowExecutionState.PendingApproval)
            .Set(x => x.PendingApprovalStageId, pendingApprovalStageId);

        return await _workflowExecutions.FindOneAndUpdateAsync(
            x => x.Id == executionId,
            update,
            new FindOneAndUpdateOptions<WorkflowExecutionDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken);
    }

    public virtual async Task<WorkflowExecutionDocument?> ApproveWorkflowStageAsync(
        string executionId,
        string approvedBy,
        CancellationToken cancellationToken)
    {
        var update = Builders<WorkflowExecutionDocument>.Update
            .Set(x => x.State, WorkflowExecutionState.Running)
            .Set(x => x.ApprovedBy, approvedBy)
            .Set(x => x.PendingApprovalStageId, string.Empty);

        return await _workflowExecutions.FindOneAndUpdateAsync(
            x => x.Id == executionId && x.State == WorkflowExecutionState.PendingApproval,
            update,
            new FindOneAndUpdateOptions<WorkflowExecutionDocument> { ReturnDocument = ReturnDocument.After },
            cancellationToken);
    }

    public virtual async Task<WorkflowExecutionDocument?> GetWorkflowExecutionByRunIdAsync(string runId, CancellationToken cancellationToken)
    {
        var filter = Builders<WorkflowExecutionDocument>.Filter.ElemMatch(
            x => x.StageResults,
            stage => stage.RunIds.Contains(runId));

        return await _workflowExecutions.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    public virtual async Task<WorkflowDocument?> GetWorkflowForExecutionAsync(string workflowId, CancellationToken cancellationToken)
    {
        return await _workflows.Find(x => x.Id == workflowId).FirstOrDefaultAsync(cancellationToken);
    }

    // --- Alert Rules ---

    public virtual async Task<AlertRuleDocument> CreateAlertRuleAsync(AlertRuleDocument rule, CancellationToken cancellationToken)
    {
        await _alertRules.InsertOneAsync(rule, cancellationToken: cancellationToken);
        return rule;
    }

    public virtual Task<List<AlertRuleDocument>> ListAlertRulesAsync(CancellationToken cancellationToken)
        => _alertRules.Find(FilterDefinition<AlertRuleDocument>.Empty).SortBy(x => x.Name).ToListAsync(cancellationToken);

    public virtual Task<List<AlertRuleDocument>> ListEnabledAlertRulesAsync(CancellationToken cancellationToken)
        => _alertRules.Find(x => x.Enabled).ToListAsync(cancellationToken);

    public virtual async Task<AlertRuleDocument?> GetAlertRuleAsync(string ruleId, CancellationToken cancellationToken)
        => await _alertRules.Find(x => x.Id == ruleId).FirstOrDefaultAsync(cancellationToken);

    public virtual async Task<AlertRuleDocument?> UpdateAlertRuleAsync(string ruleId, AlertRuleDocument rule, CancellationToken cancellationToken)
    {
        rule.Id = ruleId;
        var result = await _alertRules.ReplaceOneAsync(x => x.Id == ruleId, rule, cancellationToken: cancellationToken);
        return result.ModifiedCount > 0 ? rule : null;
    }

    public virtual async Task<bool> DeleteAlertRuleAsync(string ruleId, CancellationToken cancellationToken)
    {
        var result = await _alertRules.DeleteOneAsync(x => x.Id == ruleId, cancellationToken);
        return result.DeletedCount > 0;
    }

    // --- Alert Events ---

    public virtual async Task<AlertEventDocument> RecordAlertEventAsync(AlertEventDocument alertEvent, CancellationToken cancellationToken)
    {
        await _alertEvents.InsertOneAsync(alertEvent, cancellationToken: cancellationToken);
        return alertEvent;
    }

    public virtual Task<List<AlertEventDocument>> ListRecentAlertEventsAsync(int limit, CancellationToken cancellationToken)
        => _alertEvents.Find(FilterDefinition<AlertEventDocument>.Empty)
            .SortByDescending(x => x.FiredAtUtc)
            .Limit(limit)
            .ToListAsync(cancellationToken);

    public virtual Task<List<AlertEventDocument>> ListAlertEventsByRuleAsync(string ruleId, CancellationToken cancellationToken)
        => _alertEvents.Find(x => x.RuleId == ruleId)
            .SortByDescending(x => x.FiredAtUtc)
            .Limit(50)
            .ToListAsync(cancellationToken);

    // --- Reliability Metrics ---

    public virtual async Task<ReliabilityMetrics> GetReliabilityMetricsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var sevenDaysAgo = now.AddDays(-7);
        var thirtyDaysAgo = now.AddDays(-30);
        var fourteenDaysAgo = now.AddDays(-14);

        var recentRuns = await _runs.Find(x => x.CreatedAtUtc >= thirtyDaysAgo)
            .ToListAsync(cancellationToken);

        var runs7Days = recentRuns.Where(r => r.CreatedAtUtc >= sevenDaysAgo).ToList();
        var runs30Days = recentRuns.ToList();

        var successRate7Days = CalculateSuccessRate(runs7Days);
        var successRate30Days = CalculateSuccessRate(runs30Days);

        var runsByState = recentRuns
            .GroupBy(r => r.State.ToString())
            .ToDictionary(g => g.Key, g => g.Count());

        var failureTrend = CalculateFailureTrend(recentRuns.Where(r => r.CreatedAtUtc >= fourteenDaysAgo).ToList(), fourteenDaysAgo, now);

        var avgDuration = CalculateAverageDuration(recentRuns);

        var projects = await _projects.Find(FilterDefinition<ProjectDocument>.Empty).ToListAsync(cancellationToken);
        var projectMetrics = CalculateProjectMetrics(recentRuns, projects);

        return new ReliabilityMetrics(
            successRate7Days,
            successRate30Days,
            runs7Days.Count,
            runs30Days.Count,
            runsByState,
            failureTrend,
            avgDuration,
            projectMetrics);
    }

    private static double CalculateSuccessRate(List<RunDocument> runs)
    {
        if (runs.Count == 0) return 0;

        var completed = runs.Where(r => r.State == RunState.Succeeded || r.State == RunState.Failed).ToList();
        if (completed.Count == 0) return 0;

        var succeeded = completed.Count(r => r.State == RunState.Succeeded);
        return Math.Round((double)succeeded / completed.Count * 100, 1);
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
        var completedRuns = runs
            .Where(r => r.StartedAtUtc.HasValue && r.EndedAtUtc.HasValue)
            .ToList();

        if (completedRuns.Count == 0) return null;

        var avgSeconds = completedRuns
            .Average(r => (r.EndedAtUtc!.Value - r.StartedAtUtc!.Value).TotalSeconds);

        return Math.Round(avgSeconds, 1);
    }

    private static List<ProjectReliabilityMetrics> CalculateProjectMetrics(List<RunDocument> runs, List<ProjectDocument> projects)
    {
        var projectDict = projects.ToDictionary(p => p.Id, p => p.Name);
        var projectRuns = runs.GroupBy(r => r.ProjectId).ToList();

        return projectRuns.Select(g =>
        {
            var projectRunsList = g.ToList();
            var total = projectRunsList.Count;
            var succeeded = projectRunsList.Count(r => r.State == RunState.Succeeded);
            var failed = projectRunsList.Count(r => r.State == RunState.Failed);
            var rate = total > 0 ? Math.Round((double)succeeded / total * 100, 1) : 0;

            return new ProjectReliabilityMetrics(
                g.Key,
                projectDict.GetValueOrDefault(g.Key, "Unknown"),
                total,
                succeeded,
                failed,
                rate);
        }).OrderByDescending(p => p.TotalRuns).ToList();
    }

    // --- Helpers ---

    public static DateTime? ComputeNextRun(TaskDocument task, DateTime nowUtc)
    {
        if (!task.Enabled)
        {
            return null;
        }

        if (task.Kind == TaskKind.OneShot)
        {
            return nowUtc;
        }

        if (task.Kind != TaskKind.Cron || string.IsNullOrWhiteSpace(task.CronExpression))
        {
            return null;
        }

        var expression = CronExpression.Parse(task.CronExpression, CronFormat.Standard);
        return expression.GetNextOccurrence(nowUtc, TimeZoneInfo.Utc);
    }
}
