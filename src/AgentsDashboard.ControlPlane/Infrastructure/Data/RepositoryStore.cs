using System.Text.RegularExpressions;

namespace AgentsDashboard.ControlPlane.Infrastructure.Data;

public sealed class RepositoryStore(
    IOrchestratorRepositorySessionFactory liteDbScopeFactory) : IRepositoryStore
{
    private static readonly Regex PromptSkillTriggerRegex = new("^[a-z0-9-]+$", RegexOptions.Compiled);
    private const string GlobalRepositoryScope = "global";

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


}
