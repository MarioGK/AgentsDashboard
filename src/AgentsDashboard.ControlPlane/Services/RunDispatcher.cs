using System.Globalization;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.TaskRuntime;
using AgentsDashboard.ControlPlane;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class RunDispatcher(
    IMagicOnionClientFactory clientFactory,
    IOrchestratorStore store,
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
        string? mcpConfigSnapshotJson = null,
        string? automationRunId = null)
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

        var repoActive = await store.CountActiveRunsByRepoAsync(repository.Id, cancellationToken);
        if (repoActive >= opts.PerRepoConcurrencyLimit)
        {
            logger.LogWarning("Repo concurrency limit reached for {RepositoryId}, leaving run {RunId} queued", repository.Id, run.Id);
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

        var defaultBranch = string.IsNullOrWhiteSpace(repository.DefaultBranch) ? "main" : repository.DefaultBranch;

        try
        {
            await store.UpdateTaskGitMetadataAsync(
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
            ["GIT_URL"] = repository.GitUrl,
            ["DEFAULT_BRANCH"] = defaultBranch,
            ["AUTO_CREATE_PR"] = "false",
            ["HARNESS_NAME"] = task.Harness,
            ["HARNESS_MODE"] = run.ExecutionMode.ToString().ToLowerInvariant(),
            ["HARNESS_EXECUTION_MODE"] = run.ExecutionMode.ToString().ToLowerInvariant(),
            ["GH_REPO"] = ParseGitHubRepoSlug(repository.GitUrl),
        };

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

        var modelOverride = ResolveTaskModelOverride(task);
        if (!string.IsNullOrWhiteSpace(modelOverride))
        {
            ApplyHarnessModelOverride(task.Harness, envVars, modelOverride);
        }

        ApplyHarnessModeEnvironment(task.Harness, run.ExecutionMode, envVars);

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
            AutomationRunId = automationRunId?.Trim() ?? run.AutomationRunId ?? string.Empty,
        };

        var workerClient = clientFactory.CreateTaskRuntimeService(workerLease.TaskRuntimeId, workerLease.GrpcEndpoint);
        var response = await workerClient.DispatchJobAsync(request, cancellationToken);

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

        await workerLifecycleManager.RecordDispatchActivityAsync(workerLease.TaskRuntimeId, cancellationToken);

        var started = await store.MarkRunStartedAsync(
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

        var task = await store.GetTaskAsync(taskId, cancellationToken);
        if (task is null)
        {
            return false;
        }

        var repository = await store.GetRepositoryAsync(task.RepositoryId, cancellationToken);
        if (repository is null)
        {
            return false;
        }

        var taskRuns = await store.ListRunsByTaskAsync(taskId, TaskRunWindowLimit, cancellationToken) ?? [];
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
            var run = await store.GetRunAsync(runId, cancellationToken);
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
            await workerClient.StopJobAsync(new StopJobRequest { RunId = runId }, cancellationToken);
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

        var taskRuns = await store.ListRunsByTaskAsync(taskId, TaskRunWindowLimit, cancellationToken) ?? [];
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
            PressureSampleWindowSeconds: options.TaskRuntimes.PressureSampleWindowSeconds);
    }

    private async Task<string> BuildLayeredPromptAsync(
        RepositoryDocument repository,
        TaskDocument task,
        string defaultBranch,
        CancellationToken cancellationToken)
    {
        var systemSettings = await store.GetSettingsAsync(cancellationToken);
        var globalPrefix = string.IsNullOrWhiteSpace(systemSettings.Orchestrator.TaskPromptPrefix)
            ? BuildRequiredTaskPrefix(defaultBranch)
            : systemSettings.Orchestrator.TaskPromptPrefix;
        var globalSuffix = string.IsNullOrWhiteSpace(systemSettings.Orchestrator.TaskPromptSuffix)
            ? BuildRequiredTaskSuffix(defaultBranch)
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
            SetOrReplace(envVars, "CODEX_TRANSPORT", "stdio");
            SetIfMissing(envVars, "CODEX_MODE", "stdio");

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
        var existingKey = FindKeyIgnoreCase(envVars, key);
        if (existingKey is not null)
        {
            return;
        }

        envVars[key] = value;
    }

    private static void SetOrReplace(IDictionary<string, string> envVars, string key, string value)
    {
        var existingKey = FindKeyIgnoreCase(envVars, key);
        if (existingKey is null)
        {
            envVars[key] = value;
            return;
        }

        envVars[existingKey] = value;
    }

    private static string? FindKeyIgnoreCase(IDictionary<string, string> envVars, string key)
    {
        foreach (var existingKey in envVars.Keys)
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
}
