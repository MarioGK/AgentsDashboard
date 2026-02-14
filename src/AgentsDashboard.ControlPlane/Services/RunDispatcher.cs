using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class RunDispatcher(
    WorkerGateway.WorkerGatewayClient workerClient,
    OrchestratorStore store,
    SecretCryptoService secretCrypto,
    IRunEventPublisher publisher,
    IOptions<OrchestratorOptions> orchestratorOptions,
    ILogger<RunDispatcher> logger)
{
    public async Task<bool> DispatchAsync(
        ProjectDocument project,
        RepositoryDocument repository,
        TaskDocument task,
        RunDocument run,
        CancellationToken cancellationToken)
    {
        if (task.ApprovalProfile.RequireApproval)
        {
            var pendingRun = await store.MarkRunPendingApprovalAsync(run.Id, cancellationToken);
            if (pendingRun is not null)
            {
                await publisher.PublishStatusAsync(pendingRun, cancellationToken);
                logger.LogInformation("Run {RunId} marked as pending approval", run.Id);
            }
            return true;
        }

        var opts = orchestratorOptions.Value;

        var globalActive = await store.CountActiveRunsAsync(cancellationToken);
        if (globalActive >= opts.MaxGlobalConcurrentRuns)
        {
            logger.LogWarning("Global concurrency limit reached ({Limit}), leaving run {RunId} queued", opts.MaxGlobalConcurrentRuns, run.Id);
            return false;
        }

        var projectActive = await store.CountActiveRunsByProjectAsync(project.Id, cancellationToken);
        if (projectActive >= opts.PerProjectConcurrencyLimit)
        {
            logger.LogWarning("Project concurrency limit reached for {ProjectId}, leaving run {RunId} queued", project.Id, run.Id);
            return false;
        }

        var repoActive = await store.CountActiveRunsByRepoAsync(repository.Id, cancellationToken);
        if (repoActive >= opts.PerRepoConcurrencyLimit)
        {
            logger.LogWarning("Repo concurrency limit reached for {RepositoryId}, leaving run {RunId} queued", repository.Id, run.Id);
            return false;
        }

        if (task.ConcurrencyLimit > 0)
        {
            var taskActive = await store.CountActiveRunsByTaskAsync(task.Id, cancellationToken);
            if (taskActive >= task.ConcurrencyLimit)
            {
                logger.LogWarning("Task concurrency limit reached for {TaskId} ({Limit}), leaving run {RunId} queued", task.Id, task.ConcurrencyLimit, run.Id);
                return false;
            }
        }

        var layeredPrompt = await BuildLayeredPromptAsync(repository, task, cancellationToken);

        var request = new DispatchJobRequest
        {
            RunId = run.Id,
            ProjectId = project.Id,
            RepositoryId = repository.Id,
            TaskId = task.Id,
            Harness = task.Harness,
            Command = task.Command,
            Prompt = layeredPrompt,
            TimeoutSeconds = task.Timeouts.ExecutionSeconds,
            Attempt = run.Attempt,
            SandboxProfileCpuLimit = task.SandboxProfile.CpuLimit,
            SandboxProfileMemoryLimit = task.SandboxProfile.MemoryLimit,
            SandboxProfileNetworkDisabled = task.SandboxProfile.NetworkDisabled,
            SandboxProfileReadOnlyRootFs = task.SandboxProfile.ReadOnlyRootFs,
            GitUrl = repository.GitUrl,
            ArtifactPolicyMaxArtifacts = task.ArtifactPolicy.MaxArtifacts,
            ArtifactPolicyMaxTotalSizeBytes = task.ArtifactPolicy.MaxTotalSizeBytes,
            ArtifactsPath = $"/data/artifacts/{run.Id}",
        };

        request.ContainerLabels.Add("orchestrator.run-id", run.Id);
        request.ContainerLabels.Add("orchestrator.task-id", task.Id);
        request.ContainerLabels.Add("orchestrator.repo-id", repository.Id);
        request.ContainerLabels.Add("orchestrator.project-id", project.Id);

        request.Env.Add("GIT_URL", repository.GitUrl);
        request.Env.Add("DEFAULT_BRANCH", repository.DefaultBranch);
        request.Env.Add("AUTO_CREATE_PR", task.AutoCreatePullRequest ? "true" : "false");
        request.Env.Add("HARNESS_NAME", task.Harness);
        request.Env.Add("GH_REPO", ParseGitHubRepoSlug(repository.GitUrl));
        request.Env.Add("PR_BRANCH", $"agent/{repository.Name}/{task.Name}/{run.Id[..8]}".ToLowerInvariant().Replace(' ', '-'));
        request.Env.Add("PR_TITLE", $"[{task.Harness}] {task.Name} automated update");
        request.Env.Add("PR_BODY", $"Automated change from run {run.Id} for task {task.Name}.");

        var secrets = await store.ListProviderSecretsAsync(repository.Id, cancellationToken);
        foreach (var secret in secrets)
        {
            try
            {
                var value = secretCrypto.Decrypt(secret.EncryptedValue);
                AddMappedProviderEnvironmentVariables(request, secret.Provider, value);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to decrypt provider secret for repository {RepositoryId} and provider {Provider}", repository.Id, secret.Provider);
            }
        }

        var harnessSettings = await store.GetHarnessProviderSettingsAsync(repository.Id, task.Harness, cancellationToken);
        if (harnessSettings is not null)
        {
            AddHarnessSettingsEnvironmentVariables(request, task.Harness, harnessSettings);
        }

        var response = await workerClient.DispatchJobAsync(request, cancellationToken: cancellationToken);

        if (!response.Accepted)
        {
            logger.LogWarning("Worker rejected run {RunId}: {Reason}", run.Id, response.Reason);
            var failed = await store.MarkRunCompletedAsync(run.Id, false, $"Dispatch failed: {response.Reason}", "{}", cancellationToken);
            if (failed is not null)
            {
                await publisher.PublishStatusAsync(failed, cancellationToken);
                await store.CreateFindingFromFailureAsync(failed, response.Reason, cancellationToken);
            }
            return false;
        }

        var started = await store.MarkRunStartedAsync(run.Id, cancellationToken);
        if (started is not null)
        {
            await publisher.PublishStatusAsync(started, cancellationToken);
        }

        return true;
    }

    public async Task CancelAsync(string runId, CancellationToken cancellationToken)
    {
        try
        {
            await workerClient.CancelJobAsync(new CancelJobRequest { RunId = runId }, cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send cancel to worker for run {RunId}", runId);
        }
    }

    private async Task<string> BuildLayeredPromptAsync(RepositoryDocument repository, TaskDocument task, CancellationToken cancellationToken)
    {
        var repoInstructionsFromCollection = await store.GetInstructionsAsync(repository.Id, cancellationToken);
        var enabledRepoInstructions = repoInstructionsFromCollection.Where(i => i.Enabled).OrderBy(i => i.Priority).ToList();
        var embeddedRepoInstructions = repository.InstructionFiles?.OrderBy(f => f.Order).ToList() ?? [];
        var hasTaskInstructions = task.InstructionFiles.Count > 0;

        if (enabledRepoInstructions.Count == 0 && embeddedRepoInstructions.Count == 0 && !hasTaskInstructions)
            return task.Prompt;

        var sb = new System.Text.StringBuilder();

        if (enabledRepoInstructions.Count > 0)
        {
            foreach (var file in enabledRepoInstructions)
            {
                sb.AppendLine($"--- [Repository Collection] {file.Name} ---");
                sb.AppendLine(file.Content);
                sb.AppendLine();
            }
        }

        if (embeddedRepoInstructions.Count > 0)
        {
            foreach (var file in embeddedRepoInstructions)
            {
                sb.AppendLine($"--- [Repository] {file.Name} ---");
                sb.AppendLine(file.Content);
                sb.AppendLine();
            }
        }

        if (hasTaskInstructions)
        {
            foreach (var file in task.InstructionFiles.OrderBy(f => f.Order))
            {
                sb.AppendLine($"--- [Task] {file.Name} ---");
                sb.AppendLine(file.Content);
                sb.AppendLine();
            }
        }

        sb.AppendLine("--- Task Prompt ---");
        sb.AppendLine(task.Prompt);
        return sb.ToString();
    }

    private static void AddMappedProviderEnvironmentVariables(DispatchJobRequest request, string provider, string value)
    {
        var normalized = provider.Trim().ToLowerInvariant();

        switch (normalized)
        {
            case "github":
                request.Env["GH_TOKEN"] = value;
                request.Env["GITHUB_TOKEN"] = value;
                break;
            case "codex":
                request.Env["CODEX_API_KEY"] = value;
                break;
            case "opencode":
                request.Env["OPENCODE_API_KEY"] = value;
                break;
            case "claude-code":
            case "claude code":
                request.Env["ANTHROPIC_API_KEY"] = value;
                break;
            case "zai":
                request.Env["Z_AI_API_KEY"] = value;
                break;
            default:
                request.Env[$"SECRET_{normalized.ToUpperInvariant().Replace('-', '_')}"] = value;
                break;
        }
    }

    private static void AddHarnessSettingsEnvironmentVariables(DispatchJobRequest request, string harness, HarnessProviderSettingsDocument settings)
    {
        var normalized = harness.Trim().ToLowerInvariant();

        if (!string.IsNullOrWhiteSpace(settings.Model))
        {
            request.Env["HARNESS_MODEL"] = settings.Model;
        }

        request.Env["HARNESS_TEMPERATURE"] = settings.Temperature.ToString("F2");
        request.Env["HARNESS_MAX_TOKENS"] = settings.MaxTokens.ToString();

        switch (normalized)
        {
            case "codex":
                if (!string.IsNullOrWhiteSpace(settings.Model))
                    request.Env["CODEX_MODEL"] = settings.Model;
                request.Env["CODEX_MAX_TOKENS"] = settings.MaxTokens.ToString();
                break;
            case "opencode":
                if (!string.IsNullOrWhiteSpace(settings.Model))
                    request.Env["OPENCODE_MODEL"] = settings.Model;
                request.Env["OPENCODE_TEMPERATURE"] = settings.Temperature.ToString("F2");
                break;
            case "claude-code":
                if (!string.IsNullOrWhiteSpace(settings.Model))
                {
                    request.Env["CLAUDE_MODEL"] = settings.Model;
                    request.Env["ANTHROPIC_MODEL"] = settings.Model;
                }
                break;
            case "zai":
                if (!string.IsNullOrWhiteSpace(settings.Model))
                    request.Env["ZAI_MODEL"] = settings.Model;
                break;
        }

        foreach (var (key, value) in settings.AdditionalSettings)
        {
            var envKey = $"HARNESS_{key.ToUpperInvariant().Replace(' ', '_')}";
            request.Env[envKey] = value;
        }
    }

    private static string ParseGitHubRepoSlug(string gitUrl)
    {
        if (string.IsNullOrWhiteSpace(gitUrl))
            return string.Empty;

        var normalized = gitUrl.Trim();
        if (normalized.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Replace("git@github.com:", string.Empty, StringComparison.OrdinalIgnoreCase);
        else if (normalized.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Replace("https://github.com/", string.Empty, StringComparison.OrdinalIgnoreCase);

        if (normalized.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[..^4];

        return normalized.Trim('/');
    }
}
