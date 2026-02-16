using System.Globalization;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Proxy;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class RunDispatcher(
    IMagicOnionClientFactory clientFactory,
    IOrchestratorStore store,
    IWorkerLifecycleManager workerLifecycleManager,
    ISecretCryptoService secretCrypto,
    IRunEventPublisher publisher,
    InMemoryYarpConfigProvider yarpProvider,
    IOptions<OrchestratorOptions> orchestratorOptions,
    ILogger<RunDispatcher> logger,
    IOrchestratorRuntimeSettingsProvider? runtimeSettingsProvider = null)
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
        var runtime = runtimeSettingsProvider is null
            ? CreateFallbackRuntimeSettings(opts)
            : await runtimeSettingsProvider.GetAsync(cancellationToken);

        var queuedRuns = await store.CountRunsByStateAsync(RunState.Queued, cancellationToken);
        if (queuedRuns > runtime.MaxQueueDepth)
        {
            logger.LogWarning("Admission rejected for run {RunId}: queue depth {QueuedRuns} exceeds configured limit {Limit}", run.Id, queuedRuns, runtime.MaxQueueDepth);
            var rejected = await store.MarkRunCompletedAsync(
                run.Id,
                succeeded: false,
                summary: "Admission rejected: queue depth limit reached",
                outputJson: "{}",
                cancellationToken,
                failureClass: "AdmissionControl");
            if (rejected is not null)
            {
                await publisher.PublishStatusAsync(rejected, cancellationToken);
                await store.CreateFindingFromFailureAsync(rejected, "Admission control rejected the run due to queue depth policy.", cancellationToken);
            }

            return false;
        }

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

        var workerLease = await workerLifecycleManager.AcquireWorkerForDispatchAsync(cancellationToken);
        if (workerLease is null)
        {
            logger.LogWarning("No worker capacity available; leaving run {RunId} queued", run.Id);
            return false;
        }

        logger.LogDebug("Selected worker {WorkerId} for run {RunId}", workerLease.WorkerId, run.Id);

        var selectedWorker = await workerLifecycleManager.GetWorkerAsync(workerLease.WorkerId, cancellationToken);

        var defaultBranch = string.IsNullOrWhiteSpace(repository.DefaultBranch) ? "main" : repository.DefaultBranch;
        var taskBranch = BuildTaskBranchName(repository, task, run.Id);
        var layeredPrompt = await BuildLayeredPromptAsync(repository, task, taskBranch, defaultBranch, cancellationToken);

        var envVars = new Dictionary<string, string>
        {
            ["GIT_URL"] = repository.GitUrl,
            ["DEFAULT_BRANCH"] = defaultBranch,
            ["AUTO_CREATE_PR"] = task.AutoCreatePullRequest ? "true" : "false",
            ["HARNESS_NAME"] = task.Harness,
            ["GH_REPO"] = ParseGitHubRepoSlug(repository.GitUrl),
        };

        envVars["TASK_BRANCH"] = taskBranch;
        envVars["TASK_DEFAULT_BRANCH"] = defaultBranch;
        envVars["PR_BRANCH"] = taskBranch;
        var branchSeparator = taskBranch.LastIndexOf('/');
        envVars["PR_BRANCH_PREFIX"] = branchSeparator > 0 ? taskBranch[..branchSeparator] : taskBranch;
        envVars["PR_TITLE"] = $"[{task.Harness}] {task.Name} automated update";
        envVars["PR_BODY"] = $"Automated change from run {run.Id} for task {task.Name}.";

        var secrets = await store.ListProviderSecretsAsync(repository.Id, cancellationToken);
        var secretsDict = new Dictionary<string, string>();

        foreach (var secret in secrets)
        {
            try
            {
                var value = secretCrypto.Decrypt(secret.EncryptedValue);
                AddMappedProviderEnvironmentVariables(envVars, secretsDict, secret.Provider, value);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to decrypt provider secret for repository {RepositoryId} and provider {Provider}", repository.Id, secret.Provider);
            }
        }

        if (!envVars.ContainsKey("ANTHROPIC_AUTH_TOKEN") &&
            !envVars.ContainsKey("ANTHROPIC_API_KEY") &&
            !envVars.ContainsKey("Z_AI_API_KEY"))
        {
            var globalLlmTornadoSecret = await store.GetProviderSecretAsync("global", "llmtornado", cancellationToken);
            if (globalLlmTornadoSecret is not null)
            {
                try
                {
                    var value = secretCrypto.Decrypt(globalLlmTornadoSecret.EncryptedValue);
                    AddMappedProviderEnvironmentVariables(envVars, secretsDict, "llmtornado", value);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to decrypt global provider secret for llmtornado");
                }
            }
        }

        var isCodexTask = string.Equals(task.Harness, "codex", StringComparison.OrdinalIgnoreCase);
        var hasCodexCredentials = envVars.ContainsKey("CODEX_API_KEY") || envVars.ContainsKey("OPENAI_API_KEY");
        if (isCodexTask && !hasCodexCredentials)
        {
            var hostCodexApiKey = HostCredentialDiscovery.TryGetCodexApiKey();
            if (!string.IsNullOrWhiteSpace(hostCodexApiKey))
            {
                AddMappedProviderEnvironmentVariables(envVars, secretsDict, "codex", hostCodexApiKey);
                logger.LogInformation("Using host Codex credentials fallback for run {RunId}", run.Id);
            }
        }

        var harnessSettings = await store.GetHarnessProviderSettingsAsync(repository.Id, task.Harness, cancellationToken);
        if (harnessSettings is not null)
        {
            AddHarnessSettingsEnvironmentVariables(envVars, task.Harness, harnessSettings);
        }

        if (string.Equals(task.Harness, "zai", StringComparison.OrdinalIgnoreCase))
        {
            envVars["HARNESS_MODEL"] = "glm-5";
            envVars["ZAI_MODEL"] = "glm-5";
        }

        if (string.Equals(task.Harness, "claude-code", StringComparison.OrdinalIgnoreCase) &&
            envVars.TryGetValue("ANTHROPIC_BASE_URL", out var anthropicBaseUrl) &&
            anthropicBaseUrl.Contains("api.z.ai/api/anthropic", StringComparison.OrdinalIgnoreCase))
        {
            envVars["HARNESS_MODEL"] = "glm-5";
            envVars["CLAUDE_MODEL"] = "glm-5";
            envVars["ANTHROPIC_MODEL"] = "glm-5";
        }

        var artifactPatterns = task.ArtifactPatterns.Count > 0
            ? task.ArtifactPatterns.ToList()
            : null;

        var linkedFailureRuns = task.LinkedFailureRuns.Count > 0
            ? task.LinkedFailureRuns.ToList()
            : null;

        var request = new DispatchJobRequest
        {
            RunId = run.Id,
            ProjectId = project.Id,
            RepositoryId = repository.Id,
            TaskId = task.Id,
            HarnessType = task.Harness,
            ImageTag = $"harness-{task.Harness.ToLowerInvariant()}:latest",
            CloneUrl = repository.GitUrl,
            Branch = repository.DefaultBranch,
            WorkingDirectory = null,
            Instruction = layeredPrompt,
            EnvironmentVars = envVars,
            Secrets = secretsDict.Count > 0 ? secretsDict : null,
            ConcurrencyKey = null,
            TimeoutSeconds = task.Timeouts.ExecutionSeconds > 0
                ? Math.Min(task.Timeouts.ExecutionSeconds, runtime.RunHardTimeoutSeconds)
                : runtime.RunHardTimeoutSeconds,
            RetryCount = run.Attempt - 1,
            ArtifactPatterns = artifactPatterns,
            LinkedFailureRuns = linkedFailureRuns,
            CustomArgs = task.Command,
            DispatchedAt = DateTimeOffset.UtcNow,
            ContainerLabels = BuildContainerLabels(project, repository, task, run),
            Attempt = run.Attempt,
            SandboxProfileCpuLimit = task.SandboxProfile.CpuLimit > 0 ? task.SandboxProfile.CpuLimit : null,
            SandboxProfileMemoryLimit = ParseMemoryLimitToBytes(task.SandboxProfile.MemoryLimit),
            SandboxProfileNetworkDisabled = task.SandboxProfile.NetworkDisabled,
            SandboxProfileReadOnlyRootFs = task.SandboxProfile.ReadOnlyRootFs,
            ArtifactPolicyMaxArtifacts = task.ArtifactPolicy.MaxArtifacts,
            ArtifactPolicyMaxTotalSizeBytes = task.ArtifactPolicy.MaxTotalSizeBytes,
        };

        var workerClient = clientFactory.CreateWorkerGatewayService(workerLease.WorkerId, workerLease.GrpcEndpoint);
        var response = await workerClient.DispatchJobAsync(request);

        if (!response.Success)
        {
            logger.LogWarning("Worker rejected run {RunId}: {Reason}", run.Id, response.ErrorMessage);
            var failed = await store.MarkRunCompletedAsync(run.Id, false, $"Dispatch failed: {response.ErrorMessage}", "{}", cancellationToken);
            if (failed is not null)
            {
                await publisher.PublishStatusAsync(failed, cancellationToken);
                await store.CreateFindingFromFailureAsync(failed, response.ErrorMessage ?? "Unknown error", cancellationToken);
            }
            return false;
        }

        await workerLifecycleManager.RecordDispatchActivityAsync(workerLease.WorkerId, cancellationToken);

        var started = await store.MarkRunStartedAsync(
            run.Id,
            workerLease.WorkerId,
            cancellationToken,
            selectedWorker?.ImageRef,
            selectedWorker?.ImageDigest,
            selectedWorker?.ImageSource);
        if (started is not null)
        {
            await publisher.PublishStatusAsync(started, cancellationToken);

            var routePath = $"/proxy/runs/{run.Id}/{{**catchall}}";
            yarpProvider.UpsertRoute(
                $"run-{run.Id}",
                routePath,
                workerLease.ProxyEndpoint,
                TimeSpan.FromHours(2),
                project.Id,
                repository.Id,
                task.Id,
                run.Id);
            await publisher.PublishRouteAvailableAsync(run.Id, routePath, cancellationToken);
        }

        return true;
    }

    public async Task CancelAsync(string runId, CancellationToken cancellationToken)
    {
        try
        {
            var run = await store.GetRunAsync(runId, cancellationToken);
            if (run is null || string.IsNullOrWhiteSpace(run.WorkerId))
            {
                logger.LogWarning("Skipping cancel for run {RunId}: no assigned worker", runId);
                return;
            }

            var worker = await workerLifecycleManager.GetWorkerAsync(run.WorkerId, cancellationToken);
            if (worker is null || !worker.IsRunning)
            {
                logger.LogWarning("Skipping cancel for run {RunId}: worker {WorkerId} is unavailable", runId, run.WorkerId);
                return;
            }

            var workerClient = clientFactory.CreateWorkerGatewayService(worker.WorkerId, worker.GrpcEndpoint);
            await workerClient.CancelJobAsync(new CancelJobRequest { RunId = runId });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send cancel to worker for run {RunId}", runId);
        }
    }

    private static Dictionary<string, string> BuildContainerLabels(
        ProjectDocument project,
        RepositoryDocument repository,
        TaskDocument task,
        RunDocument run)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["orchestrator.run-id"] = run.Id,
            ["orchestrator.task-id"] = task.Id,
            ["orchestrator.repo-id"] = repository.Id,
            ["orchestrator.project-id"] = project.Id,
        };
    }

    private static long? ParseMemoryLimitToBytes(string memoryLimit)
    {
        if (string.IsNullOrWhiteSpace(memoryLimit))
        {
            return null;
        }

        var value = memoryLimit.Trim().ToLowerInvariant();
        if (!double.TryParse(
            new string(value.TakeWhile(ch => char.IsDigit(ch) || ch == '.').ToArray()),
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var number))
        {
            return null;
        }

        var suffix = value.SkipWhile(ch => char.IsDigit(ch) || ch == '.').ToArray();
        var unit = new string(suffix);
        var multiplier = unit switch
        {
            "k" or "kb" => 1024d,
            "m" or "mb" => 1024d * 1024d,
            "g" or "gb" => 1024d * 1024d * 1024d,
            "t" or "tb" => 1024d * 1024d * 1024d * 1024d,
            "" => 1d,
            _ => 1d
        };

        var bytes = number * multiplier;
        if (bytes <= 0)
        {
            return null;
        }

        return (long)bytes;
    }

    private static OrchestratorRuntimeSettings CreateFallbackRuntimeSettings(OrchestratorOptions options)
    {
        var minWorkers = 1;

        return new OrchestratorRuntimeSettings(
            MinWorkers: minWorkers,
            MaxWorkers: options.Workers.MaxWorkers,
            MaxProcessesPerWorker: 1,
            ReserveWorkers: 0,
            MaxQueueDepth: 200,
            QueueWaitTimeoutSeconds: 300,
            WorkerImagePolicy: WorkerImagePolicy.PreferLocal,
            ContainerImage: options.Workers.ContainerImage,
            ContainerNamePrefix: options.Workers.ContainerNamePrefix,
            DockerNetwork: options.Workers.DockerNetwork,
            ConnectivityMode: options.Workers.ConnectivityMode,
            WorkerImageRegistry: string.Empty,
            WorkerCanaryImage: string.Empty,
            WorkerDockerBuildContextPath: string.Empty,
            WorkerDockerfilePath: string.Empty,
            MaxConcurrentPulls: 2,
            MaxConcurrentBuilds: 1,
            ImagePullTimeoutSeconds: 120,
            ImageBuildTimeoutSeconds: 600,
            WorkerImageCacheTtlMinutes: 240,
            ImageFailureCooldownMinutes: 15,
            CanaryPercent: 10,
            MaxWorkerStartAttemptsPer10Min: 30,
            MaxFailedStartsPer10Min: 10,
            CooldownMinutes: 15,
            ContainerStartTimeoutSeconds: options.Workers.StartupTimeoutSeconds,
            ContainerStopTimeoutSeconds: 30,
            HealthProbeIntervalSeconds: 10,
            ContainerRestartLimit: 3,
            ContainerUnhealthyAction: ContainerUnhealthyAction.Recreate,
            OrchestratorErrorBurstThreshold: 20,
            OrchestratorErrorCoolDownMinutes: 10,
            EnableDraining: true,
            DrainTimeoutSeconds: 120,
            EnableAutoRecycle: true,
            RecycleAfterRuns: 200,
            RecycleAfterUptimeMinutes: 720,
            EnableContainerAutoCleanup: true,
            WorkerCpuLimit: string.Empty,
            WorkerMemoryLimitMb: 0,
            WorkerPidsLimit: 0,
            WorkerFileDescriptorLimit: 0,
            RunHardTimeoutSeconds: 3600,
            MaxRunLogMb: 50,
            EnablePressureScaling: options.Workers.EnablePressureScaling,
            CpuScaleOutThresholdPercent: options.Workers.CpuScaleOutThresholdPercent,
            MemoryScaleOutThresholdPercent: options.Workers.MemoryScaleOutThresholdPercent,
            PressureSampleWindowSeconds: options.Workers.PressureSampleWindowSeconds);
    }

    private async Task<string> BuildLayeredPromptAsync(
        RepositoryDocument repository,
        TaskDocument task,
        string taskBranch,
        string defaultBranch,
        CancellationToken cancellationToken)
    {
        var systemSettings = await store.GetSettingsAsync(cancellationToken);
        var globalPrefix = string.IsNullOrWhiteSpace(systemSettings.Orchestrator.TaskPromptPrefix)
            ? BuildRequiredTaskPrefix(taskBranch, defaultBranch)
            : systemSettings.Orchestrator.TaskPromptPrefix;
        var globalSuffix = string.IsNullOrWhiteSpace(systemSettings.Orchestrator.TaskPromptSuffix)
            ? BuildRequiredTaskSuffix(taskBranch)
            : systemSettings.Orchestrator.TaskPromptSuffix;

        var repoInstructionsFromCollection = await store.GetInstructionsAsync(repository.Id, cancellationToken);
        var enabledRepoInstructions = repoInstructionsFromCollection.Where(i => i.Enabled).OrderBy(i => i.Priority).ToList();
        var embeddedRepoInstructions = repository.InstructionFiles?.OrderBy(f => f.Order).ToList() ?? [];

        var taskInstructionFiles = task.InstructionFiles
            .OrderBy(f => f.Order)
            .ToList();

        var taskPrefix = ExtractTaskPromptWrapper(taskInstructionFiles, isPrefix: true);
        var taskSuffix = ExtractTaskPromptWrapper(taskInstructionFiles, isPrefix: false);
        var normalTaskInstructions = taskInstructionFiles
            .Where(f => !IsPromptWrapperInstruction(f.Name))
            .ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("--- [Required Prefix] ---");
        sb.AppendLine(globalPrefix);
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(taskPrefix))
        {
            sb.AppendLine("--- [Task Prefix] ---");
            sb.AppendLine(taskPrefix);
            sb.AppendLine();
        }

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

        if (normalTaskInstructions.Count > 0)
        {
            foreach (var file in normalTaskInstructions)
            {
                sb.AppendLine($"--- [Task] {file.Name} ---");
                sb.AppendLine(file.Content);
                sb.AppendLine();
            }
        }

        sb.AppendLine("--- Task Prompt ---");
        sb.AppendLine(task.Prompt);
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(taskSuffix))
        {
            sb.AppendLine("--- [Task Suffix] ---");
            sb.AppendLine(taskSuffix);
            sb.AppendLine();
        }

        sb.AppendLine("--- [Required Suffix] ---");
        sb.AppendLine(globalSuffix);

        return sb.ToString();
    }

    private static string BuildTaskBranchName(RepositoryDocument repository, TaskDocument task, string runId)
    {
        var repoSlug = ParseGitHubRepoSlug(repository.GitUrl);
        var repoSegment = repoSlug.Contains('/', StringComparison.Ordinal)
            ? repoSlug.Split('/')[1]
            : repository.Name;

        return $"agent/{SanitizeBranchSegment(repoSegment)}/{SanitizeBranchSegment(task.Name)}/{runId}";
    }

    private static string SanitizeBranchSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "task";
        }

        var sanitized = new string(value
            .Trim()
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_' ? ch : '-')
            .ToArray());

        var trimmed = sanitized.Trim('-');
        return string.IsNullOrWhiteSpace(trimmed) ? "task" : trimmed;
    }

    private static string BuildRequiredTaskPrefix(string taskBranch, string defaultBranch)
    {
        return $"""
                You must create and use a dedicated branch for this run before making any change.
                Execute these git steps at the start:
                1. git fetch --all
                2. git checkout {defaultBranch}
                3. git pull --ff-only origin {defaultBranch}
                4. git checkout -B {taskBranch}
                All edits and commits must happen only on branch {taskBranch}.
                Do not commit or push to {defaultBranch}.
                """;
    }

    private static string BuildRequiredTaskSuffix(string taskBranch)
    {
        return $"""
                Before finishing, analyze the current branch changes and prepare a precise commit message based on the diff.
                Required end steps:
                1. git status
                2. git diff --stat
                3. git diff
                4. create a commit message that matches the actual changes
                5. git add -A
                6. git commit -m "<generated-message>" (only if there are staged changes)
                7. git push -u origin {taskBranch}
                Keep all work on branch {taskBranch}.
                """;
    }

    private static bool IsPromptWrapperInstruction(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var normalized = NormalizePromptWrapperName(name);
        return normalized is "promptprefix" or "promptsuffix" or "taskpromptprefix" or "taskpromptsuffix";
    }

    private static string? ExtractTaskPromptWrapper(IEnumerable<InstructionFile> files, bool isPrefix)
    {
        var targets = isPrefix
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "promptprefix", "taskpromptprefix" }
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "promptsuffix", "taskpromptsuffix" };

        var file = files.FirstOrDefault(x => targets.Contains(NormalizePromptWrapperName(x.Name)));
        return file?.Content;
    }

    private static string NormalizePromptWrapperName(string value)
    {
        return new string(value
            .ToLowerInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
    }

    private static void AddMappedProviderEnvironmentVariables(
        Dictionary<string, string> envVars,
        Dictionary<string, string> secrets,
        string provider,
        string value)
    {
        var normalized = provider.Trim().ToLowerInvariant();

        switch (normalized)
        {
            case "github":
                SetSecret(envVars, secrets, "GH_TOKEN", value);
                SetSecret(envVars, secrets, "GITHUB_TOKEN", value);
                break;
            case "codex":
                SetSecret(envVars, secrets, "CODEX_API_KEY", value);
                SetSecret(envVars, secrets, "OPENAI_API_KEY", value);
                break;
            case "opencode":
                SetSecret(envVars, secrets, "OPENCODE_API_KEY", value);
                break;
            case "claude-code":
            case "claude code":
                SetSecret(envVars, secrets, "ANTHROPIC_API_KEY", value);
                break;
            case "zai":
                SetSecret(envVars, secrets, "Z_AI_API_KEY", value);
                SetSecret(envVars, secrets, "ANTHROPIC_AUTH_TOKEN", value);
                SetSecret(envVars, secrets, "ANTHROPIC_API_KEY", value);
                envVars["ANTHROPIC_BASE_URL"] = "https://api.z.ai/api/anthropic";
                break;
            case "llmtornado":
                SetSecret(envVars, secrets, "Z_AI_API_KEY", value);
                SetSecret(envVars, secrets, "ANTHROPIC_AUTH_TOKEN", value);
                SetSecret(envVars, secrets, "ANTHROPIC_API_KEY", value);
                envVars["ANTHROPIC_BASE_URL"] = "https://api.z.ai/api/anthropic";
                break;
            default:
                SetSecret(envVars, secrets, $"SECRET_{normalized.ToUpperInvariant().Replace('-', '_')}", value);
                break;
        }
    }

    private static void SetSecret(
        Dictionary<string, string> envVars,
        Dictionary<string, string> secrets,
        string key,
        string value)
    {
        envVars[key] = value;
        secrets[key] = value;
    }

    private static void AddHarnessSettingsEnvironmentVariables(
        Dictionary<string, string> envVars,
        string harness,
        HarnessProviderSettingsDocument settings)
    {
        var normalized = harness.Trim().ToLowerInvariant();
        var effectiveModel = settings.Model;

        if (normalized == "zai")
        {
            effectiveModel = "glm-5";
        }
        else if (normalized == "claude-code" &&
                 envVars.TryGetValue("ANTHROPIC_BASE_URL", out var anthropicBaseUrl) &&
                 anthropicBaseUrl.Contains("api.z.ai/api/anthropic", StringComparison.OrdinalIgnoreCase))
        {
            effectiveModel = "glm-5";
        }

        if (!string.IsNullOrWhiteSpace(effectiveModel))
        {
            envVars["HARNESS_MODEL"] = effectiveModel;
        }

        envVars["HARNESS_TEMPERATURE"] = settings.Temperature.ToString("F2");
        envVars["HARNESS_MAX_TOKENS"] = settings.MaxTokens.ToString();

        switch (normalized)
        {
            case "codex":
                if (!string.IsNullOrWhiteSpace(effectiveModel))
                    envVars["CODEX_MODEL"] = effectiveModel;
                envVars["CODEX_MAX_TOKENS"] = settings.MaxTokens.ToString();
                break;
            case "opencode":
                if (!string.IsNullOrWhiteSpace(effectiveModel))
                    envVars["OPENCODE_MODEL"] = effectiveModel;
                envVars["OPENCODE_TEMPERATURE"] = settings.Temperature.ToString("F2");
                break;
            case "claude-code":
                if (!string.IsNullOrWhiteSpace(effectiveModel))
                {
                    envVars["CLAUDE_MODEL"] = effectiveModel;
                    envVars["ANTHROPIC_MODEL"] = effectiveModel;
                }
                break;
            case "zai":
                envVars["HARNESS_MODEL"] = "glm-5";
                envVars["ZAI_MODEL"] = "glm-5";
                break;
        }

        foreach (var (key, value) in settings.AdditionalSettings)
        {
            var envKey = $"HARNESS_{key.ToUpperInvariant().Replace(' ', '_').Replace('-', '_')}";
            envVars[envKey] = value;
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
