using System.Globalization;


using AgentsDashboard.ControlPlane;


using Microsoft.Extensions.Options;

namespace AgentsDashboard.ControlPlane.Features.Runs.Services;

public sealed class RunDispatcher(
    IMagicOnionClientFactory clientFactory,
    IRepositoryStore repositoryStore,
    ITaskStore taskStore,
    IRunStore runStore,
    ISystemStore systemStore,
    ITaskRuntimeLifecycleManager workerLifecycleManager,
    ISecretCryptoService secretCrypto,
    IRunEventPublisher publisher,
    IOptions<OrchestratorOptions> orchestratorOptions,
    ILogger<RunDispatcher> logger,
    IOrchestratorRuntimeSettingsProvider? runtimeSettingsProvider = null)
{
    private const int TaskRunWindowLimit = 500;

    public async Task<bool> DispatchAsync(
        RepositoryDocument repository,
        TaskDocument task,
        RunDocument run,
        CancellationToken cancellationToken,
        IReadOnlyList<DispatchInputPart>? inputParts = null,
        IReadOnlyList<DispatchImageAttachment>? imageAttachments = null,
        bool preferNativeMultimodal = true,
        string multimodalFallbackPolicy = "auto-text-reference",
        string? sessionProfileId = null,
        string? instructionStackHash = null,
        string? mcpConfigSnapshotJson = null)
    {
        logger.LogInformation(
            "Dispatch request repo={RepositoryId} task={TaskId} run={RunId} harness={Harness} mode={Mode} protocol={Protocol} sessionProfile={SessionProfileId} requireApproval={RequireApproval} linkedFailures={LinkedFailureRuns} artifactPatterns={ArtifactPatterns}",
            repository.Id,
            task.Id,
            run.Id,
            task.Harness,
            run.ExecutionMode,
            run.StructuredProtocol,
            run.SessionProfileId ?? "none",
            task.ApprovalProfile.RequireApproval,
            task.LinkedFailureRuns.Count,
            task.ArtifactPatterns.Count);

        var taskQueueHead = await GetTaskQueueHeadAsync(task.Id, cancellationToken);
        if (taskQueueHead is not null &&
            !string.Equals(taskQueueHead.Id, run.Id, StringComparison.Ordinal))
        {
            logger.LogInformation(
                "Task queue head is run {HeadRunId} for task {TaskId}; leaving run {RunId} queued",
                taskQueueHead.Id,
                task.Id,
                run.Id);
            return false;
        }

        if (task.ApprovalProfile.RequireApproval)
        {
            var pendingRun = await runStore.MarkRunPendingApprovalAsync(run.Id, cancellationToken);
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

        var queuedRuns = await runStore.CountRunsByStateAsync(RunState.Queued, cancellationToken);
        if (queuedRuns > runtime.MaxQueueDepth)
        {
            logger.LogWarning("Admission rejected for run {RunId}: queue depth {QueuedRuns} exceeds configured limit {Limit}", run.Id, queuedRuns, runtime.MaxQueueDepth);
            var rejected = await runStore.MarkRunCompletedAsync(
                run.Id,
                succeeded: false,
                summary: "Admission rejected: queue depth limit reached",
                outputJson: "{}",
                cancellationToken,
                failureClass: "AdmissionControl");
            if (rejected is not null)
            {
                await publisher.PublishStatusAsync(rejected, cancellationToken);
            }

            return false;
        }

        var globalActive = await runStore.CountActiveRunsAsync(cancellationToken);
        if (globalActive >= opts.MaxGlobalConcurrentRuns)
        {
            logger.LogWarning("Global concurrency limit reached ({Limit}), leaving run {RunId} queued", opts.MaxGlobalConcurrentRuns, run.Id);
            return false;
        }

        var repoActive = await runStore.CountActiveRunsByRepoAsync(repository.Id, cancellationToken);
        if (repoActive >= opts.PerRepoConcurrencyLimit)
        {
            logger.LogWarning("Repo concurrency limit reached for {RepositoryId}, leaving run {RunId} queued", repository.Id, run.Id);
            return false;
        }

        var defaultBranch = string.IsNullOrWhiteSpace(repository.DefaultBranch) ? "main" : repository.DefaultBranch;

        if (!TryNormalizeCloneUrl(repository.GitUrl, out var normalizedCloneUrl, out var cloneUrlError))
        {
            logger.LogWarning(
                "Rejecting run {RunId}: repository URL is invalid ({Reason})",
                run.Id,
                cloneUrlError);

            var failed = await runStore.MarkRunCompletedAsync(
                run.Id,
                succeeded: false,
                summary: "Dispatch failed: invalid repository URL",
                outputJson: "{}",
                cancellationToken,
                failureClass: "InvalidRepositoryUrl");
            if (failed is not null)
            {
                await publisher.PublishStatusAsync(failed, cancellationToken);
            }

            return false;
        }

        var taskParallelSlots = task.ConcurrencyLimit > 0
            ? task.ConcurrencyLimit
            : Math.Max(1, runtime.DefaultTaskParallelRuns);
        var workerLease = await workerLifecycleManager.AcquireTaskRuntimeForDispatchAsync(
            repository.Id,
            task.Id,
            taskParallelSlots,
            cancellationToken);
        if (workerLease is null)
        {
            logger.LogWarning("No worker capacity available; leaving run {RunId} queued", run.Id);
            return false;
        }

        logger.LogDebug("Selected worker {TaskRuntimeId} for run {RunId}", workerLease.TaskRuntimeId, run.Id);

        var selectedWorker = await workerLifecycleManager.GetTaskRuntimeAsync(workerLease.TaskRuntimeId, cancellationToken);

        try
        {
            await taskStore.UpdateTaskGitMetadataAsync(
                task.Id,
                null,
                string.Empty,
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist task git metadata for task {TaskId}", task.Id);
        }

        var layeredPrompt = await BuildLayeredPromptAsync(repository, task, defaultBranch, cancellationToken);

        var envVars = new Dictionary<string, string>
        {
            ["GIT_URL"] = normalizedCloneUrl,
            ["DEFAULT_BRANCH"] = defaultBranch,
            ["AUTO_CREATE_PR"] = "false",
            ["HARNESS_NAME"] = task.Harness,
            ["HARNESS_MODE"] = run.ExecutionMode.ToString().ToLowerInvariant(),
            ["GH_REPO"] = ParseGitHubRepoSlug(normalizedCloneUrl),
        };

        var secrets = await repositoryStore.ListProviderSecretsAsync(repository.Id, cancellationToken);
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

        var harnessSettings = await repositoryStore.GetHarnessProviderSettingsAsync(repository.Id, task.Harness, cancellationToken);
        if (harnessSettings is not null)
        {
            AddHarnessSettingsEnvironmentVariables(envVars, task.Harness, harnessSettings);
        }

        var modelOverride = ResolveTaskModelOverride(task);
        if (!string.IsNullOrWhiteSpace(modelOverride))
        {
            ApplyHarnessModelOverride(task.Harness, envVars, modelOverride);
        }

        TryApplyHostGitHubTokenFallback(envVars, secretsDict);

        ApplyHarnessModeEnvironment(task.Harness, run.ExecutionMode, envVars);

        if (IsGitHubCloneUrl(normalizedCloneUrl) && !HasGitHubCredentials(envVars))
        {
            logger.LogWarning(
                "No GitHub token is available for run {RunId}; set repository secret 'github' or environment GH_TOKEN/GITHUB_TOKEN for private GitHub clone reliability.",
                run.Id);
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
            RepositoryId = repository.Id,
            TaskId = task.Id,
            HarnessType = task.Harness,
            ImageTag = $"harness-{task.Harness.ToLowerInvariant()}:latest",
            CloneUrl = normalizedCloneUrl,
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
            ContainerLabels = BuildContainerLabels(repository, task, run),
            Attempt = run.Attempt,
            SandboxProfileCpuLimit = task.SandboxProfile.CpuLimit > 0 ? task.SandboxProfile.CpuLimit : null,
            SandboxProfileMemoryLimit = ParseMemoryLimitToBytes(task.SandboxProfile.MemoryLimit),
            SandboxProfileNetworkDisabled = task.SandboxProfile.NetworkDisabled,
            SandboxProfileReadOnlyRootFs = task.SandboxProfile.ReadOnlyRootFs,
            ArtifactPolicyMaxArtifacts = task.ArtifactPolicy.MaxArtifacts,
            ArtifactPolicyMaxTotalSizeBytes = task.ArtifactPolicy.MaxTotalSizeBytes,
            Mode = run.ExecutionMode,
            StructuredProtocolVersion = run.StructuredProtocol,
            InputParts = inputParts is { Count: > 0 } ? [.. inputParts] : null,
            ImageAttachments = imageAttachments is { Count: > 0 } ? [.. imageAttachments] : null,
            PreferNativeMultimodal = preferNativeMultimodal,
            MultimodalFallbackPolicy = string.IsNullOrWhiteSpace(multimodalFallbackPolicy)
                ? "auto-text-reference"
                : multimodalFallbackPolicy.Trim(),
            SessionProfileId = sessionProfileId?.Trim() ?? run.SessionProfileId ?? string.Empty,
            InstructionStackHash = instructionStackHash?.Trim() ?? run.InstructionStackHash ?? string.Empty,
            McpConfigSnapshotJson = mcpConfigSnapshotJson?.Trim() ?? run.McpConfigSnapshotJson ?? string.Empty,
        };

        var workerClient = clientFactory.CreateTaskRuntimeService(workerLease.TaskRuntimeId, workerLease.GrpcEndpoint);
        var response = await workerClient.WithCancellationToken(cancellationToken).DispatchJobAsync(request);

        if (!response.Success)
        {
            logger.LogWarning("Worker rejected run {RunId}: {Reason}", run.Id, response.ErrorMessage);
            var failed = await runStore.MarkRunCompletedAsync(run.Id, false, $"Dispatch failed: {response.ErrorMessage}", "{}", cancellationToken);
            if (failed is not null)
            {
                await publisher.PublishStatusAsync(failed, cancellationToken);
            }
            return false;
        }

        await workerLifecycleManager.RecordDispatchActivityAsync(workerLease.TaskRuntimeId, cancellationToken);

        var started = await runStore.MarkRunStartedAsync(
            run.Id,
            workerLease.TaskRuntimeId,
            cancellationToken,
            selectedWorker?.ImageRef,
            selectedWorker?.ImageDigest,
            selectedWorker?.ImageSource);
        if (started is not null)
        {
            await publisher.PublishStatusAsync(started, cancellationToken);
        }

        return true;
    }

    public async Task<bool> DispatchNextQueuedRunForTaskAsync(string taskId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return false;
        }

        var task = await taskStore.GetTaskAsync(taskId, cancellationToken);
        if (task is null)
        {
            return false;
        }

        var repository = await repositoryStore.GetRepositoryAsync(task.RepositoryId, cancellationToken);
        if (repository is null)
        {
            return false;
        }

        var taskRuns = await runStore.ListRunsByTaskAsync(taskId, TaskRunWindowLimit, cancellationToken) ?? [];
        var nextQueuedRun = taskRuns
            .Where(x => x.State == RunState.Queued)
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .FirstOrDefault();

        if (nextQueuedRun is null)
        {
            return false;
        }

        return await DispatchAsync(repository, task, nextQueuedRun, cancellationToken);
    }

    public async Task CancelAsync(string runId, CancellationToken cancellationToken)
    {
        try
        {
            var run = await runStore.GetRunAsync(runId, cancellationToken);
            if (run is null || string.IsNullOrWhiteSpace(run.TaskRuntimeId))
            {
                logger.LogWarning("Skipping cancel for run {RunId}: no assigned worker", runId);
                return;
            }

            var worker = await workerLifecycleManager.GetTaskRuntimeAsync(run.TaskRuntimeId, cancellationToken);
            if (worker is null || !worker.IsRunning)
            {
                logger.LogWarning("Skipping cancel for run {RunId}: worker {TaskRuntimeId} is unavailable", runId, run.TaskRuntimeId);
                return;
            }

            var workerClient = clientFactory.CreateTaskRuntimeService(worker.TaskRuntimeId, worker.GrpcEndpoint);
            await workerClient.WithCancellationToken(cancellationToken).StopJobAsync(new StopJobRequest { RunId = runId });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send cancel to worker for run {RunId}", runId);
        }
    }

    private async Task<RunDocument?> GetTaskQueueHeadAsync(string taskId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return null;
        }

        var taskRuns = await runStore.ListRunsByTaskAsync(taskId, TaskRunWindowLimit, cancellationToken) ?? [];
        return taskRuns
            .Where(x => IsTaskQueueState(x.State))
            .OrderBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static bool IsTaskQueueState(RunState state)
    {
        return state is RunState.Queued or RunState.Running or RunState.PendingApproval;
    }

    private static Dictionary<string, string> BuildContainerLabels(
        RepositoryDocument repository,
        TaskDocument task,
        RunDocument run)
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["orchestrator.run-id"] = run.Id,
            ["orchestrator.task-id"] = task.Id,
            ["orchestrator.repo-id"] = repository.Id,
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
            MaxActiveTaskRuntimes: options.TaskRuntimes.MaxTaskRuntimes,
            DefaultTaskParallelRuns: 1,
            TaskRuntimeInactiveTimeoutMinutes: options.TaskRuntimes.IdleTimeoutMinutes,
            MinWorkers: minWorkers,
            MaxWorkers: options.TaskRuntimes.MaxTaskRuntimes,
            MaxProcessesPerWorker: 1,
            ReserveWorkers: 0,
            MaxQueueDepth: 200,
            QueueWaitTimeoutSeconds: 300,
            TaskRuntimeImagePolicy: TaskRuntimeImagePolicy.PreferLocal,
            ContainerImage: options.TaskRuntimes.ContainerImage,
            ContainerNamePrefix: options.TaskRuntimes.ContainerNamePrefix,
            DockerNetwork: options.TaskRuntimes.DockerNetwork,
            ConnectivityMode: options.TaskRuntimes.ConnectivityMode,
            TaskRuntimeImageRegistry: string.Empty,
            TaskRuntimeCanaryImage: string.Empty,
            WorkerDockerBuildContextPath: string.Empty,
            WorkerDockerfilePath: string.Empty,
            MaxConcurrentPulls: 2,
            MaxConcurrentBuilds: 1,
            ImagePullTimeoutSeconds: 120,
            ImageBuildTimeoutSeconds: 600,
            TaskRuntimeImageCacheTtlMinutes: 240,
            ImageFailureCooldownMinutes: 15,
            CanaryPercent: 10,
            MaxWorkerStartAttemptsPer10Min: 30,
            MaxFailedStartsPer10Min: 10,
            CooldownMinutes: 15,
            ContainerStartTimeoutSeconds: options.TaskRuntimes.StartupTimeoutSeconds,
            ContainerStopTimeoutSeconds: 30,
            HealthProbeIntervalSeconds: 10,
            RuntimeHeartbeatStaleSeconds: 60,
            RuntimeProbeFailureThreshold: 2,
            RuntimeRemediationCooldownSeconds: 30,
            RuntimeReadinessDegradeSeconds: 45,
            RuntimeReadinessFailureRatioPercent: 30,
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
            EnablePressureScaling: options.TaskRuntimes.EnablePressureScaling,
            CpuScaleOutThresholdPercent: options.TaskRuntimes.CpuScaleOutThresholdPercent,
            MemoryScaleOutThresholdPercent: options.TaskRuntimes.MemoryScaleOutThresholdPercent,
            PressureSampleWindowSeconds: options.TaskRuntimes.PressureSampleWindowSeconds,
            EnableHostSshPassthrough: options.TaskRuntimes.EnableHostSshPassthrough,
            HostSshDirectory: options.TaskRuntimes.HostSshDirectory,
            HostSshAgentSocketPath: options.TaskRuntimes.HostSshAgentSocketPath,
            GitSshCommandMode: options.TaskRuntimes.GitSshCommandMode);
    }

    private async Task<string> BuildLayeredPromptAsync(
        RepositoryDocument repository,
        TaskDocument task,
        string defaultBranch,
        CancellationToken cancellationToken)
    {
        var systemSettings = await systemStore.GetSettingsAsync(cancellationToken);
        var globalPrefix = string.IsNullOrWhiteSpace(systemSettings.Orchestrator.TaskPromptPrefix)
            ? BuildRequiredTaskPrefix(defaultBranch)
            : systemSettings.Orchestrator.TaskPromptPrefix;
        var globalSuffix = string.IsNullOrWhiteSpace(systemSettings.Orchestrator.TaskPromptSuffix)
            ? BuildRequiredTaskSuffix(defaultBranch)
            : systemSettings.Orchestrator.TaskPromptSuffix;

        var repoInstructionsFromCollection = await repositoryStore.GetInstructionsAsync(repository.Id, cancellationToken);
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

    private static string BuildRequiredTaskPrefix(string defaultBranch)
    {
        return $"""
                Execute these git steps at the start:
                1. git fetch --all
                2. git checkout {defaultBranch}
                3. git pull --ff-only origin {defaultBranch}
                Keep all edits on {defaultBranch}.
                """;
    }

    private static string BuildRequiredTaskSuffix(string defaultBranch)
    {
        return $"""
                Required end steps:
                1. git status
                2. git diff --stat
                3. git diff
                4. create a commit message that matches the actual changes
                5. git add -A
                6. git commit -m "<generated-message>" (only if there are staged changes)
                7. git push origin {defaultBranch}
                Keep all work on branch {defaultBranch}.
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
            default:
                SetSecret(envVars, secrets, $"SECRET_{normalized.ToUpperInvariant().Replace('-', '_')}", value);
                break;
        }
    }

    private static bool TryApplyHostGitHubTokenFallback(
        Dictionary<string, string> envVars,
        Dictionary<string, string> secrets)
    {
        if (HasGitHubCredentials(envVars))
        {
            return false;
        }

        var ghToken = Environment.GetEnvironmentVariable("GH_TOKEN")?.Trim();
        var githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN")?.Trim();
        if (string.IsNullOrWhiteSpace(ghToken) && string.IsNullOrWhiteSpace(githubToken))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(ghToken))
        {
            ghToken = githubToken;
        }

        if (string.IsNullOrWhiteSpace(githubToken))
        {
            githubToken = ghToken;
        }

        if (string.IsNullOrWhiteSpace(ghToken) || string.IsNullOrWhiteSpace(githubToken))
        {
            return false;
        }

        SetSecret(envVars, secrets, "GH_TOKEN", ghToken);
        SetSecret(envVars, secrets, "GITHUB_TOKEN", githubToken);
        return true;
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
        }

        foreach (var (key, value) in settings.AdditionalSettings)
        {
            var envKey = $"HARNESS_{key.ToUpperInvariant().Replace(' ', '_').Replace('-', '_')}";
            envVars[envKey] = value;
        }
    }

    private static string ResolveTaskModelOverride(TaskDocument task)
    {
        if (task.InstructionFiles.Count == 0)
        {
            return string.Empty;
        }

        foreach (var instruction in task.InstructionFiles)
        {
            if (instruction.Content.Length == 0)
            {
                continue;
            }

            var normalizedName = NormalizePromptWrapperName(instruction.Name);
            if (normalizedName is "modeloverride" or "harnessmodel")
            {
                return instruction.Content.Trim();
            }
        }

        return string.Empty;
    }

    private static void ApplyHarnessModelOverride(string harness, Dictionary<string, string> envVars, string modelOverride)
    {
        var normalizedHarness = harness.Trim().ToLowerInvariant();
        var model = modelOverride.Trim();
        if (model.Length == 0)
        {
            return;
        }

        envVars["HARNESS_MODEL"] = model;

        switch (normalizedHarness)
        {
            case "codex":
                envVars["CODEX_MODEL"] = model;
                break;
            case "opencode":
                envVars["OPENCODE_MODEL"] = model;
                break;
        }
    }

    private static void ApplyHarnessModeEnvironment(string harness, HarnessExecutionMode mode, IDictionary<string, string> envVars)
    {
        var modeValue = mode.ToString().ToLowerInvariant();
        envVars["TASK_MODE"] = modeValue;
        envVars["RUN_MODE"] = modeValue;

        if (string.Equals(harness, "codex", StringComparison.OrdinalIgnoreCase))
        {
            var approvalPolicy = mode is HarnessExecutionMode.Plan or HarnessExecutionMode.Review
                ? "never"
                : "on-failure";
            SetIfMissing(envVars, "CODEX_APPROVAL_POLICY", approvalPolicy);
            return;
        }

        if (string.Equals(harness, "opencode", StringComparison.OrdinalIgnoreCase))
        {
            SetIfMissing(envVars, "OPENCODE_MODE", modeValue);
            return;
        }

    }

    private static void SetIfMissing(IDictionary<string, string> envVars, string key, string value)
    {
        var existingKey = FindKeyIgnoreCase(envVars.Keys, key);
        if (existingKey is not null)
        {
            return;
        }

        envVars[key] = value;
    }

    private static string? FindKeyIgnoreCase(IEnumerable<string> envKeys, string key)
    {
        foreach (var existingKey in envKeys)
        {
            if (string.Equals(existingKey, key, StringComparison.OrdinalIgnoreCase))
            {
                return existingKey;
            }
        }

        return null;
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

    private static bool TryNormalizeCloneUrl(string? gitUrl, out string normalizedUrl, out string reason)
    {
        normalizedUrl = string.Empty;
        reason = string.Empty;

        if (string.IsNullOrWhiteSpace(gitUrl))
        {
            reason = "Repository URL is missing";
            return false;
        }

        var trimmed = gitUrl.Trim();
        if (IsScpStyleGitUrl(trimmed) || IsSupportedUrl(trimmed))
        {
            normalizedUrl = trimmed;
            return true;
        }

        reason = "Repository URL is not a supported clone URL format";
        return false;
    }

    private static bool IsSupportedUrl(string gitUrl)
    {
        if (string.IsNullOrWhiteSpace(gitUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(gitUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!uri.IsWellFormedOriginalString())
        {
            return false;
        }

        return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri.Scheme, Uri.UriSchemeSsh, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri.Scheme, "git", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri.Scheme, "git+ssh", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsScpStyleGitUrl(string gitUrl)
    {
        if (string.IsNullOrWhiteSpace(gitUrl))
        {
            return false;
        }

        if (Uri.TryCreate(gitUrl, UriKind.Absolute, out _))
        {
            return false;
        }

        if (gitUrl.Contains(' '))
        {
            return false;
        }

        var atIndex = gitUrl.IndexOf('@', StringComparison.Ordinal);
        if (atIndex <= 0)
        {
            return false;
        }

        var colonIndex = gitUrl.IndexOf(':', StringComparison.Ordinal);
        if (colonIndex <= atIndex)
        {
            return false;
        }

        var host = gitUrl[(atIndex + 1)..colonIndex];
        return !string.IsNullOrWhiteSpace(host) && !host.Contains('/');
    }

    private static bool IsGitHubCloneUrl(string gitUrl)
    {
        if (string.IsNullOrWhiteSpace(gitUrl))
        {
            return false;
        }

        if (IsScpStyleGitUrl(gitUrl))
        {
            var atIndex = gitUrl.IndexOf('@', StringComparison.Ordinal);
            var colonIndex = gitUrl.IndexOf(':', StringComparison.Ordinal);
            if (atIndex < 0 || colonIndex <= atIndex)
            {
                return false;
            }

            var host = gitUrl[(atIndex + 1)..colonIndex];
            return string.Equals(host, "github.com", StringComparison.OrdinalIgnoreCase);
        }

        if (!Uri.TryCreate(gitUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasGitHubCredentials(IReadOnlyDictionary<string, string> envVars)
    {
        var ghTokenKey = FindKeyIgnoreCase(envVars.Keys, "GH_TOKEN");
        if (!string.IsNullOrWhiteSpace(ghTokenKey) && !string.IsNullOrWhiteSpace(envVars[ghTokenKey]))
        {
            return true;
        }

        var githubTokenKey = FindKeyIgnoreCase(envVars.Keys, "GITHUB_TOKEN");
        return !string.IsNullOrWhiteSpace(githubTokenKey) && !string.IsNullOrWhiteSpace(envVars[githubTokenKey]);
    }
}
