using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.ControlPlane.Data;

public sealed class OrchestratorStore(
    IOrchestratorRepositorySessionFactory liteDbScopeFactory,
    LiteDbExecutor liteDbExecutor,
    LiteDbDatabase liteDbDatabase) : IOrchestratorStore, IAsyncDisposable
{
    private static readonly RunState[] ActiveStates = [RunState.Queued, RunState.Running, RunState.PendingApproval];
    private static readonly FindingState[] OpenFindingStates = [FindingState.New, FindingState.Acknowledged, FindingState.InProgress];
    private static readonly Regex PromptSkillTriggerRegex = new("^[a-z0-9-]+$", RegexOptions.Compiled);
    private const string GlobalRepositoryScope = "global";
    private const string TaskWorkspacesRootPath = "/workspaces/repos";
    private const string ArtifactFileStorageRoot = "$/run-artifacts";

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task<RepositoryDocument> CreateRepositoryAsync(CreateRepositoryRequest request, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var repository = new RepositoryDocument
        {
            Name = request.Name,
            GitUrl = request.GitUrl,
            LocalPath = request.LocalPath,
            DefaultBranch = string.IsNullOrWhiteSpace(request.DefaultBranch) ? "main" : request.DefaultBranch,
            TaskDefaults = NormalizeRepositoryTaskDefaults(null),
        };

        db.Repositories.Add(repository);
        await db.SaveChangesAsync(cancellationToken);
        return repository;
    }

    public async Task<List<RepositoryDocument>> ListRepositoriesAsync(CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var repositories = await db.Repositories.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);
        foreach (var repository in repositories)
        {
            repository.TaskDefaults = NormalizeRepositoryTaskDefaults(repository.TaskDefaults);
        }

        return repositories;
    }

    public async Task<RepositoryDocument?> GetRepositoryAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var repository = await db.Repositories.AsNoTracking().FirstOrDefaultAsync(x => x.Id == repositoryId, cancellationToken);
        if (repository is null)
        {
            return null;
        }

        repository.TaskDefaults = NormalizeRepositoryTaskDefaults(repository.TaskDefaults);
        return repository;
    }

    public async Task<RepositoryDocument?> UpdateRepositoryAsync(string repositoryId, UpdateRepositoryRequest request, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var repository = await db.Repositories.FirstOrDefaultAsync(x => x.Id == repositoryId, cancellationToken);
        if (repository is null)
            return null;

        repository.Name = request.Name;
        repository.GitUrl = request.GitUrl;
        repository.LocalPath = request.LocalPath;
        repository.DefaultBranch = string.IsNullOrWhiteSpace(request.DefaultBranch) ? "main" : request.DefaultBranch;
        repository.TaskDefaults = NormalizeRepositoryTaskDefaults(repository.TaskDefaults);
        await db.SaveChangesAsync(cancellationToken);
        return repository;
    }

    public async Task<RepositoryDocument?> UpdateRepositoryTaskDefaultsAsync(
        string repositoryId,
        UpdateRepositoryTaskDefaultsRequest request,
        CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var repository = await db.Repositories.FirstOrDefaultAsync(x => x.Id == repositoryId, cancellationToken);
        if (repository is null)
        {
            return null;
        }

        var taskDefaults = NormalizeRepositoryTaskDefaults(
            new RepositoryTaskDefaultsConfig
            {
                Kind = request.Kind,
                Harness = request.Harness,
                ExecutionModeDefault = request.ExecutionModeDefault,
                SessionProfileId = request.SessionProfileId?.Trim() ?? string.Empty,
                Command = request.Command,
                AutoCreatePullRequest = request.AutoCreatePullRequest,
                Enabled = request.Enabled,
            });

        repository.TaskDefaults = taskDefaults;
        await db.SaveChangesAsync(cancellationToken);
        return repository;
    }


    public async Task<RepositoryDocument?> UpdateRepositoryGitStateAsync(string repositoryId, RepositoryGitStatus gitStatus, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
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
        repository.TaskDefaults = NormalizeRepositoryTaskDefaults(repository.TaskDefaults);

        await db.SaveChangesAsync(cancellationToken);
        return repository;
    }

    public async Task<RepositoryDocument?> TouchRepositoryAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var repository = await db.Repositories.FirstOrDefaultAsync(x => x.Id == repositoryId, cancellationToken);
        if (repository is null)
            return null;

        repository.LastViewedAtUtc = DateTime.UtcNow;
        repository.TaskDefaults = NormalizeRepositoryTaskDefaults(repository.TaskDefaults);
        await db.SaveChangesAsync(cancellationToken);
        return repository;
    }

    public async Task<bool> DeleteRepositoryAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var repository = await db.Repositories.FirstOrDefaultAsync(x => x.Id == repositoryId, cancellationToken);
        if (repository is null)
            return false;

        db.Repositories.Remove(repository);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<List<InstructionFile>> GetRepositoryInstructionFilesAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var repo = await db.Repositories.AsNoTracking().FirstOrDefaultAsync(x => x.Id == repositoryId, cancellationToken);
        return repo?.InstructionFiles ?? [];
    }

    public async Task<RepositoryDocument?> UpdateRepositoryInstructionFilesAsync(string repositoryId, List<InstructionFile> instructionFiles, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var repo = await db.Repositories.FirstOrDefaultAsync(x => x.Id == repositoryId, cancellationToken);
        if (repo is null)
            return null;

        repo.InstructionFiles = instructionFiles;
        repo.TaskDefaults = NormalizeRepositoryTaskDefaults(repo.TaskDefaults);
        await db.SaveChangesAsync(cancellationToken);
        return repo;
    }

    public async Task<List<RepositoryInstructionDocument>> GetInstructionsAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.RepositoryInstructions.AsNoTracking()
            .Where(x => x.RepositoryId == repositoryId)
            .OrderBy(x => x.Priority)
            .ToListAsync(cancellationToken);
    }

    public async Task<RepositoryInstructionDocument?> GetInstructionAsync(string instructionId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.RepositoryInstructions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == instructionId, cancellationToken);
    }

    public async Task<RepositoryInstructionDocument> UpsertInstructionAsync(string repositoryId, string? instructionId, CreateRepositoryInstructionRequest request, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
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
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var instruction = await db.RepositoryInstructions.FirstOrDefaultAsync(x => x.Id == instructionId, cancellationToken);
        if (instruction is null)
            return false;

        db.RepositoryInstructions.Remove(instruction);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<HarnessProviderSettingsDocument?> GetHarnessProviderSettingsAsync(string repositoryId, string harness, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
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
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
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
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
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
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
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
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.PromptSkills.AsNoTracking().FirstOrDefaultAsync(x => x.Id == skillId, cancellationToken);
    }

    public async Task<PromptSkillDocument?> UpdatePromptSkillAsync(string skillId, UpdatePromptSkillRequest request, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
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
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
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
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
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
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
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
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.RunSessionProfiles.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sessionProfileId, cancellationToken);
    }

    public async Task<RunSessionProfileDocument?> UpdateRunSessionProfileAsync(string sessionProfileId, UpdateRunSessionProfileRequest request, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
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
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var existing = await db.RunSessionProfiles.FirstOrDefaultAsync(x => x.Id == sessionProfileId, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        db.RunSessionProfiles.Remove(existing);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

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
            Kind = repository.TaskDefaults.Kind,
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

    public async Task<List<TaskDocument>> ListEventDrivenTasksAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.Tasks.AsNoTracking()
            .Where(x => x.RepositoryId == repositoryId && x.Enabled && x.Kind == TaskKind.EventDriven)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
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
            .Where(x => x.Enabled && x.Kind == TaskKind.OneShot)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task MarkOneShotTaskConsumedAsync(string taskId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var task = await db.Tasks.FirstOrDefaultAsync(x => x.Id == taskId, cancellationToken);
        if (task is null)
            return;

        task.Enabled = false;
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
        task.Kind = request.Kind;
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
            _ = await db.RunQuestionRequests.DeleteWhereAsync(x => runIds.Contains(x.RunId), cancellationToken);
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
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("VACUUM;", cancellationToken);
    }

    public async Task<RunDocument> CreateRunAsync(
        TaskDocument task,
        CancellationToken cancellationToken,
        int attempt = 1,
        HarnessExecutionMode? executionModeOverride = null,
        string? sessionProfileId = null)
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
        bool excludeTasksWithOpenFindings,
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
        if (excludeTasksWithOpenFindings)
        {
            var taskIds = candidateRunSeeds
                .Select(x => x.TaskId)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (taskIds.Count > 0)
            {
                var excludedTaskIds = new HashSet<string>(StringComparer.Ordinal);
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
        _ = await db.RunQuestionRequests.DeleteWhereAsync(x => runIds.Contains(x.RunId), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return new StructuredRunDataPruneResult(
            RunsScanned: runIds.Count,
            DeletedStructuredEvents: deletedStructuredEvents,
            DeletedDiffSnapshots: deletedDiffSnapshots,
            DeletedToolProjections: deletedToolProjections);
    }

    public async Task<List<FindingDocument>> ListFindingsAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.Findings.AsNoTracking().Where(x => x.RepositoryId == repositoryId).OrderByDescending(x => x.CreatedAtUtc).Take(200).ToListAsync(cancellationToken);
    }

    public async Task<List<FindingDocument>> ListAllFindingsAsync(CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.Findings.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(500).ToListAsync(cancellationToken);
    }

    public async Task<FindingDocument?> GetFindingAsync(string findingId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.Findings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == findingId, cancellationToken);
    }

    public async Task<FindingDocument> CreateFindingFromFailureAsync(RunDocument run, string description, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
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
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var finding = await db.Findings.FirstOrDefaultAsync(x => x.Id == findingId, cancellationToken);
        if (finding is null)
            return null;

        finding.State = state;
        await db.SaveChangesAsync(cancellationToken);
        return finding;
    }

    public async Task<FindingDocument?> AssignFindingAsync(string findingId, string assignedTo, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
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
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var finding = await db.Findings.FirstOrDefaultAsync(x => x.Id == findingId, cancellationToken);
        if (finding is null)
            return false;

        db.Findings.Remove(finding);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task UpsertProviderSecretAsync(string repositoryId, string provider, string encryptedValue, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
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
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.ProviderSecrets.AsNoTracking().Where(x => x.RepositoryId == repositoryId).ToListAsync(cancellationToken);
    }

    public async Task<ProviderSecretDocument?> GetProviderSecretAsync(string repositoryId, string provider, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.ProviderSecrets.AsNoTracking().FirstOrDefaultAsync(x => x.RepositoryId == repositoryId && x.Provider == provider, cancellationToken);
    }

    public async Task<bool> DeleteProviderSecretAsync(string repositoryId, string provider, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var secret = await db.ProviderSecrets.FirstOrDefaultAsync(x => x.RepositoryId == repositoryId && x.Provider == provider, cancellationToken);
        if (secret is null)
            return false;

        db.ProviderSecrets.Remove(secret);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<List<TaskRuntimeRegistration>> ListTaskRuntimeRegistrationsAsync(CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.TaskRuntimeRegistrations.AsNoTracking().OrderBy(x => x.RuntimeId).ToListAsync(cancellationToken);
    }

    public async Task UpsertTaskRuntimeRegistrationHeartbeatAsync(string runtimeId, string endpoint, int activeSlots, int maxSlots, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
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
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
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
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.TaskRuntimes.AsNoTracking().OrderBy(x => x.RuntimeId).ToListAsync(cancellationToken);
    }

    public async Task<TaskRuntimeDocument> UpsertTaskRuntimeStateAsync(TaskRuntimeStateUpdate update, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(update.RuntimeId))
        {
            throw new InvalidOperationException("RuntimeId is required.");
        }

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
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
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
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
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
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
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.Webhooks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == webhookId, cancellationToken);
    }

    public async Task<List<WebhookRegistration>> ListWebhooksAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.Webhooks.AsNoTracking().Where(x => x.RepositoryId == repositoryId).ToListAsync(cancellationToken);
    }

    public async Task<WebhookRegistration?> UpdateWebhookAsync(string webhookId, UpdateWebhookRequest request, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
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
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var webhook = await db.Webhooks.FirstOrDefaultAsync(x => x.Id == webhookId, cancellationToken);
        if (webhook is null)
            return false;

        db.Webhooks.Remove(webhook);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<SystemSettingsDocument> GetSettingsAsync(CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var settings = await db.Settings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == "singleton", cancellationToken);
        return settings ?? new SystemSettingsDocument();
    }

    public async Task<SystemSettingsDocument> UpdateSettingsAsync(SystemSettingsDocument settings, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
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

    public async Task<List<McpRegistryServerDocument>> ListMcpRegistryServersAsync(CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.McpRegistryServers.AsNoTracking()
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);
    }

    public async Task ReplaceMcpRegistryServersAsync(List<McpRegistryServerDocument> servers, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);

        var existing = await db.McpRegistryServers.ToListAsync(cancellationToken);
        foreach (var entry in existing)
        {
            db.McpRegistryServers.Remove(entry);
        }

        foreach (var server in servers)
        {
            if (string.IsNullOrWhiteSpace(server.Id))
            {
                server.Id = Guid.NewGuid().ToString("N");
            }

            server.UpdatedAtUtc = DateTime.UtcNow;
            db.McpRegistryServers.Add(server);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<McpRegistryStateDocument> GetMcpRegistryStateAsync(CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var state = await db.McpRegistryState.AsNoTracking().FirstOrDefaultAsync(x => x.Id == "singleton", cancellationToken);
        return state ?? new McpRegistryStateDocument();
    }

    public async Task<McpRegistryStateDocument> UpsertMcpRegistryStateAsync(McpRegistryStateDocument state, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        state.Id = "singleton";
        state.UpdatedAtUtc = DateTime.UtcNow;

        var existing = await db.McpRegistryState.FirstOrDefaultAsync(x => x.Id == "singleton", cancellationToken);
        if (existing is null)
        {
            db.McpRegistryState.Add(state);
            await db.SaveChangesAsync(cancellationToken);
            return state;
        }

        db.Entry(existing).CurrentValues.SetValues(state);
        await db.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<bool> TryAcquireLeaseAsync(string leaseName, string ownerId, TimeSpan ttl, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
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
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
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
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
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

    public async Task<AlertRuleDocument> CreateAlertRuleAsync(AlertRuleDocument rule, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        db.AlertRules.Add(rule);
        await db.SaveChangesAsync(cancellationToken);
        return rule;
    }

    public async Task<List<AlertRuleDocument>> ListAlertRulesAsync(CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.AlertRules.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);
    }

    public async Task<List<AlertRuleDocument>> ListEnabledAlertRulesAsync(CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.AlertRules.AsNoTracking().Where(x => x.Enabled).ToListAsync(cancellationToken);
    }

    public async Task<AlertRuleDocument?> GetAlertRuleAsync(string ruleId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.AlertRules.AsNoTracking().FirstOrDefaultAsync(x => x.Id == ruleId, cancellationToken);
    }

    public async Task<AlertRuleDocument?> UpdateAlertRuleAsync(string ruleId, AlertRuleDocument rule, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
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
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        var rule = await db.AlertRules.FirstOrDefaultAsync(x => x.Id == ruleId, cancellationToken);
        if (rule is null)
            return false;

        db.AlertRules.Remove(rule);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<AlertEventDocument> RecordAlertEventAsync(AlertEventDocument alertEvent, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        db.AlertEvents.Add(alertEvent);
        await db.SaveChangesAsync(cancellationToken);
        return alertEvent;
    }

    public async Task<AlertEventDocument?> GetAlertEventAsync(string eventId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.AlertEvents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == eventId, cancellationToken);
    }

    public async Task<List<AlertEventDocument>> ListRecentAlertEventsAsync(int limit, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.AlertEvents.AsNoTracking().OrderByDescending(x => x.FiredAtUtc).Take(limit).ToListAsync(cancellationToken);
    }

    public async Task<List<AlertEventDocument>> ListAlertEventsByRuleAsync(string ruleId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
        return await db.AlertEvents.AsNoTracking().Where(x => x.RuleId == ruleId).OrderByDescending(x => x.FiredAtUtc).Take(50).ToListAsync(cancellationToken);
    }

    public async Task<AlertEventDocument?> ResolveAlertEventAsync(string eventId, CancellationToken cancellationToken)
    {
        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
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

        await using var db = await liteDbScopeFactory.CreateAsync(cancellationToken);
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

    private static RepositoryTaskDefaultsConfig NormalizeRepositoryTaskDefaults(RepositoryTaskDefaultsConfig? taskDefaults)
    {
        var kind = taskDefaults?.Kind ?? TaskKind.OneShot;
        if (!Enum.IsDefined(kind))
        {
            kind = TaskKind.OneShot;
        }

        var mode = taskDefaults?.ExecutionModeDefault ?? HarnessExecutionMode.Default;
        if (!Enum.IsDefined(mode))
        {
            mode = HarnessExecutionMode.Default;
        }

        var defaultCommand = new RepositoryTaskDefaultsConfig().Command;
        var command = taskDefaults?.Command?.Trim() ?? string.Empty;
        if (command.Length == 0)
        {
            command = defaultCommand;
        }

        var harness = taskDefaults?.Harness?.Trim().ToLowerInvariant() ?? string.Empty;
        if (harness.Length == 0)
        {
            harness = "codex";
        }

        return new RepositoryTaskDefaultsConfig
        {
            Kind = kind,
            Harness = harness,
            ExecutionModeDefault = mode,
            SessionProfileId = taskDefaults?.SessionProfileId?.Trim() ?? string.Empty,
            Command = command,
            AutoCreatePullRequest = taskDefaults?.AutoCreatePullRequest ?? false,
            Enabled = taskDefaults?.Enabled ?? true,
        };
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

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    public static DateTime? ComputeNextRun(TaskDocument task, DateTime nowUtc)
    {
        if (!task.Enabled)
        {
            return null;
        }

        return task.Kind == TaskKind.OneShot ? nowUtc : null;
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
