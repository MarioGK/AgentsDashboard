using System.Text.Json;
using System.Text.RegularExpressions;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using Cronos;

namespace AgentsDashboard.ControlPlane.Data;

public sealed partial class OrchestratorStore(
    IRepository<RepositoryDocument> repositories,
    IRepository<TaskDocument> tasks,
    IRepository<RunDocument> runs,
    IRepository<WorkspacePromptEntryDocument> workspacePromptEntries,
    IRepository<SemanticChunkDocument> semanticChunks,
    IRepository<RunAiSummaryDocument> runAiSummaries,
    IRepository<RunLogEvent> runEvents,
    IRepository<RunStructuredEventDocument> runStructuredEvents,
    IRepository<RunDiffSnapshotDocument> runDiffSnapshots,
    IRepository<RunToolProjectionDocument> runToolProjections,
    IRepository<RunSessionProfileDocument> runSessionProfiles,
    IRepository<RunInstructionStackDocument> runInstructionStacks,
    IRepository<RunShareBundleDocument> runShareBundles,
    IRepository<AutomationDefinitionDocument> automationDefinitions,
    IRepository<AutomationExecutionDocument> automationExecutions,
    IRepository<FindingDocument> findings,
    IRepository<ProviderSecretDocument> providerSecrets,
    IRepository<TaskRuntimeRegistration> taskRuntimeRegistrations,
    IRepository<TaskRuntimeDocument> taskRuntimes,
    IRepository<WebhookRegistration> webhooks,
    IRepository<SystemSettingsDocument> settings,
    IRepository<OrchestratorLeaseDocument> leases,
    IRepository<WorkflowDocument> workflows,
    IRepository<WorkflowExecutionDocument> workflowExecutions,
    IRepository<AlertRuleDocument> alertRules,
    IRepository<AlertEventDocument> alertEvents,
    IRepository<RepositoryInstructionDocument> repositoryInstructions,
    IRepository<HarnessProviderSettingsDocument> harnessProviderSettings,
    IRepository<PromptSkillDocument> promptSkills,
    IRunArtifactStorage runArtifactStorage,
    LiteDbExecutor liteDbExecutor,
    LiteDbDatabase liteDbDatabase) : IOrchestratorStore, IAsyncDisposable
{
    private static readonly RunState[] ActiveStates = [RunState.Queued, RunState.Running, RunState.PendingApproval];
    private static readonly FindingState[] OpenFindingStates = [FindingState.New, FindingState.Acknowledged, FindingState.InProgress];
    private static readonly Regex PromptSkillTriggerRegex = new("^[a-z0-9-]+$", RegexOptions.Compiled);
    private const string GlobalRepositoryScope = "global";
    private static readonly string TaskWorkspacesRootPath = RepositoryPathResolver.GetDataPath("workspaces", "repos");

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task<RepositoryDocument> CreateRepositoryAsync(CreateRepositoryRequest request, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var repository = new RepositoryDocument
        {
            Name = request.Name,
            GitUrl = request.GitUrl,
            LocalPath = request.LocalPath,
            DefaultBranch = string.IsNullOrWhiteSpace(request.DefaultBranch) ? "main" : request.DefaultBranch,
        };

        db.Repositories.Add(repository);
        await db.SaveChangesAsync(cancellationToken);
        return repository;
    }

    public async Task<List<RepositoryDocument>> ListRepositoriesAsync(CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.Repositories.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);
    }

    public async Task<RepositoryDocument?> GetRepositoryAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.Repositories.AsNoTracking().FirstOrDefaultAsync(x => x.Id == repositoryId, cancellationToken);
    }

    public async Task<RepositoryDocument?> UpdateRepositoryAsync(string repositoryId, UpdateRepositoryRequest request, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var repository = await db.Repositories.FirstOrDefaultAsync(x => x.Id == repositoryId, cancellationToken);
        if (repository is null)
            return null;

        repository.Name = request.Name;
        repository.GitUrl = request.GitUrl;
        repository.LocalPath = request.LocalPath;
        repository.DefaultBranch = string.IsNullOrWhiteSpace(request.DefaultBranch) ? "main" : request.DefaultBranch;
        await db.SaveChangesAsync(cancellationToken);
        return repository;
    }


    public async Task<RepositoryDocument?> UpdateRepositoryGitStateAsync(string repositoryId, RepositoryGitStatus gitStatus, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var repository = await db.Repositories.FirstOrDefaultAsync(x => x.Id == repositoryId, cancellationToken);
        if (repository is null)
            return null;

        repository.CurrentBranch = gitStatus.CurrentBranch;
        repository.CurrentCommit = gitStatus.CurrentCommit;
        repository.AheadCount = gitStatus.AheadCount;
        repository.BehindCount = gitStatus.BehindCount;
        repository.ModifiedCount = gitStatus.ModifiedCount;
        repository.StagedCount = gitStatus.StagedCount;
        repository.UntrackedCount = gitStatus.UntrackedCount;
        repository.LastScannedAtUtc = gitStatus.ScannedAtUtc;
        repository.LastFetchedAtUtc = gitStatus.FetchedAtUtc;
        repository.LastSyncError = gitStatus.LastSyncError;

        await db.SaveChangesAsync(cancellationToken);
        return repository;
    }

    public async Task<RepositoryDocument?> TouchRepositoryAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var repository = await db.Repositories.FirstOrDefaultAsync(x => x.Id == repositoryId, cancellationToken);
        if (repository is null)
            return null;

        repository.LastViewedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return repository;
    }

    public async Task<bool> DeleteRepositoryAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var repository = await db.Repositories.FirstOrDefaultAsync(x => x.Id == repositoryId, cancellationToken);
        if (repository is null)
            return false;

        db.Repositories.Remove(repository);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<List<InstructionFile>> GetRepositoryInstructionFilesAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var repo = await db.Repositories.AsNoTracking().FirstOrDefaultAsync(x => x.Id == repositoryId, cancellationToken);
        return repo?.InstructionFiles ?? [];
    }

    public async Task<RepositoryDocument?> UpdateRepositoryInstructionFilesAsync(string repositoryId, List<InstructionFile> instructionFiles, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var repo = await db.Repositories.FirstOrDefaultAsync(x => x.Id == repositoryId, cancellationToken);
        if (repo is null)
            return null;

        repo.InstructionFiles = instructionFiles;
        await db.SaveChangesAsync(cancellationToken);
        return repo;
    }

    public async Task<List<RepositoryInstructionDocument>> GetInstructionsAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.RepositoryInstructions.AsNoTracking()
            .Where(x => x.RepositoryId == repositoryId)
            .OrderBy(x => x.Priority)
            .ToListAsync(cancellationToken);
    }

    public async Task<RepositoryInstructionDocument?> GetInstructionAsync(string instructionId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.RepositoryInstructions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == instructionId, cancellationToken);
    }

    public async Task<RepositoryInstructionDocument> UpsertInstructionAsync(string repositoryId, string? instructionId, CreateRepositoryInstructionRequest request, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var now = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(instructionId))
        {
            var existing = await db.RepositoryInstructions.FirstOrDefaultAsync(
                x => x.Id == instructionId && x.RepositoryId == repositoryId,
                cancellationToken);

            if (existing is not null)
            {
                existing.Name = request.Name;
                existing.Content = request.Content;
                existing.Priority = request.Priority;
                existing.Enabled = request.Enabled;
                existing.UpdatedAtUtc = now;
                await db.SaveChangesAsync(cancellationToken);
                return existing;
            }
        }

        var instruction = new RepositoryInstructionDocument
        {
            RepositoryId = repositoryId,
            Name = request.Name,
            Content = request.Content,
            Priority = request.Priority,
            Enabled = request.Enabled,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

        db.RepositoryInstructions.Add(instruction);
        await db.SaveChangesAsync(cancellationToken);
        return instruction;
    }

    public async Task<bool> DeleteInstructionAsync(string instructionId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var instruction = await db.RepositoryInstructions.FirstOrDefaultAsync(x => x.Id == instructionId, cancellationToken);
        if (instruction is null)
            return false;

        db.RepositoryInstructions.Remove(instruction);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<HarnessProviderSettingsDocument?> GetHarnessProviderSettingsAsync(string repositoryId, string harness, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.HarnessProviderSettings.AsNoTracking().FirstOrDefaultAsync(
            x => x.RepositoryId == repositoryId && x.Harness == harness,
            cancellationToken);
    }

    public async Task<HarnessProviderSettingsDocument> UpsertHarnessProviderSettingsAsync(
        string repositoryId,
        string harness,
        string model,
        double temperature,
        int maxTokens,
        Dictionary<string, string>? additionalSettings,
        CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var settings = await db.HarnessProviderSettings.FirstOrDefaultAsync(
            x => x.RepositoryId == repositoryId && x.Harness == harness,
            cancellationToken);

        if (settings is null)
        {
            settings = new HarnessProviderSettingsDocument
            {
                RepositoryId = repositoryId,
                Harness = harness,
            };
            db.HarnessProviderSettings.Add(settings);
        }

        settings.Model = model;
        settings.Temperature = temperature;
        settings.MaxTokens = maxTokens;
        settings.AdditionalSettings = additionalSettings ?? [];
        settings.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return settings;
    }

    public async Task<PromptSkillDocument> CreatePromptSkillAsync(CreatePromptSkillRequest request, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var repositoryId = NormalizePromptSkillScope(request.RepositoryId);
        var trigger = NormalizePromptSkillTrigger(request.Trigger);
        var name = NormalizeRequiredValue(request.Name, nameof(request.Name));
        var content = NormalizeRequiredValue(request.Content, nameof(request.Content));
        var description = request.Description?.Trim() ?? string.Empty;

        var exists = await db.PromptSkills.AnyAsync(
            x => x.RepositoryId == repositoryId && x.Trigger == trigger,
            cancellationToken);

        if (exists)
        {
            throw new InvalidOperationException($"A skill with trigger '/{trigger}' already exists in this scope.");
        }

        var now = DateTime.UtcNow;
        var skill = new PromptSkillDocument
        {
            RepositoryId = repositoryId,
            Name = name,
            Trigger = trigger,
            Content = content,
            Description = description,
            Enabled = request.Enabled,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.PromptSkills.Add(skill);
        await db.SaveChangesAsync(cancellationToken);
        return skill;
    }

    public async Task<List<PromptSkillDocument>> ListPromptSkillsAsync(string repositoryId, bool includeGlobal, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var scope = NormalizePromptSkillScope(repositoryId);

        IQueryable<PromptSkillDocument> query = db.PromptSkills.AsNoTracking();

        if (includeGlobal && !string.Equals(scope, GlobalRepositoryScope, StringComparison.Ordinal))
        {
            query = query.Where(x => x.RepositoryId == scope || x.RepositoryId == GlobalRepositoryScope);
            return await query
                .OrderBy(x => x.RepositoryId == scope ? 0 : 1)
                .ThenBy(x => x.Trigger)
                .ToListAsync(cancellationToken);
        }

        return await query
            .Where(x => x.RepositoryId == scope)
            .OrderBy(x => x.Trigger)
            .ToListAsync(cancellationToken);
    }

    public async Task<PromptSkillDocument?> GetPromptSkillAsync(string skillId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.PromptSkills.AsNoTracking().FirstOrDefaultAsync(x => x.Id == skillId, cancellationToken);
    }

    public async Task<PromptSkillDocument?> UpdatePromptSkillAsync(string skillId, UpdatePromptSkillRequest request, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var existing = await db.PromptSkills.FirstOrDefaultAsync(x => x.Id == skillId, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        var trigger = NormalizePromptSkillTrigger(request.Trigger);
        var name = NormalizeRequiredValue(request.Name, nameof(request.Name));
        var content = NormalizeRequiredValue(request.Content, nameof(request.Content));
        var description = request.Description?.Trim() ?? string.Empty;

        var duplicate = await db.PromptSkills.AnyAsync(
            x => x.Id != skillId && x.RepositoryId == existing.RepositoryId && x.Trigger == trigger,
            cancellationToken);

        if (duplicate)
        {
            throw new InvalidOperationException($"A skill with trigger '/{trigger}' already exists in this scope.");
        }

        existing.Name = name;
        existing.Trigger = trigger;
        existing.Content = content;
        existing.Description = description;
        existing.Enabled = request.Enabled;
        existing.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<bool> DeletePromptSkillAsync(string skillId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var existing = await db.PromptSkills.FirstOrDefaultAsync(x => x.Id == skillId, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        db.PromptSkills.Remove(existing);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<RunSessionProfileDocument> CreateRunSessionProfileAsync(CreateRunSessionProfileRequest request, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var repositoryId = NormalizeSessionProfileScope(request.RepositoryId, request.Scope);
        var name = NormalizeRequiredValue(request.Name, nameof(request.Name));
        var harness = NormalizeHarnessValue(request.Harness);

        var exists = await db.RunSessionProfiles.AnyAsync(
            x => x.RepositoryId == repositoryId && x.Name == name,
            cancellationToken);
        if (exists)
        {
            throw new InvalidOperationException($"A session profile named '{name}' already exists in this scope.");
        }

        var now = DateTime.UtcNow;
        var profile = new RunSessionProfileDocument
        {
            RepositoryId = repositoryId,
            Scope = request.Scope,
            Name = name,
            Harness = harness,
            ExecutionModeDefault = request.ExecutionModeDefault,
            ApprovalMode = request.ApprovalMode?.Trim() ?? "auto",
            DiffViewDefault = request.DiffViewDefault?.Trim() ?? "side-by-side",
            ToolTimelineMode = request.ToolTimelineMode?.Trim() ?? "table",
            McpConfigJson = request.McpConfigJson?.Trim() ?? string.Empty,
            Enabled = true,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.RunSessionProfiles.Add(profile);
        await db.SaveChangesAsync(cancellationToken);
        return profile;
    }

    public async Task<List<RunSessionProfileDocument>> ListRunSessionProfilesAsync(string repositoryId, bool includeGlobal, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var scope = NormalizePromptSkillScope(repositoryId);
        IQueryable<RunSessionProfileDocument> query = db.RunSessionProfiles.AsNoTracking();

        if (includeGlobal && !string.Equals(scope, GlobalRepositoryScope, StringComparison.Ordinal))
        {
            query = query.Where(x => x.RepositoryId == scope || x.RepositoryId == GlobalRepositoryScope);
            return await query
                .OrderBy(x => x.RepositoryId == scope ? 0 : 1)
                .ThenBy(x => x.Name)
                .ToListAsync(cancellationToken);
        }

        return await query
            .Where(x => x.RepositoryId == scope)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<RunSessionProfileDocument?> GetRunSessionProfileAsync(string sessionProfileId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.RunSessionProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sessionProfileId, cancellationToken);
    }

    public async Task<RunSessionProfileDocument?> UpdateRunSessionProfileAsync(string sessionProfileId, UpdateRunSessionProfileRequest request, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var existing = await db.RunSessionProfiles.FirstOrDefaultAsync(x => x.Id == sessionProfileId, cancellationToken);
        if (existing is null)
        {
            return null;
        }

        var name = NormalizeRequiredValue(request.Name, nameof(request.Name));
        var harness = NormalizeHarnessValue(request.Harness);

        var duplicate = await db.RunSessionProfiles.AnyAsync(
            x => x.Id != sessionProfileId && x.RepositoryId == existing.RepositoryId && x.Name == name,
            cancellationToken);
        if (duplicate)
        {
            throw new InvalidOperationException($"A session profile named '{name}' already exists in this scope.");
        }

        existing.Name = name;
        existing.Harness = harness;
        existing.ExecutionModeDefault = request.ExecutionModeDefault;
        existing.ApprovalMode = request.ApprovalMode?.Trim() ?? "auto";
        existing.DiffViewDefault = request.DiffViewDefault?.Trim() ?? "side-by-side";
        existing.ToolTimelineMode = request.ToolTimelineMode?.Trim() ?? "table";
        existing.McpConfigJson = request.McpConfigJson?.Trim() ?? string.Empty;
        existing.Enabled = request.Enabled;
        existing.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<bool> DeleteRunSessionProfileAsync(string sessionProfileId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var existing = await db.RunSessionProfiles.FirstOrDefaultAsync(x => x.Id == sessionProfileId, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        db.RunSessionProfiles.Remove(existing);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<AutomationDefinitionDocument> UpsertAutomationDefinitionAsync(
        string? automationId,
        UpsertAutomationDefinitionRequest request,
        CancellationToken cancellationToken)
    {
        var repositoryId = NormalizePromptSkillScope(request.RepositoryId);
        var taskId = NormalizeRequiredValue(request.TaskId, nameof(request.TaskId));
        var name = NormalizeRequiredValue(request.Name, nameof(request.Name));
        var triggerKind = NormalizeRequiredValue(request.TriggerKind, nameof(request.TriggerKind)).ToLowerInvariant();
        var replayPolicy = NormalizeRequiredValue(request.ReplayPolicy, nameof(request.ReplayPolicy)).ToLowerInvariant();
        var cronExpression = request.CronExpression?.Trim() ?? string.Empty;

        await using var db = CreateSession();
        AutomationDefinitionDocument automation;

        if (string.IsNullOrWhiteSpace(automationId))
        {
            automation = new AutomationDefinitionDocument
            {
                RepositoryId = repositoryId,
                TaskId = taskId,
                Name = name,
                TriggerKind = triggerKind,
                ReplayPolicy = replayPolicy,
                CronExpression = cronExpression,
                Enabled = request.Enabled,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                NextRunAtUtc = ComputeNextAutomationRun(triggerKind, cronExpression, request.Enabled, DateTime.UtcNow),
            };
            db.AutomationDefinitions.Add(automation);
        }
        else
        {
            automation = await db.AutomationDefinitions.FirstOrDefaultAsync(x => x.Id == automationId, cancellationToken)
                ?? throw new InvalidOperationException("Automation definition not found.");
            automation.RepositoryId = repositoryId;
            automation.TaskId = taskId;
            automation.Name = name;
            automation.TriggerKind = triggerKind;
            automation.ReplayPolicy = replayPolicy;
            automation.CronExpression = cronExpression;
            automation.Enabled = request.Enabled;
            automation.NextRunAtUtc = ComputeNextAutomationRun(triggerKind, cronExpression, request.Enabled, DateTime.UtcNow);
            automation.UpdatedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
        return automation;
    }

    public async Task<List<AutomationDefinitionDocument>> ListAutomationDefinitionsAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var normalizedRepositoryId = NormalizePromptSkillScope(repositoryId);
        return await db.AutomationDefinitions.AsNoTracking()
            .Where(x => x.RepositoryId == normalizedRepositoryId)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<AutomationDefinitionDocument?> GetAutomationDefinitionAsync(string automationId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.AutomationDefinitions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == automationId, cancellationToken);
    }

    public async Task<bool> DeleteAutomationDefinitionAsync(string automationId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var existing = await db.AutomationDefinitions.FirstOrDefaultAsync(x => x.Id == automationId, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        db.AutomationDefinitions.Remove(existing);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<AutomationExecutionDocument> CreateAutomationExecutionAsync(AutomationExecutionDocument execution, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        if (string.IsNullOrWhiteSpace(execution.Id))
        {
            execution.Id = Guid.NewGuid().ToString("N");
        }

        execution.StartedAtUtc = execution.StartedAtUtc == default ? DateTime.UtcNow : execution.StartedAtUtc;
        db.AutomationExecutions.Add(execution);
        await db.SaveChangesAsync(cancellationToken);
        return execution;
    }

    public async Task<List<AutomationExecutionDocument>> ListAutomationExecutionsAsync(string repositoryId, int limit, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var normalizedRepositoryId = NormalizePromptSkillScope(repositoryId);
        var normalizedLimit = limit <= 0 ? 100 : Math.Clamp(limit, 1, 1000);
        return await db.AutomationExecutions.AsNoTracking()
            .Where(x => x.RepositoryId == normalizedRepositoryId)
            .OrderByDescending(x => x.StartedAtUtc)
            .Take(normalizedLimit)
            .ToListAsync(cancellationToken);
    }

    public async Task<TaskDocument> CreateTaskAsync(CreateTaskRequest request, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var task = new TaskDocument
        {
            RepositoryId = request.RepositoryId,
            Name = request.Name,
            Kind = request.Kind,
            Harness = request.Harness.Trim().ToLowerInvariant(),
            ExecutionModeDefault = request.ExecutionModeDefault,
            SessionProfileId = request.SessionProfileId?.Trim() ?? string.Empty,
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
            ArtifactPatterns = request.ArtifactPatterns ?? [],
            LinkedFailureRuns = request.LinkedFailureRuns ?? [],
        };

        task.NextRunAtUtc = ComputeNextRun(task, DateTime.UtcNow);
        db.Tasks.Add(task);
        await db.SaveChangesAsync(cancellationToken);
        return task;
    }

    public async Task<List<TaskDocument>> ListTasksAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.Tasks.AsNoTracking().Where(x => x.RepositoryId == repositoryId).OrderBy(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task<List<TaskDocument>> ListEventDrivenTasksAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.Tasks.AsNoTracking()
            .Where(x => x.RepositoryId == repositoryId && x.Enabled && x.Kind == TaskKind.EventDriven)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<TaskDocument?> GetTaskAsync(string taskId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.Tasks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == taskId, cancellationToken);
    }

    public async Task<List<TaskDocument>> ListScheduledTasksAsync(CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.Tasks.AsNoTracking()
            .Where(x => x.Enabled && x.Kind == TaskKind.Cron)
            .OrderBy(x => x.NextRunAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<TaskDocument>> ListDueTasksAsync(DateTime utcNow, int limit, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.Tasks.AsNoTracking()
            .Where(x => x.Enabled && (x.Kind == TaskKind.OneShot || (x.Kind == TaskKind.Cron && x.NextRunAtUtc != null && x.NextRunAtUtc <= utcNow)))
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task MarkOneShotTaskConsumedAsync(string taskId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var task = await db.Tasks.FirstOrDefaultAsync(x => x.Id == taskId, cancellationToken);
        if (task is null)
            return;

        task.Enabled = false;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateTaskNextRunAsync(string taskId, DateTime? nextRunAtUtc, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var task = await db.Tasks.FirstOrDefaultAsync(x => x.Id == taskId, cancellationToken);
        if (task is null)
            return;

        task.NextRunAtUtc = nextRunAtUtc;
        await db.SaveChangesAsync(cancellationToken);
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

        await using var db = CreateSession();
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
        await using var db = CreateSession();
        var task = await db.Tasks.FirstOrDefaultAsync(x => x.Id == taskId, cancellationToken);
        if (task is null)
            return null;

        task.Name = request.Name;
        task.Kind = request.Kind;
        task.Harness = request.Harness.Trim().ToLowerInvariant();
        task.ExecutionModeDefault = request.ExecutionModeDefault;
        task.SessionProfileId = request.SessionProfileId?.Trim() ?? string.Empty;
        task.Prompt = request.Prompt;
        task.Command = request.Command;
        task.AutoCreatePullRequest = request.AutoCreatePullRequest;
        task.CronExpression = request.CronExpression;
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
        await using var db = CreateSession();
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

        await using var db = CreateSession();

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
        var workflowReferencedTaskIds = new HashSet<string>(StringComparer.Ordinal);
        if (query.ExcludeWorkflowReferencedTasks)
        {
            var repositoryIds = taskSeeds
                .Select(x => x.RepositoryId)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (repositoryIds.Count > 0)
            {
                var workflowStages = await db.Workflows.AsNoTracking()
                    .Where(x => repositoryIds.Contains(x.RepositoryId))
                    .Select(x => x.Stages)
                    .ToListAsync(cancellationToken);

                foreach (var stages in workflowStages)
                {
                    if (stages is null || stages.Count == 0)
                    {
                        continue;
                    }

                    foreach (var stage in stages)
                    {
                        if (!string.IsNullOrWhiteSpace(stage.TaskId))
                        {
                            workflowReferencedTaskIds.Add(stage.TaskId);
                        }
                    }
                }
            }
        }

        var tasksWithOpenFindings = new HashSet<string>(StringComparer.Ordinal);
        if (query.ExcludeTasksWithOpenFindings)
        {
            var taskIdsWithOpenFindings = await (
                    from finding in db.Findings.AsNoTracking()
                    join run in db.Runs.AsNoTracking() on finding.RunId equals run.Id
                    where taskIds.Contains(run.TaskId) && OpenFindingStates.Contains(finding.State)
                    select run.TaskId)
                .Distinct()
                .ToListAsync(cancellationToken);
            tasksWithOpenFindings = new HashSet<string>(taskIdsWithOpenFindings, StringComparer.Ordinal);
        }

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

            var isWorkflowReferenced = workflowReferencedTaskIds.Contains(task.TaskId);
            if (query.ExcludeWorkflowReferencedTasks && isWorkflowReferenced)
            {
                continue;
            }

            var hasOpenFindings = tasksWithOpenFindings.Contains(task.TaskId);
            if (query.ExcludeTasksWithOpenFindings && hasOpenFindings)
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
                isDisabledInactiveEligible,
                isWorkflowReferenced,
                hasOpenFindings));
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
                DeletedFindings: 0,
                DeletedPromptEntries: 0,
                DeletedRunSummaries: 0,
                DeletedSemanticChunks: 0,
                DeletedArtifactDirectories: 0,
                ArtifactDeleteErrors: 0,
                DeletedTaskWorkspaceDirectories: 0,
                TaskWorkspaceDeleteErrors: 0);
        }

        await using var db = CreateSession();
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
                DeletedFindings: 0,
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
        var deletedFindings = 0;
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
            deletedFindings = await db.Findings.DeleteWhereAsync(x => runIds.Contains(x.RunId), cancellationToken);
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
            DeletedFindings: deletedFindings,
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
                DeletedFindings: 0,
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
        var deletedFindings = 0;
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
                deletedFindings += result.DeletedFindings;
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
            DeletedFindings: deletedFindings,
            DeletedPromptEntries: deletedPromptEntries,
            DeletedRunSummaries: deletedRunSummaries,
            DeletedSemanticChunks: deletedSemanticChunks,
            DeletedArtifactDirectories: deletedArtifactDirectories,
            ArtifactDeleteErrors: artifactDeleteErrors,
            DeletedTaskWorkspaceDirectories: deletedTaskWorkspaceDirectories,
            TaskWorkspaceDeleteErrors: taskWorkspaceDeleteErrors);
    }

    public async Task VacuumAsync(CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        await db.Database.ExecuteSqlRawAsync("VACUUM;", cancellationToken);
    }

    public async Task<RunDocument> CreateRunAsync(
        TaskDocument task,
        CancellationToken cancellationToken,
        int attempt = 1,
        HarnessExecutionMode? executionModeOverride = null,
        string? sessionProfileId = null,
        string? automationRunId = null)
    {
        await using var db = CreateSession();
        var run = new RunDocument
        {
            RepositoryId = task.RepositoryId,
            TaskId = task.Id,
            State = RunState.Queued,
            ExecutionMode = executionModeOverride ?? task.ExecutionModeDefault ?? HarnessExecutionMode.Default,
            StructuredProtocol = "harness-structured-event-v2",
            SessionProfileId = sessionProfileId?.Trim() ?? task.SessionProfileId,
            AutomationRunId = automationRunId?.Trim() ?? string.Empty,
            Summary = "Queued",
            Attempt = attempt,
        };

        db.Runs.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        return run;
    }

    public async Task<List<RunDocument>> ListRunsByRepositoryAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.Runs.AsNoTracking().Where(x => x.RepositoryId == repositoryId).OrderByDescending(x => x.CreatedAtUtc).Take(200).ToListAsync(cancellationToken);
    }

    public async Task<List<RunDocument>> ListRecentRunsAsync(CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.Runs.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(100).ToListAsync(cancellationToken);
    }

    public async Task<List<RepositoryDocument>> ListRepositoriesWithRecentTasksAsync(int limit, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
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

        await using var db = CreateSession();
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

        await using var db = CreateSession();
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

        await using var db = CreateSession();
        var normalizedLimit = Math.Clamp(limit, 1, 1000);

        return await db.WorkspacePromptEntries.AsNoTracking()
            .Where(x => x.TaskId == taskId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(normalizedLimit)
            .ToListAsync(cancellationToken);
    }

    public async Task<WorkspacePromptEntryDocument> AppendWorkspacePromptEntryAsync(WorkspacePromptEntryDocument promptEntry, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(promptEntry.TaskId))
        {
            throw new ArgumentException("TaskId is required.", nameof(promptEntry));
        }

        await using var db = CreateSession();

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

    public async Task<RunAiSummaryDocument> UpsertRunAiSummaryAsync(RunAiSummaryDocument summary, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(summary.RunId))
        {
            throw new ArgumentException("RunId is required.", nameof(summary));
        }

        await using var db = CreateSession();
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
        await using var db = CreateSession();
        return await db.RunAiSummaries.AsNoTracking()
            .FirstOrDefaultAsync(x => x.RunId == runId, cancellationToken);
    }

    public async Task<ReliabilityMetrics> GetReliabilityMetricsByRepositoryAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
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
        await using var db = CreateSession();
        return await db.Runs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == runId, cancellationToken);
    }

    public async Task<List<RunDocument>> ListRunsByStateAsync(RunState state, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.Runs.AsNoTracking().Where(x => x.State == state).OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task<List<string>> ListAllRunIdsAsync(CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.Runs.AsNoTracking().Select(x => x.Id).ToListAsync(cancellationToken);
    }

    public async Task<long> CountRunsByStateAsync(RunState state, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.Runs.LongCountAsync(x => x.State == state, cancellationToken);
    }

    public async Task<long> CountActiveRunsAsync(CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.Runs.LongCountAsync(x => ActiveStates.Contains(x.State), cancellationToken);
    }

    public async Task<long> CountActiveRunsByRepoAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.Runs.LongCountAsync(x => x.RepositoryId == repositoryId && ActiveStates.Contains(x.State), cancellationToken);
    }

    public async Task<long> CountActiveRunsByTaskAsync(string taskId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
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
        await using var db = CreateSession();
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
        await using var db = CreateSession();
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
        await using var db = CreateSession();
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
        await using var db = CreateSession();
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

    public async Task<RunDocument?> MarkRunPendingApprovalAsync(string runId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
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
        await using var db = CreateSession();
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
        await using var db = CreateSession();
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

        await using var db = CreateSession();
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
        await runArtifactStorage.SaveAsync(runId, normalizedFileName, stream, cancellationToken);
    }

    public Task<List<string>> ListArtifactsAsync(string runId, CancellationToken cancellationToken)
    {
        return runArtifactStorage.ListAsync(runId, cancellationToken);
    }

    public async Task<Stream?> GetArtifactAsync(string runId, string fileName, CancellationToken cancellationToken)
    {
        var normalizedFileName = NormalizeArtifactFileName(fileName);
        return await runArtifactStorage.GetAsync(runId, normalizedFileName, cancellationToken);
    }

    public async Task AddRunLogAsync(RunLogEvent logEvent, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        db.RunEvents.Add(logEvent);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<RunLogEvent>> ListRunLogsAsync(string runId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.RunEvents.AsNoTracking().Where(x => x.RunId == runId).OrderBy(x => x.TimestampUtc).ToListAsync(cancellationToken);
    }

    public async Task<RunStructuredEventDocument> AppendRunStructuredEventAsync(RunStructuredEventDocument structuredEvent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(structuredEvent.RunId))
        {
            throw new ArgumentException("RunId is required.", nameof(structuredEvent));
        }

        await using var db = CreateSession();
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

        if (string.IsNullOrWhiteSpace(structuredEvent.RepositoryId) || string.IsNullOrWhiteSpace(structuredEvent.TaskId))
        {
            var runMetadata = await db.Runs.AsNoTracking()
                .Where(x => x.Id == structuredEvent.RunId)
                .Select(x => new
                {
                    x.RepositoryId,
                    x.TaskId
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

        await db.SaveChangesAsync(cancellationToken);
        return stored;
    }

    public async Task<List<RunStructuredEventDocument>> ListRunStructuredEventsAsync(string runId, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return [];
        }

        await using var db = CreateSession();
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

        await using var db = CreateSession();
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

        await using var db = CreateSession();
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

        await using var db = CreateSession();
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

        await using var db = CreateSession();
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

        await using var db = CreateSession();
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

        await using var db = CreateSession();
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

        await using var db = CreateSession();
        return await db.RunShareBundles.AsNoTracking()
            .Where(x => x.RunId == runId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<StructuredRunDataPruneResult> PruneStructuredRunDataAsync(
        DateTime olderThanUtc,
        int maxRuns,
        bool excludeWorkflowReferencedTasks,
        bool excludeTasksWithOpenFindings,
        CancellationToken cancellationToken)
    {
        var normalizedMaxRuns = Math.Clamp(maxRuns, 1, 5000);
        var scanLimit = Math.Clamp(normalizedMaxRuns * 5, normalizedMaxRuns, 20_000);

        await using var db = CreateSession();
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
        if (excludeWorkflowReferencedTasks || excludeTasksWithOpenFindings)
        {
            var taskIds = candidateRunSeeds
                .Select(x => x.TaskId)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (taskIds.Count > 0)
            {
                var excludedTaskIds = new HashSet<string>(StringComparer.Ordinal);
                if (excludeWorkflowReferencedTasks)
                {
                    var repositoryIds = candidateRunSeeds
                        .Select(x => x.RepositoryId)
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.Ordinal)
                        .ToList();

                    if (repositoryIds.Count > 0)
                    {
                        var workflowStages = await db.Workflows.AsNoTracking()
                            .Where(x => repositoryIds.Contains(x.RepositoryId))
                            .Select(x => x.Stages)
                            .ToListAsync(cancellationToken);

                        foreach (var stages in workflowStages)
                        {
                            if (stages is null || stages.Count == 0)
                            {
                                continue;
                            }

                            foreach (var stage in stages)
                            {
                                if (!string.IsNullOrWhiteSpace(stage.TaskId))
                                {
                                    excludedTaskIds.Add(stage.TaskId);
                                }
                            }
                        }
                    }
                }

                if (excludeTasksWithOpenFindings)
                {
                    var taskIdsWithOpenFindings = await (
                            from finding in db.Findings.AsNoTracking()
                            join run in db.Runs.AsNoTracking() on finding.RunId equals run.Id
                            where taskIds.Contains(run.TaskId) && OpenFindingStates.Contains(finding.State)
                            select run.TaskId)
                        .Distinct()
                        .ToListAsync(cancellationToken);

                    foreach (var taskId in taskIdsWithOpenFindings)
                    {
                        if (!string.IsNullOrWhiteSpace(taskId))
                        {
                            excludedTaskIds.Add(taskId);
                        }
                    }
                }

                if (excludedTaskIds.Count > 0)
                {
                    candidateRunSeeds = candidateRunSeeds
                        .Where(x => !excludedTaskIds.Contains(x.TaskId))
                        .ToList();
                }
            }
        }

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
        await db.SaveChangesAsync(cancellationToken);

        return new StructuredRunDataPruneResult(
            RunsScanned: runIds.Count,
            DeletedStructuredEvents: deletedStructuredEvents,
            DeletedDiffSnapshots: deletedDiffSnapshots,
            DeletedToolProjections: deletedToolProjections);
    }

    public async Task<List<FindingDocument>> ListFindingsAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.Findings.AsNoTracking().Where(x => x.RepositoryId == repositoryId).OrderByDescending(x => x.CreatedAtUtc).Take(200).ToListAsync(cancellationToken);
    }

    public async Task<List<FindingDocument>> ListAllFindingsAsync(CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.Findings.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(500).ToListAsync(cancellationToken);
    }

    public async Task<FindingDocument?> GetFindingAsync(string findingId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.Findings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == findingId, cancellationToken);
    }

    public async Task<FindingDocument> CreateFindingFromFailureAsync(RunDocument run, string description, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var shortRun = run.Id.Length >= 8 ? run.Id[..8] : run.Id;
        var finding = new FindingDocument
        {
            RepositoryId = run.RepositoryId,
            RunId = run.Id,
            Title = $"Run {shortRun} failed",
            Description = description,
            Severity = FindingSeverity.High,
            State = FindingState.New,
        };

        db.Findings.Add(finding);
        await db.SaveChangesAsync(cancellationToken);
        return finding;
    }

    public async Task<FindingDocument?> UpdateFindingStateAsync(string findingId, FindingState state, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var finding = await db.Findings.FirstOrDefaultAsync(x => x.Id == findingId, cancellationToken);
        if (finding is null)
            return null;

        finding.State = state;
        await db.SaveChangesAsync(cancellationToken);
        return finding;
    }

    public async Task<FindingDocument?> AssignFindingAsync(string findingId, string assignedTo, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var finding = await db.Findings.FirstOrDefaultAsync(x => x.Id == findingId, cancellationToken);
        if (finding is null)
            return null;

        finding.AssignedTo = assignedTo;
        finding.State = FindingState.InProgress;
        await db.SaveChangesAsync(cancellationToken);
        return finding;
    }

    public async Task<bool> DeleteFindingAsync(string findingId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var finding = await db.Findings.FirstOrDefaultAsync(x => x.Id == findingId, cancellationToken);
        if (finding is null)
            return false;

        db.Findings.Remove(finding);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task UpsertProviderSecretAsync(string repositoryId, string provider, string encryptedValue, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var secret = await db.ProviderSecrets.FirstOrDefaultAsync(x => x.RepositoryId == repositoryId && x.Provider == provider, cancellationToken);
        if (secret is null)
        {
            secret = new ProviderSecretDocument
            {
                RepositoryId = repositoryId,
                Provider = provider,
            };
            db.ProviderSecrets.Add(secret);
        }

        secret.EncryptedValue = encryptedValue;
        secret.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<ProviderSecretDocument>> ListProviderSecretsAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.ProviderSecrets.AsNoTracking().Where(x => x.RepositoryId == repositoryId).ToListAsync(cancellationToken);
    }

    public async Task<ProviderSecretDocument?> GetProviderSecretAsync(string repositoryId, string provider, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.ProviderSecrets.AsNoTracking().FirstOrDefaultAsync(x => x.RepositoryId == repositoryId && x.Provider == provider, cancellationToken);
    }

    public async Task<bool> DeleteProviderSecretAsync(string repositoryId, string provider, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var secret = await db.ProviderSecrets.FirstOrDefaultAsync(x => x.RepositoryId == repositoryId && x.Provider == provider, cancellationToken);
        if (secret is null)
            return false;

        db.ProviderSecrets.Remove(secret);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<List<TaskRuntimeRegistration>> ListTaskRuntimeRegistrationsAsync(CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.TaskRuntimeRegistrations.AsNoTracking().OrderBy(x => x.RuntimeId).ToListAsync(cancellationToken);
    }

    public async Task UpsertTaskRuntimeRegistrationHeartbeatAsync(string runtimeId, string endpoint, int activeSlots, int maxSlots, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var registration = await db.TaskRuntimeRegistrations.FirstOrDefaultAsync(x => x.RuntimeId == runtimeId, cancellationToken);
        if (registration is null)
        {
            registration = new TaskRuntimeRegistration
            {
                RuntimeId = runtimeId,
                RegisteredAtUtc = DateTime.UtcNow,
            };
            db.TaskRuntimeRegistrations.Add(registration);
        }

        registration.Endpoint = endpoint;
        registration.ActiveSlots = Math.Max(0, activeSlots);
        registration.MaxSlots = maxSlots > 0 ? maxSlots : Math.Max(1, registration.MaxSlots);
        registration.Online = true;
        registration.LastHeartbeatUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkStaleTaskRuntimeRegistrationsOfflineAsync(TimeSpan threshold, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var cutoff = DateTime.UtcNow - threshold;
        var stale = await db.TaskRuntimeRegistrations
            .Where(x => x.Online && x.LastHeartbeatUtc < cutoff)
            .ToListAsync(cancellationToken);
        if (stale.Count == 0)
        {
            return;
        }

        foreach (var registration in stale)
        {
            registration.Online = false;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<TaskRuntimeDocument>> ListTaskRuntimesAsync(CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.TaskRuntimes.AsNoTracking().OrderBy(x => x.RuntimeId).ToListAsync(cancellationToken);
    }

    public async Task<TaskRuntimeDocument> UpsertTaskRuntimeStateAsync(TaskRuntimeStateUpdate update, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(update.RuntimeId))
        {
            throw new InvalidOperationException("RuntimeId is required.");
        }

        await using var db = CreateSession();
        var now = update.ObservedAtUtc == default ? DateTime.UtcNow : update.ObservedAtUtc;
        var runtime = await db.TaskRuntimes.FirstOrDefaultAsync(x => x.RuntimeId == update.RuntimeId, cancellationToken);
        if (runtime is null)
        {
            runtime = new TaskRuntimeDocument
            {
                RuntimeId = update.RuntimeId,
                RepositoryId = update.RepositoryId,
                TaskId = update.TaskId,
                LastActivityUtc = now,
                LastStateChangeUtc = now,
            };
            db.TaskRuntimes.Add(runtime);
        }

        var previousState = runtime.State;
        var previousLastActivityUtc = runtime.LastActivityUtc;

        runtime.RepositoryId = string.IsNullOrWhiteSpace(update.RepositoryId) ? runtime.RepositoryId : update.RepositoryId;
        runtime.TaskId = string.IsNullOrWhiteSpace(update.TaskId) ? runtime.TaskId : update.TaskId;
        runtime.State = update.State;
        runtime.ActiveRuns = Math.Max(0, update.ActiveRuns);
        runtime.MaxParallelRuns = update.MaxParallelRuns > 0 ? update.MaxParallelRuns : Math.Max(1, runtime.MaxParallelRuns);
        runtime.Endpoint = string.IsNullOrWhiteSpace(update.Endpoint) ? runtime.Endpoint : update.Endpoint;
        runtime.ContainerId = string.IsNullOrWhiteSpace(update.ContainerId) ? runtime.ContainerId : update.ContainerId;
        runtime.WorkspacePath = string.IsNullOrWhiteSpace(update.WorkspacePath) ? runtime.WorkspacePath : update.WorkspacePath;
        runtime.RuntimeHomePath = string.IsNullOrWhiteSpace(update.RuntimeHomePath) ? runtime.RuntimeHomePath : update.RuntimeHomePath;
        if (previousState != runtime.State || runtime.LastStateChangeUtc is null)
        {
            runtime.LastStateChangeUtc = now;
        }

        if (update.UpdateLastActivityUtc)
        {
            runtime.LastActivityUtc = now;
        }

        if (!string.IsNullOrWhiteSpace(update.LastError))
        {
            runtime.LastError = update.LastError.Trim();
        }
        else if (runtime.State != TaskRuntimeState.Failed)
        {
            runtime.LastError = string.Empty;
        }

        if (update.ClearInactiveAfterUtc)
        {
            runtime.InactiveAfterUtc = null;
        }
        else if (update.InactiveAfterUtc.HasValue)
        {
            runtime.InactiveAfterUtc = update.InactiveAfterUtc.Value;
        }
        else if (runtime.State == TaskRuntimeState.Inactive)
        {
            runtime.InactiveAfterUtc = now;
        }

        if (runtime.State == TaskRuntimeState.Starting)
        {
            runtime.LastStartedAtUtc = now;
        }

        if (runtime.State == TaskRuntimeState.Ready)
        {
            runtime.LastReadyAtUtc = now;
            if (previousState is TaskRuntimeState.Cold or TaskRuntimeState.Starting &&
                runtime.LastStartedAtUtc.HasValue &&
                now >= runtime.LastStartedAtUtc.Value)
            {
                var durationMs = Math.Max(0L, (long)(now - runtime.LastStartedAtUtc.Value).TotalMilliseconds);
                runtime.ColdStartCount++;
                runtime.ColdStartDurationTotalMs += durationMs;
                runtime.LastColdStartDurationMs = durationMs;
            }
        }

        if (runtime.State == TaskRuntimeState.Inactive &&
            previousState != TaskRuntimeState.Inactive &&
            previousLastActivityUtc != default &&
            now >= previousLastActivityUtc)
        {
            var inactiveDurationMs = Math.Max(0L, (long)(now - previousLastActivityUtc).TotalMilliseconds);
            runtime.LastBecameInactiveUtc = now;
            runtime.InactiveTransitionCount++;
            runtime.InactiveDurationTotalMs += inactiveDurationMs;
            runtime.LastInactiveDurationMs = inactiveDurationMs;
        }

        await db.SaveChangesAsync(cancellationToken);
        return runtime;
    }

    public async Task<TaskRuntimeTelemetrySnapshot> GetTaskRuntimeTelemetryAsync(CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var runtimes = await db.TaskRuntimes.AsNoTracking().ToListAsync(cancellationToken);
        if (runtimes.Count == 0)
        {
            return new TaskRuntimeTelemetrySnapshot(
                TotalRuntimes: 0,
                ReadyRuntimes: 0,
                BusyRuntimes: 0,
                InactiveRuntimes: 0,
                FailedRuntimes: 0,
                TotalColdStarts: 0,
                AverageColdStartSeconds: 0,
                LastColdStartSeconds: 0,
                TotalInactiveTransitions: 0,
                AverageInactiveSeconds: 0,
                LastInactiveSeconds: 0);
        }

        var totalColdStarts = runtimes.Sum(x => x.ColdStartCount);
        var totalColdStartDurationMs = runtimes.Sum(x => x.ColdStartDurationTotalMs);
        var totalInactiveTransitions = runtimes.Sum(x => x.InactiveTransitionCount);
        var totalInactiveDurationMs = runtimes.Sum(x => x.InactiveDurationTotalMs);
        var lastColdStartSeconds = runtimes.Where(x => x.LastColdStartDurationMs > 0)
            .Select(x => x.LastColdStartDurationMs / 1000d)
            .DefaultIfEmpty(0)
            .Average();
        var lastInactiveSeconds = runtimes.Where(x => x.LastInactiveDurationMs > 0)
            .Select(x => x.LastInactiveDurationMs / 1000d)
            .DefaultIfEmpty(0)
            .Average();

        return new TaskRuntimeTelemetrySnapshot(
            TotalRuntimes: runtimes.Count,
            ReadyRuntimes: runtimes.Count(x => x.State == TaskRuntimeState.Ready),
            BusyRuntimes: runtimes.Count(x => x.State == TaskRuntimeState.Busy),
            InactiveRuntimes: runtimes.Count(x => x.State == TaskRuntimeState.Inactive),
            FailedRuntimes: runtimes.Count(x => x.State == TaskRuntimeState.Failed),
            TotalColdStarts: totalColdStarts,
            AverageColdStartSeconds: totalColdStarts > 0 ? totalColdStartDurationMs / 1000d / totalColdStarts : 0,
            LastColdStartSeconds: lastColdStartSeconds,
            TotalInactiveTransitions: totalInactiveTransitions,
            AverageInactiveSeconds: totalInactiveTransitions > 0 ? totalInactiveDurationMs / 1000d / totalInactiveTransitions : 0,
            LastInactiveSeconds: lastInactiveSeconds);
    }

    public async Task<WebhookRegistration> CreateWebhookAsync(CreateWebhookRequest request, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var webhook = new WebhookRegistration
        {
            RepositoryId = request.RepositoryId,
            TaskId = request.TaskId,
            EventFilter = request.EventFilter,
            Secret = request.Secret,
        };

        db.Webhooks.Add(webhook);
        await db.SaveChangesAsync(cancellationToken);
        return webhook;
    }

    public async Task<WebhookRegistration?> GetWebhookAsync(string webhookId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.Webhooks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == webhookId, cancellationToken);
    }

    public async Task<List<WebhookRegistration>> ListWebhooksAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.Webhooks.AsNoTracking().Where(x => x.RepositoryId == repositoryId).ToListAsync(cancellationToken);
    }

    public async Task<WebhookRegistration?> UpdateWebhookAsync(string webhookId, UpdateWebhookRequest request, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var webhook = await db.Webhooks.FirstOrDefaultAsync(x => x.Id == webhookId, cancellationToken);
        if (webhook is null)
            return null;

        webhook.TaskId = request.TaskId;
        webhook.EventFilter = request.EventFilter;
        webhook.Secret = request.Secret;
        webhook.Enabled = request.Enabled;
        await db.SaveChangesAsync(cancellationToken);
        return webhook;
    }

    public async Task<bool> DeleteWebhookAsync(string webhookId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var webhook = await db.Webhooks.FirstOrDefaultAsync(x => x.Id == webhookId, cancellationToken);
        if (webhook is null)
            return false;

        db.Webhooks.Remove(webhook);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<SystemSettingsDocument> GetSettingsAsync(CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var settings = await db.Settings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == "singleton", cancellationToken);
        return settings ?? new SystemSettingsDocument();
    }

    public async Task<SystemSettingsDocument> UpdateSettingsAsync(SystemSettingsDocument settings, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        settings.Id = "singleton";
        settings.UpdatedAtUtc = DateTime.UtcNow;

        var existing = await db.Settings.FirstOrDefaultAsync(x => x.Id == "singleton", cancellationToken);
        if (existing is null)
        {
            db.Settings.Add(settings);
            await db.SaveChangesAsync(cancellationToken);
            return settings;
        }

        db.Entry(existing).CurrentValues.SetValues(settings);
        await db.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<bool> TryAcquireLeaseAsync(string leaseName, string ownerId, TimeSpan ttl, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var now = DateTime.UtcNow;
        var expiresAtUtc = now.Add(ttl);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var lease = await db.Leases.FirstOrDefaultAsync(x => x.LeaseName == leaseName, cancellationToken);
        if (lease is null)
        {
            db.Leases.Add(new OrchestratorLeaseDocument
            {
                LeaseName = leaseName,
                OwnerId = ownerId,
                ExpiresAtUtc = expiresAtUtc,
            });

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        if (string.Equals(lease.OwnerId, ownerId, StringComparison.OrdinalIgnoreCase) || lease.ExpiresAtUtc <= now)
        {
            lease.OwnerId = ownerId;
            lease.ExpiresAtUtc = expiresAtUtc;
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        await transaction.RollbackAsync(cancellationToken);
        return false;
    }

    public async Task<bool> RenewLeaseAsync(string leaseName, string ownerId, TimeSpan ttl, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var lease = await db.Leases.FirstOrDefaultAsync(
            x => x.LeaseName == leaseName && x.OwnerId == ownerId,
            cancellationToken);

        if (lease is null)
        {
            return false;
        }

        lease.ExpiresAtUtc = DateTime.UtcNow.Add(ttl);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task ReleaseLeaseAsync(string leaseName, string ownerId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var lease = await db.Leases.FirstOrDefaultAsync(
            x => x.LeaseName == leaseName && x.OwnerId == ownerId,
            cancellationToken);

        if (lease is null)
        {
            return;
        }

        db.Leases.Remove(lease);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<WorkflowDocument> CreateWorkflowAsync(WorkflowDocument workflow, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync(cancellationToken);
        return workflow;
    }

    public async Task<List<WorkflowDocument>> ListWorkflowsByRepositoryAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.Workflows.AsNoTracking().Where(x => x.RepositoryId == repositoryId).OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task<List<WorkflowDocument>> ListAllWorkflowsAsync(CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.Workflows.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task<WorkflowDocument?> GetWorkflowAsync(string workflowId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.Workflows.AsNoTracking().FirstOrDefaultAsync(x => x.Id == workflowId, cancellationToken);
    }

    public async Task<WorkflowDocument?> UpdateWorkflowAsync(string workflowId, WorkflowDocument workflow, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var existing = await db.Workflows.FirstOrDefaultAsync(x => x.Id == workflowId, cancellationToken);
        if (existing is null)
            return null;

        workflow.Id = existing.Id;
        db.Entry(existing).CurrentValues.SetValues(workflow);
        await db.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<bool> DeleteWorkflowAsync(string workflowId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var workflow = await db.Workflows.FirstOrDefaultAsync(x => x.Id == workflowId, cancellationToken);
        if (workflow is null)
            return false;

        db.Workflows.Remove(workflow);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<WorkflowExecutionDocument> CreateWorkflowExecutionAsync(WorkflowExecutionDocument execution, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        db.WorkflowExecutions.Add(execution);
        await db.SaveChangesAsync(cancellationToken);
        return execution;
    }

    public async Task<List<WorkflowExecutionDocument>> ListWorkflowExecutionsAsync(string workflowId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.WorkflowExecutions.AsNoTracking()
            .Where(x => x.WorkflowId == workflowId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<WorkflowExecutionDocument>> ListWorkflowExecutionsByStateAsync(WorkflowExecutionState state, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.WorkflowExecutions.AsNoTracking().Where(x => x.State == state).OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task<WorkflowExecutionDocument?> GetWorkflowExecutionAsync(string executionId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.WorkflowExecutions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == executionId, cancellationToken);
    }

    public async Task<WorkflowExecutionDocument?> UpdateWorkflowExecutionAsync(WorkflowExecutionDocument execution, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var existing = await db.WorkflowExecutions.FirstOrDefaultAsync(x => x.Id == execution.Id, cancellationToken);
        if (existing is null)
            return null;

        db.Entry(existing).CurrentValues.SetValues(execution);
        await db.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<WorkflowExecutionDocument?> MarkWorkflowExecutionCompletedAsync(string executionId, WorkflowExecutionState finalState, string failureReason, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var execution = await db.WorkflowExecutions.FirstOrDefaultAsync(x => x.Id == executionId, cancellationToken);
        if (execution is null)
            return null;

        execution.State = finalState;
        execution.EndedAtUtc = DateTime.UtcNow;
        execution.FailureReason = failureReason;
        await db.SaveChangesAsync(cancellationToken);
        return execution;
    }

    public async Task<WorkflowExecutionDocument?> MarkWorkflowExecutionPendingApprovalAsync(string executionId, string pendingApprovalStageId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var execution = await db.WorkflowExecutions.FirstOrDefaultAsync(x => x.Id == executionId, cancellationToken);
        if (execution is null)
            return null;

        execution.State = WorkflowExecutionState.PendingApproval;
        execution.PendingApprovalStageId = pendingApprovalStageId;
        await db.SaveChangesAsync(cancellationToken);
        return execution;
    }

    public async Task<WorkflowExecutionDocument?> ApproveWorkflowStageAsync(string executionId, string approvedBy, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var execution = await db.WorkflowExecutions.FirstOrDefaultAsync(
            x => x.Id == executionId && x.State == WorkflowExecutionState.PendingApproval,
            cancellationToken);
        if (execution is null)
            return null;

        execution.State = WorkflowExecutionState.Running;
        execution.ApprovedBy = approvedBy;
        execution.PendingApprovalStageId = string.Empty;
        await db.SaveChangesAsync(cancellationToken);
        return execution;
    }

    public async Task<WorkflowExecutionDocument?> GetWorkflowExecutionByRunIdAsync(string runId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var executions = await db.WorkflowExecutions.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(500).ToListAsync(cancellationToken);
        return executions.FirstOrDefault(x => x.StageResults.Any(stage => stage.RunIds.Contains(runId)));
    }

    public async Task<WorkflowDocument?> GetWorkflowForExecutionAsync(string workflowId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.Workflows.AsNoTracking().FirstOrDefaultAsync(x => x.Id == workflowId, cancellationToken);
    }

    public async Task<AlertRuleDocument> CreateAlertRuleAsync(AlertRuleDocument rule, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        db.AlertRules.Add(rule);
        await db.SaveChangesAsync(cancellationToken);
        return rule;
    }

    public async Task<List<AlertRuleDocument>> ListAlertRulesAsync(CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.AlertRules.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);
    }

    public async Task<List<AlertRuleDocument>> ListEnabledAlertRulesAsync(CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.AlertRules.AsNoTracking().Where(x => x.Enabled).ToListAsync(cancellationToken);
    }

    public async Task<AlertRuleDocument?> GetAlertRuleAsync(string ruleId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.AlertRules.AsNoTracking().FirstOrDefaultAsync(x => x.Id == ruleId, cancellationToken);
    }

    public async Task<AlertRuleDocument?> UpdateAlertRuleAsync(string ruleId, AlertRuleDocument rule, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var existing = await db.AlertRules.FirstOrDefaultAsync(x => x.Id == ruleId, cancellationToken);
        if (existing is null)
            return null;

        rule.Id = existing.Id;
        db.Entry(existing).CurrentValues.SetValues(rule);
        await db.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<bool> DeleteAlertRuleAsync(string ruleId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var rule = await db.AlertRules.FirstOrDefaultAsync(x => x.Id == ruleId, cancellationToken);
        if (rule is null)
            return false;

        db.AlertRules.Remove(rule);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<AlertEventDocument> RecordAlertEventAsync(AlertEventDocument alertEvent, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        db.AlertEvents.Add(alertEvent);
        await db.SaveChangesAsync(cancellationToken);
        return alertEvent;
    }

    public async Task<AlertEventDocument?> GetAlertEventAsync(string eventId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.AlertEvents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == eventId, cancellationToken);
    }

    public async Task<List<AlertEventDocument>> ListRecentAlertEventsAsync(int limit, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.AlertEvents.AsNoTracking().OrderByDescending(x => x.FiredAtUtc).Take(limit).ToListAsync(cancellationToken);
    }

    public async Task<List<AlertEventDocument>> ListAlertEventsByRuleAsync(string ruleId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        return await db.AlertEvents.AsNoTracking().Where(x => x.RuleId == ruleId).OrderByDescending(x => x.FiredAtUtc).Take(50).ToListAsync(cancellationToken);
    }

    public async Task<AlertEventDocument?> ResolveAlertEventAsync(string eventId, CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
        var alertEvent = await db.AlertEvents.FirstOrDefaultAsync(x => x.Id == eventId, cancellationToken);
        if (alertEvent is null)
            return null;

        alertEvent.Resolved = true;
        await db.SaveChangesAsync(cancellationToken);
        return alertEvent;
    }

    public async Task<int> ResolveAlertEventsAsync(List<string> eventIds, CancellationToken cancellationToken)
    {
        if (eventIds.Count == 0)
            return 0;

        await using var db = CreateSession();
        var events = await db.AlertEvents.Where(x => eventIds.Contains(x.Id) && !x.Resolved).ToListAsync(cancellationToken);
        foreach (var alertEvent in events)
        {
            alertEvent.Resolved = true;
        }

        await db.SaveChangesAsync(cancellationToken);
        return events.Count;
    }

    public async Task<ReliabilityMetrics> GetReliabilityMetricsAsync(CancellationToken cancellationToken)
    {
        await using var db = CreateSession();
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

    private static string NormalizePromptSkillScope(string repositoryId)
    {
        var normalized = repositoryId.Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Repository scope is required.", nameof(repositoryId));
        }

        return string.Equals(normalized, GlobalRepositoryScope, StringComparison.OrdinalIgnoreCase)
            ? GlobalRepositoryScope
            : normalized;
    }

    private static string NormalizeSessionProfileScope(string repositoryId, RunSessionProfileScope scope)
    {
        if (scope == RunSessionProfileScope.Global)
        {
            return GlobalRepositoryScope;
        }

        var normalized = repositoryId.Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("Repository scope is required.", nameof(repositoryId));
        }

        if (string.Equals(normalized, GlobalRepositoryScope, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Repository profiles cannot use global repository scope.", nameof(repositoryId));
        }

        return normalized;
    }

    private static string NormalizeHarnessValue(string harness)
    {
        return NormalizeRequiredValue(harness, nameof(harness)).ToLowerInvariant();
    }

    private static DateTime? ComputeNextAutomationRun(string triggerKind, string cronExpression, bool enabled, DateTime nowUtc)
    {
        if (!enabled)
        {
            return null;
        }

        if (!string.Equals(triggerKind, "cron", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(cronExpression))
        {
            return null;
        }

        try
        {
            var expression = CronExpression.Parse(cronExpression, CronFormat.Standard);
            return expression.GetNextOccurrence(nowUtc, TimeZoneInfo.Utc);
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Invalid cron expression '{cronExpression}': {ex.Message}", nameof(cronExpression), ex);
        }
    }

    private static string NormalizePromptSkillTrigger(string trigger)
    {
        var normalized = NormalizeRequiredValue(trigger, nameof(trigger))
            .TrimStart('/')
            .ToLowerInvariant();

        if (!PromptSkillTriggerRegex.IsMatch(normalized))
        {
            throw new ArgumentException("Trigger must match [a-z0-9-]+.", nameof(trigger));
        }

        return normalized;
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

    private Task DeleteStoredArtifactsByRunIdsAsync(IReadOnlyList<string> runIds, CancellationToken cancellationToken)
    {
        return runArtifactStorage.DeleteByRunIdsAsync(runIds, cancellationToken);
    }

    private OrchestratorRepositorySession CreateSession()
    {
        return new OrchestratorRepositorySession(
            repositories,
            tasks,
            runs,
            workspacePromptEntries,
            semanticChunks,
            runAiSummaries,
            runEvents,
            runStructuredEvents,
            runDiffSnapshots,
            runToolProjections,
            runSessionProfiles,
            runInstructionStacks,
            runShareBundles,
            automationDefinitions,
            automationExecutions,
            findings,
            providerSecrets,
            taskRuntimeRegistrations,
            taskRuntimes,
            webhooks,
            settings,
            leases,
            workflows,
            workflowExecutions,
            alertRules,
            alertEvents,
            repositoryInstructions,
            harnessProviderSettings,
            promptSkills,
            liteDbExecutor,
            liteDbDatabase);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public static DateTime? ComputeNextRun(TaskDocument task, DateTime nowUtc)
    {
        if (!task.Enabled)
            return null;

        if (task.Kind == TaskKind.OneShot)
            return nowUtc;

        if (task.Kind != TaskKind.Cron || string.IsNullOrWhiteSpace(task.CronExpression))
            return null;

        var expression = CronExpression.Parse(task.CronExpression, CronFormat.Standard);
        return expression.GetNextOccurrence(nowUtc, TimeZoneInfo.Utc);
    }




}
