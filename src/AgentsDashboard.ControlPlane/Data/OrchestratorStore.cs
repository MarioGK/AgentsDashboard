using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Configuration;
using Cronos;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.ControlPlane.Data;

public sealed class OrchestratorStore(
    IDbContextFactory<OrchestratorDbContext> dbContextFactory,
    IOptions<OrchestratorOptions> orchestratorOptions) : IOrchestratorStore
{
    private static readonly RunState[] ActiveStates = [RunState.Queued, RunState.Running, RunState.PendingApproval];
    private static readonly Regex PromptSkillTriggerRegex = new("^[a-z0-9-]+$", RegexOptions.Compiled);
    private const string GlobalRepositoryScope = "global";
    private readonly string _artifactsRootPath = orchestratorOptions.Value.ArtifactsRootPath;

    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task<RepositoryDocument> CreateRepositoryAsync(CreateRepositoryRequest request, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Repositories.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);
    }

    public async Task<RepositoryDocument?> GetRepositoryAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Repositories.AsNoTracking().FirstOrDefaultAsync(x => x.Id == repositoryId, cancellationToken);
    }

    public async Task<RepositoryDocument?> UpdateRepositoryAsync(string repositoryId, UpdateRepositoryRequest request, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var repository = await db.Repositories.FirstOrDefaultAsync(x => x.Id == repositoryId, cancellationToken);
        if (repository is null)
            return null;

        repository.LastViewedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return repository;
    }

    public async Task<bool> DeleteRepositoryAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var repository = await db.Repositories.FirstOrDefaultAsync(x => x.Id == repositoryId, cancellationToken);
        if (repository is null)
            return false;

        db.Repositories.Remove(repository);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<List<InstructionFile>> GetRepositoryInstructionFilesAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var repo = await db.Repositories.AsNoTracking().FirstOrDefaultAsync(x => x.Id == repositoryId, cancellationToken);
        return repo?.InstructionFiles ?? [];
    }

    public async Task<RepositoryDocument?> UpdateRepositoryInstructionFilesAsync(string repositoryId, List<InstructionFile> instructionFiles, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var repo = await db.Repositories.FirstOrDefaultAsync(x => x.Id == repositoryId, cancellationToken);
        if (repo is null)
            return null;

        repo.InstructionFiles = instructionFiles;
        await db.SaveChangesAsync(cancellationToken);
        return repo;
    }

    public async Task<List<RepositoryInstructionDocument>> GetInstructionsAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.RepositoryInstructions.AsNoTracking()
            .Where(x => x.RepositoryId == repositoryId)
            .OrderBy(x => x.Priority)
            .ToListAsync(cancellationToken);
    }

    public async Task<RepositoryInstructionDocument?> GetInstructionAsync(string instructionId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.RepositoryInstructions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == instructionId, cancellationToken);
    }

    public async Task<RepositoryInstructionDocument> UpsertInstructionAsync(string repositoryId, string? instructionId, CreateRepositoryInstructionRequest request, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var instruction = await db.RepositoryInstructions.FirstOrDefaultAsync(x => x.Id == instructionId, cancellationToken);
        if (instruction is null)
            return false;

        db.RepositoryInstructions.Remove(instruction);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<HarnessProviderSettingsDocument?> GetHarnessProviderSettingsAsync(string repositoryId, string harness, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.PromptSkills.AsNoTracking().FirstOrDefaultAsync(x => x.Id == skillId, cancellationToken);
    }

    public async Task<PromptSkillDocument?> UpdatePromptSkillAsync(string skillId, UpdatePromptSkillRequest request, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.PromptSkills.FirstOrDefaultAsync(x => x.Id == skillId, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        db.PromptSkills.Remove(existing);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<TaskDocument> CreateTaskAsync(CreateTaskRequest request, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Tasks.AsNoTracking().Where(x => x.RepositoryId == repositoryId).OrderBy(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task<List<TaskDocument>> ListEventDrivenTasksAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Tasks.AsNoTracking()
            .Where(x => x.RepositoryId == repositoryId && x.Enabled && x.Kind == TaskKind.EventDriven)
            .OrderBy(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<TaskDocument?> GetTaskAsync(string taskId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Tasks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == taskId, cancellationToken);
    }

    public async Task<List<TaskDocument>> ListScheduledTasksAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Tasks.AsNoTracking()
            .Where(x => x.Enabled && x.Kind == TaskKind.Cron)
            .OrderBy(x => x.NextRunAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<TaskDocument>> ListDueTasksAsync(DateTime utcNow, int limit, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Tasks.AsNoTracking()
            .Where(x => x.Enabled && (x.Kind == TaskKind.OneShot || (x.Kind == TaskKind.Cron && x.NextRunAtUtc != null && x.NextRunAtUtc <= utcNow)))
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task MarkOneShotTaskConsumedAsync(string taskId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var task = await db.Tasks.FirstOrDefaultAsync(x => x.Id == taskId, cancellationToken);
        if (task is null)
            return;

        task.Enabled = false;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateTaskNextRunAsync(string taskId, DateTime? nextRunAtUtc, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var task = await db.Tasks.FirstOrDefaultAsync(x => x.Id == taskId, cancellationToken);
        if (task is null)
            return;

        task.NextRunAtUtc = nextRunAtUtc;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<TaskDocument?> UpdateTaskAsync(string taskId, UpdateTaskRequest request, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var task = await db.Tasks.FirstOrDefaultAsync(x => x.Id == taskId, cancellationToken);
        if (task is null)
            return null;

        task.Name = request.Name;
        task.Kind = request.Kind;
        task.Harness = request.Harness.Trim().ToLowerInvariant();
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var task = await db.Tasks.FirstOrDefaultAsync(x => x.Id == taskId, cancellationToken);
        if (task is null)
            return false;

        db.Tasks.Remove(task);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<RunDocument> CreateRunAsync(TaskDocument task, CancellationToken cancellationToken, int attempt = 1)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var run = new RunDocument
        {
            RepositoryId = task.RepositoryId,
            TaskId = task.Id,
            State = RunState.Queued,
            Summary = "Queued",
            Attempt = attempt,
        };

        db.Runs.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        return run;
    }

    public async Task<List<RunDocument>> ListRunsByRepositoryAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Runs.AsNoTracking().Where(x => x.RepositoryId == repositoryId).OrderByDescending(x => x.CreatedAtUtc).Take(200).ToListAsync(cancellationToken);
    }

    public async Task<List<RunDocument>> ListRecentRunsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Runs.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(100).ToListAsync(cancellationToken);
    }

    public async Task<List<RepositoryDocument>> ListRepositoriesWithRecentTasksAsync(int limit, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var normalizedLimit = Math.Clamp(limit, 1, 500);

        return await db.Runs.AsNoTracking()
            .Where(x => x.TaskId == taskId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(normalizedLimit)
            .ToListAsync(cancellationToken);
    }

    public async Task<Dictionary<string, RunState>> GetLatestRunStatesByTaskIdsAsync(List<string> taskIds, CancellationToken cancellationToken)
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

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
            select new
            {
                run.TaskId,
                run.State,
                run.Id
            })
            .ToListAsync(cancellationToken);

        return latestRunCandidates
            .GroupBy(x => x.TaskId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(x => x.Id, StringComparer.Ordinal)
                    .First()
                    .State,
                StringComparer.Ordinal);
    }

    public async Task<List<WorkspacePromptEntryDocument>> ListWorkspacePromptHistoryAsync(string taskId, int limit, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return [];
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

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

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.RunAiSummaries.AsNoTracking()
            .FirstOrDefaultAsync(x => x.RunId == runId, cancellationToken);
    }

    public async Task UpsertSemanticChunksAsync(string taskId, List<SemanticChunkDocument> chunks, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId) || chunks.Count == 0)
        {
            return;
        }

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var normalizedLimit = Math.Clamp(limit, 1, 200);
        var chunks = db.SemanticChunks.AsNoTracking().Where(x => x.TaskId == taskId);
        if (!string.IsNullOrWhiteSpace(queryEmbeddingPayload))
        {
            try
            {
                var semanticIds = new List<string>(normalizedLimit);
                var connection = (SqliteConnection)db.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open)
                {
                    await connection.OpenAsync(cancellationToken);
                }

                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT Id
                    FROM SemanticChunks
                    WHERE TaskId = $taskId
                      AND EmbeddingPayload <> ''
                    ORDER BY vec_distance_cosine(vec_f32(EmbeddingPayload), vec_f32($embedding)) ASC, UpdatedAtUtc DESC
                    LIMIT $limit;
                    """;
                command.Parameters.AddWithValue("$taskId", taskId);
                command.Parameters.AddWithValue("$embedding", queryEmbeddingPayload);
                command.Parameters.AddWithValue("$limit", normalizedLimit);

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    semanticIds.Add(reader.GetString(0));
                }

                if (semanticIds.Count > 0)
                {
                    var candidates = await chunks
                        .Where(x => semanticIds.Contains(x.Id))
                        .ToListAsync(cancellationToken);
                    var byId = candidates.ToDictionary(x => x.Id, StringComparer.Ordinal);
                    return semanticIds
                        .Select(id => byId.GetValueOrDefault(id))
                        .Where(chunk => chunk is not null)
                        .Cast<SemanticChunkDocument>()
                        .ToList();
                }
            }
            catch (SqliteException ex)
            {
                // sqlite-vec unavailable or vector function not loaded; fallback to text ranking below.
                _ = ex;
            }
        }

        var normalizedQuery = queryText.Trim();
        if (normalizedQuery.Length == 0)
        {
            return await chunks
                .OrderByDescending(x => x.UpdatedAtUtc)
                .Take(normalizedLimit)
                .ToListAsync(cancellationToken);
        }

        var likeQuery = $"%{normalizedQuery}%";
        var textMatches = await chunks
            .Where(x =>
                EF.Functions.Like(x.Content, likeQuery) ||
                EF.Functions.Like(x.SourceRef, likeQuery) ||
                EF.Functions.Like(x.ChunkKey, likeQuery))
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(normalizedLimit)
            .ToListAsync(cancellationToken);

        if (textMatches.Count > 0)
        {
            return textMatches;
        }

        return await chunks
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(normalizedLimit)
            .ToListAsync(cancellationToken);
    }

    public async Task<ReliabilityMetrics> GetReliabilityMetricsByRepositoryAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Runs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == runId, cancellationToken);
    }

    public async Task<List<RunDocument>> ListRunsByStateAsync(RunState state, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Runs.AsNoTracking().Where(x => x.State == state).OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task<List<string>> ListAllRunIdsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Runs.AsNoTracking().Select(x => x.Id).ToListAsync(cancellationToken);
    }

    public async Task<long> CountRunsByStateAsync(RunState state, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Runs.LongCountAsync(x => x.State == state, cancellationToken);
    }

    public async Task<long> CountActiveRunsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Runs.LongCountAsync(x => ActiveStates.Contains(x.State), cancellationToken);
    }

    public async Task<long> CountActiveRunsByRepoAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Runs.LongCountAsync(x => x.RepositoryId == repositoryId && ActiveStates.Contains(x.State), cancellationToken);
    }

    public async Task<long> CountActiveRunsByTaskAsync(string taskId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var run = await db.Runs.FirstOrDefaultAsync(x => x.Id == runId && x.State != RunState.Obsolete, cancellationToken);
        if (run is null)
            return null;

        run.State = RunState.Running;
        run.WorkerId = workerId;
        run.StartedAtUtc = DateTime.UtcNow;
        run.Summary = "Running";
        if (!string.IsNullOrWhiteSpace(workerImageRef))
        {
            run.WorkerImageRef = workerImageRef;
        }

        if (!string.IsNullOrWhiteSpace(workerImageDigest))
        {
            run.WorkerImageDigest = workerImageDigest;
        }

        if (!string.IsNullOrWhiteSpace(workerImageSource))
        {
            run.WorkerImageSource = workerImageSource;
        }

        await db.SaveChangesAsync(cancellationToken);
        return run;
    }

    public async Task<RunDocument?> MarkRunCompletedAsync(string runId, bool succeeded, string summary, string outputJson, CancellationToken cancellationToken, string? failureClass = null, string? prUrl = null)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var run = await db.Runs.FirstOrDefaultAsync(x => x.Id == runId && ActiveStates.Contains(x.State), cancellationToken);
        if (run is null)
            return null;

        run.State = RunState.Obsolete;
        run.EndedAtUtc = DateTime.UtcNow;
        run.Summary = "Obsolete";
        await db.SaveChangesAsync(cancellationToken);
        return run;
    }

    public async Task<RunDocument?> MarkRunPendingApprovalAsync(string runId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        var artifactDir = Path.Combine(_artifactsRootPath, runId);
        Directory.CreateDirectory(artifactDir);

        var filePath = Path.Combine(artifactDir, fileName);
        await using var fileStream = File.Create(filePath);
        await stream.CopyToAsync(fileStream, cancellationToken);
    }

    public Task<List<string>> ListArtifactsAsync(string runId, CancellationToken cancellationToken)
    {
        var artifactDir = Path.Combine(_artifactsRootPath, runId);
        if (!Directory.Exists(artifactDir))
            return Task.FromResult(new List<string>());

        var files = Directory.GetFiles(artifactDir)
            .Select(Path.GetFileName)
            .Where(f => f is not null)
            .Cast<string>()
            .ToList();

        return Task.FromResult(files);
    }

    public Task<FileStream?> GetArtifactAsync(string runId, string fileName, CancellationToken cancellationToken)
    {
        var artifactDir = Path.Combine(_artifactsRootPath, runId);
        var filePath = Path.Combine(artifactDir, fileName);

        if (!File.Exists(filePath))
            return Task.FromResult<FileStream?>(null);

        return Task.FromResult<FileStream?>(File.OpenRead(filePath));
    }

    public async Task AddRunLogAsync(RunLogEvent logEvent, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.RunEvents.Add(logEvent);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<RunLogEvent>> ListRunLogsAsync(string runId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.RunEvents.AsNoTracking().Where(x => x.RunId == runId).OrderBy(x => x.TimestampUtc).ToListAsync(cancellationToken);
    }

    public async Task<List<FindingDocument>> ListFindingsAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Findings.AsNoTracking().Where(x => x.RepositoryId == repositoryId).OrderByDescending(x => x.CreatedAtUtc).Take(200).ToListAsync(cancellationToken);
    }

    public async Task<List<FindingDocument>> ListAllFindingsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Findings.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(500).ToListAsync(cancellationToken);
    }

    public async Task<FindingDocument?> GetFindingAsync(string findingId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Findings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == findingId, cancellationToken);
    }

    public async Task<FindingDocument> CreateFindingFromFailureAsync(RunDocument run, string description, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var finding = await db.Findings.FirstOrDefaultAsync(x => x.Id == findingId, cancellationToken);
        if (finding is null)
            return null;

        finding.State = state;
        await db.SaveChangesAsync(cancellationToken);
        return finding;
    }

    public async Task<FindingDocument?> AssignFindingAsync(string findingId, string assignedTo, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var finding = await db.Findings.FirstOrDefaultAsync(x => x.Id == findingId, cancellationToken);
        if (finding is null)
            return false;

        db.Findings.Remove(finding);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task UpsertProviderSecretAsync(string repositoryId, string provider, string encryptedValue, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.ProviderSecrets.AsNoTracking().Where(x => x.RepositoryId == repositoryId).ToListAsync(cancellationToken);
    }

    public async Task<ProviderSecretDocument?> GetProviderSecretAsync(string repositoryId, string provider, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.ProviderSecrets.AsNoTracking().FirstOrDefaultAsync(x => x.RepositoryId == repositoryId && x.Provider == provider, cancellationToken);
    }

    public async Task<bool> DeleteProviderSecretAsync(string repositoryId, string provider, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var secret = await db.ProviderSecrets.FirstOrDefaultAsync(x => x.RepositoryId == repositoryId && x.Provider == provider, cancellationToken);
        if (secret is null)
            return false;

        db.ProviderSecrets.Remove(secret);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<List<WorkerRegistration>> ListWorkersAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Workers.AsNoTracking().OrderBy(x => x.WorkerId).ToListAsync(cancellationToken);
    }

    public async Task UpsertWorkerHeartbeatAsync(string workerId, string endpoint, int activeSlots, int maxSlots, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var worker = await db.Workers.FirstOrDefaultAsync(x => x.WorkerId == workerId, cancellationToken);
        if (worker is null)
        {
            worker = new WorkerRegistration
            {
                WorkerId = workerId,
                RegisteredAtUtc = DateTime.UtcNow,
            };
            db.Workers.Add(worker);
        }

        worker.Endpoint = endpoint;
        worker.ActiveSlots = activeSlots;
        worker.MaxSlots = maxSlots;
        worker.Online = true;
        worker.LastHeartbeatUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkStaleWorkersOfflineAsync(TimeSpan threshold, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var cutoff = DateTime.UtcNow - threshold;
        var stale = await db.Workers.Where(x => x.Online && x.LastHeartbeatUtc < cutoff).ToListAsync(cancellationToken);
        if (stale.Count == 0)
            return;

        foreach (var worker in stale)
        {
            worker.Online = false;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<WebhookRegistration> CreateWebhookAsync(CreateWebhookRequest request, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Webhooks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == webhookId, cancellationToken);
    }

    public async Task<List<WebhookRegistration>> ListWebhooksAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Webhooks.AsNoTracking().Where(x => x.RepositoryId == repositoryId).ToListAsync(cancellationToken);
    }

    public async Task<WebhookRegistration?> UpdateWebhookAsync(string webhookId, UpdateWebhookRequest request, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var webhook = await db.Webhooks.FirstOrDefaultAsync(x => x.Id == webhookId, cancellationToken);
        if (webhook is null)
            return false;

        db.Webhooks.Remove(webhook);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task RecordProxyRequestAsync(ProxyAuditDocument audit, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.ProxyAudits.Add(audit);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<ProxyAuditDocument>> ListProxyAuditsAsync(string runId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.ProxyAudits.AsNoTracking().Where(x => x.RunId == runId).OrderByDescending(x => x.TimestampUtc).Take(200).ToListAsync(cancellationToken);
    }

    public async Task<List<ProxyAuditDocument>> ListProxyAuditsAsync(string? repoId, string? taskId, string? runId, int limit, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.ProxyAudits.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(repoId))
            query = query.Where(x => x.RepoId == repoId);
        if (!string.IsNullOrWhiteSpace(taskId))
            query = query.Where(x => x.TaskId == taskId);
        if (!string.IsNullOrWhiteSpace(runId))
            query = query.Where(x => x.RunId == runId);

        return await query.OrderByDescending(x => x.TimestampUtc).Take(limit).ToListAsync(cancellationToken);
    }

    public async Task<SystemSettingsDocument> GetSettingsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var settings = await db.Settings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == "singleton", cancellationToken);
        return settings ?? new SystemSettingsDocument();
    }

    public async Task<SystemSettingsDocument> UpdateSettingsAsync(SystemSettingsDocument settings, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.Workflows.Add(workflow);
        await db.SaveChangesAsync(cancellationToken);
        return workflow;
    }

    public async Task<List<WorkflowDocument>> ListWorkflowsByRepositoryAsync(string repositoryId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Workflows.AsNoTracking().Where(x => x.RepositoryId == repositoryId).OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task<List<WorkflowDocument>> ListAllWorkflowsAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Workflows.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task<WorkflowDocument?> GetWorkflowAsync(string workflowId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Workflows.AsNoTracking().FirstOrDefaultAsync(x => x.Id == workflowId, cancellationToken);
    }

    public async Task<WorkflowDocument?> UpdateWorkflowAsync(string workflowId, WorkflowDocument workflow, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var workflow = await db.Workflows.FirstOrDefaultAsync(x => x.Id == workflowId, cancellationToken);
        if (workflow is null)
            return false;

        db.Workflows.Remove(workflow);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<WorkflowExecutionDocument> CreateWorkflowExecutionAsync(WorkflowExecutionDocument execution, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.WorkflowExecutions.Add(execution);
        await db.SaveChangesAsync(cancellationToken);
        return execution;
    }

    public async Task<List<WorkflowExecutionDocument>> ListWorkflowExecutionsAsync(string workflowId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.WorkflowExecutions.AsNoTracking()
            .Where(x => x.WorkflowId == workflowId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<WorkflowExecutionDocument>> ListWorkflowExecutionsByStateAsync(WorkflowExecutionState state, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.WorkflowExecutions.AsNoTracking().Where(x => x.State == state).OrderByDescending(x => x.CreatedAtUtc).ToListAsync(cancellationToken);
    }

    public async Task<WorkflowExecutionDocument?> GetWorkflowExecutionAsync(string executionId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.WorkflowExecutions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == executionId, cancellationToken);
    }

    public async Task<WorkflowExecutionDocument?> UpdateWorkflowExecutionAsync(WorkflowExecutionDocument execution, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.WorkflowExecutions.FirstOrDefaultAsync(x => x.Id == execution.Id, cancellationToken);
        if (existing is null)
            return null;

        db.Entry(existing).CurrentValues.SetValues(execution);
        await db.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task<WorkflowExecutionDocument?> MarkWorkflowExecutionCompletedAsync(string executionId, WorkflowExecutionState finalState, string failureReason, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var executions = await db.WorkflowExecutions.AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).Take(500).ToListAsync(cancellationToken);
        return executions.FirstOrDefault(x => x.StageResults.Any(stage => stage.RunIds.Contains(runId)));
    }

    public async Task<WorkflowDocument?> GetWorkflowForExecutionAsync(string workflowId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Workflows.AsNoTracking().FirstOrDefaultAsync(x => x.Id == workflowId, cancellationToken);
    }

    public async Task<AlertRuleDocument> CreateAlertRuleAsync(AlertRuleDocument rule, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.AlertRules.Add(rule);
        await db.SaveChangesAsync(cancellationToken);
        return rule;
    }

    public async Task<List<AlertRuleDocument>> ListAlertRulesAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.AlertRules.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken);
    }

    public async Task<List<AlertRuleDocument>> ListEnabledAlertRulesAsync(CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.AlertRules.AsNoTracking().Where(x => x.Enabled).ToListAsync(cancellationToken);
    }

    public async Task<AlertRuleDocument?> GetAlertRuleAsync(string ruleId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.AlertRules.AsNoTracking().FirstOrDefaultAsync(x => x.Id == ruleId, cancellationToken);
    }

    public async Task<AlertRuleDocument?> UpdateAlertRuleAsync(string ruleId, AlertRuleDocument rule, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rule = await db.AlertRules.FirstOrDefaultAsync(x => x.Id == ruleId, cancellationToken);
        if (rule is null)
            return false;

        db.AlertRules.Remove(rule);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<AlertEventDocument> RecordAlertEventAsync(AlertEventDocument alertEvent, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.AlertEvents.Add(alertEvent);
        await db.SaveChangesAsync(cancellationToken);
        return alertEvent;
    }

    public async Task<AlertEventDocument?> GetAlertEventAsync(string eventId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.AlertEvents.AsNoTracking().FirstOrDefaultAsync(x => x.Id == eventId, cancellationToken);
    }

    public async Task<List<AlertEventDocument>> ListRecentAlertEventsAsync(int limit, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.AlertEvents.AsNoTracking().OrderByDescending(x => x.FiredAtUtc).Take(limit).ToListAsync(cancellationToken);
    }

    public async Task<List<AlertEventDocument>> ListAlertEventsByRuleAsync(string ruleId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.AlertEvents.AsNoTracking().Where(x => x.RuleId == ruleId).OrderByDescending(x => x.FiredAtUtc).Take(50).ToListAsync(cancellationToken);
    }

    public async Task<AlertEventDocument?> ResolveAlertEventAsync(string eventId, CancellationToken cancellationToken)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
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

        var succeeded = runs.Count(r => IsTerminalSuccessState(r.State));
        var failed = runs.Count(r => IsTerminalErrorState(r.State));
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
            var succeeded = repositoryRunsList.Count(r => IsTerminalSuccessState(r.State));
            var failed = repositoryRunsList.Count(r => IsTerminalErrorState(r.State));
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

    private static bool IsTerminalState(RunState state)
    {
        return state is RunState.Succeeded or RunState.Failed or RunState.Cancelled or RunState.Obsolete;
    }

    private static bool IsTerminalErrorState(RunState state)
    {
        return IsTerminalState(state) && state == RunState.Failed;
    }

    private static bool IsTerminalSuccessState(RunState state)
    {
        return IsTerminalState(state) && state == RunState.Succeeded;
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
                return JsonSerializer.Deserialize<double[]>(trimmed, (JsonSerializerOptions?)null);
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

    // ── Terminal Sessions ─────────────────────────────────────────────────────

    public async Task<TerminalSessionDocument> CreateTerminalSessionAsync(TerminalSessionDocument session, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        db.TerminalSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<TerminalSessionDocument?> GetTerminalSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.TerminalSessions.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == sessionId, cancellationToken);
    }

    public async Task<IReadOnlyList<TerminalSessionDocument>> ListActiveTerminalSessionsByWorkerAsync(string workerId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.TerminalSessions.AsNoTracking()
            .Where(x => x.WorkerId == workerId && (x.State == TerminalSessionState.Active || x.State == TerminalSessionState.Pending))
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TerminalSessionDocument>> ListTerminalSessionsByRunAsync(string runId, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await db.TerminalSessions.AsNoTracking()
            .Where(x => x.RunId == runId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task UpdateTerminalSessionAsync(TerminalSessionDocument session, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.TerminalSessions.FirstOrDefaultAsync(x => x.Id == session.Id, cancellationToken);
        if (existing is null)
            return;

        existing.WorkerId = session.WorkerId;
        existing.RunId = session.RunId;
        existing.State = session.State;
        existing.Cols = session.Cols;
        existing.Rows = session.Rows;
        existing.LastSeenAtUtc = session.LastSeenAtUtc;
        existing.ClosedAtUtc = session.ClosedAtUtc;
        existing.CloseReason = session.CloseReason;

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task CloseTerminalSessionAsync(string sessionId, string reason, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var session = await db.TerminalSessions.FirstOrDefaultAsync(x => x.Id == sessionId, cancellationToken);
        if (session is null)
            return;

        session.State = TerminalSessionState.Closed;
        session.ClosedAtUtc = DateTime.UtcNow;
        session.CloseReason = reason;

        await db.SaveChangesAsync(cancellationToken);
    }

    // ── Terminal Audit Events ────────────────────────────────────────────────

    public async Task AppendTerminalAuditEventAsync(TerminalAuditEventDocument auditEvent, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var maxSequence = await db.TerminalAuditEvents
            .Where(x => x.SessionId == auditEvent.SessionId)
            .MaxAsync(x => (long?)x.Sequence, cancellationToken) ?? 0;

        auditEvent.Sequence = maxSequence + 1;

        db.TerminalAuditEvents.Add(auditEvent);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TerminalAuditEventDocument>> GetTerminalAuditEventsAsync(string sessionId, long? afterSequence = null, int limit = 2000, CancellationToken cancellationToken = default)
    {
        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.TerminalAuditEvents.AsNoTracking()
            .Where(x => x.SessionId == sessionId);

        if (afterSequence.HasValue)
        {
            query = query.Where(x => x.Sequence > afterSequence.Value);
        }

        return await query
            .OrderBy(x => x.Sequence)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}
