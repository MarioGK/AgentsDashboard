using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.TaskRuntime;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class DockerTaskRuntimeLifecycleManager(
    IOptions<OrchestratorOptions> options,
    IOrchestratorRuntimeSettingsProvider runtimeSettingsProvider,
    ILeaseCoordinator leaseCoordinator,
    IOrchestratorStore store,
    IMagicOnionClientFactory clientFactory,
    ILogger<DockerTaskRuntimeLifecycleManager> logger) : ITaskRuntimeLifecycleManager
{
    private const string ManagedByLabel = "orchestrator.managed-by";
    private const string ManagedByValue = "control-plane";
    private const string WorkerRoleLabel = "orchestrator.role";
    private const string WorkerRoleValue = "task-runtime-gateway";
    private const string TaskRuntimeIdLabel = "orchestrator.worker-id";
    private const string TaskIdLabel = "orchestrator.task-id";
    private const string RepositoryIdLabel = "orchestrator.repo-id";
    private const string MaxSlotsLabel = "orchestrator.max-slots";
    private const string SharedWorkspacesVolumeName = "agentsdashboard-workspaces";
    private const int WorkerGrpcPort = 5201;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ScaleOutAttemptWindow = TimeSpan.FromMinutes(10);
    private static readonly string[] TaskRuntimeGatewayBuildContextRequirements =
    [
        "global.json",
        "Directory.Build.props",
        "Directory.Packages.props",
        Path.Combine("src", "AgentsDashboard.slnx"),
        Path.Combine("src", "AgentsDashboard.TaskRuntimeGateway"),
        Path.Combine("src", "AgentsDashboard.Contracts")
    ];
    private static readonly Regex PullProgressRegex = new(
        @"(?<current>\d+(?:\.\d+)?)\s*(?<currentUnit>[KMGTP]?B)\s*/\s*(?<total>\d+(?:\.\d+)?)\s*(?<totalUnit>[KMGTP]?B)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex BuildStepRegex = new(@"\[(?<current>\d+)\/(?<total>\d+)\]", RegexOptions.Compiled);
    private static readonly Regex BuildPercentRegex = new(@"(?<percent>\d{1,3})%", RegexOptions.Compiled);

    private readonly OrchestratorOptions _options = options.Value;
    private readonly DockerClient _dockerClient = new DockerClientConfiguration().CreateClient();
    private readonly ConcurrentDictionary<string, TaskRuntimeStateEntry> _workers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _taskRepositoryCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _imageAcquireLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _imageFailureCooldownUntilUtc = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly SemaphoreSlim _spawnLock = new(1, 1);

    private readonly object _budgetLock = new();
    private readonly object _concurrencyLock = new();

    private DateTime _lastRefreshUtc = DateTime.MinValue;
    private DateTime _startBudgetWindowUtc = DateTime.UtcNow;
    private int _startAttemptsInWindow;
    private int _failedStartsInWindow;
    private DateTime? _scaleOutCooldownUntilUtc;
    private bool _scaleOutPaused;
    private int _activePulls;
    private int _activeBuilds;

    public async Task EnsureTaskRuntimeImageAvailableAsync(
        CancellationToken cancellationToken,
        IProgress<BackgroundWorkSnapshot>? progress = null)
    {
        ReportTaskRuntimeImageProgress(progress, "Resolving task runtime image policy.");
        var runtime = await runtimeSettingsProvider.GetAsync(cancellationToken);
        var baseImage = ResolveEffectiveImageReference(runtime.ContainerImage, runtime.TaskRuntimeImageRegistry);
        if (string.IsNullOrWhiteSpace(baseImage))
        {
            ReportTaskRuntimeImageProgress(
                progress,
                "Task runtime image configuration is empty; skipping startup image resolution.",
                percentComplete: 100,
                state: BackgroundWorkState.Succeeded);
            return;
        }

        ReportTaskRuntimeImageProgress(progress, $"Resolving base task runtime image {baseImage}.", percentComplete: 5);
        var baseResolution = await EnsureTaskRuntimeImageResolvedWithSourceAsync(
            baseImage,
            runtime,
            cancellationToken: cancellationToken,
            progress: progress,
            forceRefresh: true);
        if (!baseResolution.Available)
        {
            logger.ZLogWarning(
                "Task runtime image {Image} is unavailable after image policy execution. Dispatch will fail until the image is available.",
                baseImage);
            ReportTaskRuntimeImageProgress(
                progress,
                $"Task runtime image {baseImage} is unavailable after policy execution.",
                state: BackgroundWorkState.Failed,
                errorCode: "image_unavailable",
                errorMessage: $"Task runtime image {baseImage} is unavailable.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(runtime.TaskRuntimeCanaryImage) && runtime.CanaryPercent > 0)
        {
            var canaryImage = ResolveEffectiveImageReference(runtime.TaskRuntimeCanaryImage, runtime.TaskRuntimeImageRegistry);
            ReportTaskRuntimeImageProgress(progress, $"Resolving canary task runtime image {canaryImage}.", percentComplete: 65);
            var canaryResolution = await EnsureTaskRuntimeImageResolvedWithSourceAsync(
                canaryImage,
                runtime,
                cancellationToken: cancellationToken,
                progress: progress,
                forceRefresh: true);
            if (!canaryResolution.Available)
            {
                logger.ZLogWarning(
                    "Worker canary image {Image} is unavailable; continuing with base image {BaseImage}.",
                    canaryImage,
                    baseImage);
                ReportTaskRuntimeImageProgress(
                    progress,
                    $"Canary image {canaryImage} is unavailable; continuing with base image {baseImage}.",
                    percentComplete: 85);
            }
        }

        logger.ZLogInformation("Task runtime image {Image} is available", baseImage);
        ReportTaskRuntimeImageProgress(
            progress,
            $"Task runtime image {baseImage} is available.",
            percentComplete: 100,
            state: BackgroundWorkState.Succeeded);
    }

    private sealed record ImageResolutionResult(bool Available, string Source);

    private static readonly ImageResolutionResult UnavailableImageResolution = new(false, string.Empty);

    private sealed record SpawnImageSelection(string Image, bool IsCanary, string CanaryReason);

    public async Task EnsureMinimumTaskRuntimesAsync(CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
    }

    public async Task<TaskRuntimeLease?> AcquireTaskRuntimeForDispatchAsync(
        string repositoryId,
        string taskId,
        int requestedParallelSlots,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return null;
        }

        var runtime = await runtimeSettingsProvider.GetAsync(cancellationToken);
        var workers = await ListTaskRuntimesAsync(cancellationToken);
        var effectiveParallelSlots = Math.Clamp(requestedParallelSlots <= 0 ? 1 : requestedParallelSlots, 1, 64);
        var existing = workers
            .Where(x => x.IsRunning &&
                        !x.IsDraining &&
                        string.Equals(x.TaskId, taskId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.ActiveSlots)
            .ThenBy(x => x.LastActivityUtc)
            .FirstOrDefault();

        if (existing is not null)
        {
            await RecordDispatchActivityAsync(existing.TaskRuntimeId, cancellationToken);
            return new TaskRuntimeLease(
                existing.TaskRuntimeId,
                existing.ContainerId,
                existing.GrpcEndpoint,
                existing.ProxyEndpoint);
        }

        var runningCount = workers.Count(x => x.IsRunning);
        if (runningCount >= runtime.MaxWorkers)
        {
            return null;
        }

        var candidate = await SpawnWorkerAsync(runtime, repositoryId, taskId, effectiveParallelSlots, cancellationToken);
        if (candidate is null)
        {
            return null;
        }

        await RecordDispatchActivityAsync(candidate.TaskRuntimeId, cancellationToken);
        return new TaskRuntimeLease(
            candidate.TaskRuntimeId,
            candidate.ContainerId,
            candidate.GrpcEndpoint,
            candidate.ProxyEndpoint);
    }

    public async Task<TaskRuntimeInstance?> GetTaskRuntimeAsync(string runtimeId, CancellationToken cancellationToken)
    {
        var runtimes = await ListTaskRuntimesAsync(cancellationToken);
        return runtimes.FirstOrDefault(x => string.Equals(x.TaskRuntimeId, runtimeId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<TaskRuntimeInstance>> ListTaskRuntimesAsync(CancellationToken cancellationToken)
    {
        var runtime = await runtimeSettingsProvider.GetAsync(cancellationToken);
        await RefreshWorkersAsync(runtime, cancellationToken);
        return _workers.Values
            .Select(x => x.ToRuntime())
            .OrderBy(x => x.TaskRuntimeId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task ReportTaskRuntimeHeartbeatAsync(string runtimeId, int activeSlots, int maxSlots, CancellationToken cancellationToken)
    {
        if (_workers.TryGetValue(runtimeId, out var state))
        {
            var now = DateTime.UtcNow;
            var previousActiveSlots = state.ActiveSlots;
            state.ActiveSlots = Math.Max(0, activeSlots);
            state.MaxSlots = maxSlots > 0 ? maxSlots : state.MaxSlots;
            state.LifecycleState = state.ActiveSlots > 0 ? TaskRuntimeLifecycleState.Busy : state.IsDraining ? TaskRuntimeLifecycleState.Draining : TaskRuntimeLifecycleState.Ready;
            if (state.ActiveSlots > 0 || (previousActiveSlots > 0 && state.ActiveSlots == 0))
            {
                state.LastActivityUtc = now;
            }

            await PersistTaskRuntimeStateAsync(
                state,
                cancellationToken,
                updateLastActivityUtc: state.ActiveSlots > 0 || previousActiveSlots > 0,
                clearInactiveAfterUtc: true);
        }
    }

    public async Task RecordDispatchActivityAsync(string runtimeId, CancellationToken cancellationToken)
    {
        if (_workers.TryGetValue(runtimeId, out var state))
        {
            state.LastActivityUtc = DateTime.UtcNow;
            state.DispatchCount++;
            state.LifecycleState = TaskRuntimeLifecycleState.Busy;
            state.ActiveSlots = Math.Clamp(state.ActiveSlots + 1, 1, Math.Max(1, state.MaxSlots));
            await PersistTaskRuntimeStateAsync(state, cancellationToken, updateLastActivityUtc: true, clearInactiveAfterUtc: true);
        }
    }

    public async Task ScaleDownIdleTaskRuntimesAsync(CancellationToken cancellationToken)
    {
        var runtime = await runtimeSettingsProvider.GetAsync(cancellationToken);
        var workers = await ListTaskRuntimesAsync(cancellationToken);
        var runningWorkers = workers.Where(x => x.IsRunning).OrderBy(x => x.LastActivityUtc).ToList();
        var idleTimeoutMinutes = runtime.TaskRuntimeInactiveTimeoutMinutes > 0
            ? runtime.TaskRuntimeInactiveTimeoutMinutes
            : _options.TaskRuntimes.IdleTimeoutMinutes;
        var idleThreshold = TimeSpan.FromMinutes(idleTimeoutMinutes);
        var now = DateTime.UtcNow;

        foreach (var worker in runningWorkers)
        {
            var isTimedOutIdle = worker.ActiveSlots == 0 && now - worker.LastActivityUtc >= idleThreshold;
            var isDrainReady = worker.IsDraining && worker.ActiveSlots == 0;
            var isDrainTimedOut = worker.IsDraining && now - worker.LastActivityUtc >= TimeSpan.FromSeconds(runtime.DrainTimeoutSeconds);

            if (!isTimedOutIdle && !isDrainReady && !isDrainTimedOut)
            {
                continue;
            }

            await StopWorkerAsync(
                worker.TaskRuntimeId,
                worker.ContainerId,
                runtime,
                force: isDrainTimedOut,
                cancellationToken,
                finalState: TaskRuntimeState.Inactive);
        }
    }

    public async Task SetTaskRuntimeDrainingAsync(string runtimeId, bool draining, CancellationToken cancellationToken)
    {
        if (_workers.TryGetValue(runtimeId, out var state))
        {
            state.IsDraining = draining;
            state.DrainingSinceUtc = draining ? DateTime.UtcNow : DateTime.MinValue;
            state.LifecycleState = draining ? TaskRuntimeLifecycleState.Draining : state.ActiveSlots > 0 ? TaskRuntimeLifecycleState.Busy : TaskRuntimeLifecycleState.Ready;
            await PersistTaskRuntimeStateAsync(
                state,
                cancellationToken,
                updateLastActivityUtc: false,
                clearInactiveAfterUtc: !draining);
        }
    }

    public async Task RecycleTaskRuntimeAsync(string runtimeId, CancellationToken cancellationToken)
    {
        var runtime = await runtimeSettingsProvider.GetAsync(cancellationToken);
        var worker = await GetTaskRuntimeAsync(runtimeId, cancellationToken);
        if (worker is null)
        {
            return;
        }

        await SetTaskRuntimeDrainingAsync(runtimeId, true, cancellationToken);
        await StopWorkerAsync(runtimeId, worker.ContainerId, runtime, force: true, cancellationToken, finalState: TaskRuntimeState.Inactive);
    }

    public async Task RecycleTaskRuntimePoolAsync(CancellationToken cancellationToken)
    {
        var workers = await ListTaskRuntimesAsync(cancellationToken);
        foreach (var worker in workers.Where(x => x.IsRunning))
        {
            await SetTaskRuntimeDrainingAsync(worker.TaskRuntimeId, true, cancellationToken);
        }
    }

    public async Task RunReconciliationAsync(CancellationToken cancellationToken)
    {
        await using var reconcileLease = await leaseCoordinator.TryAcquireAsync(
            "worker-reconciler",
            TimeSpan.FromSeconds(30),
            cancellationToken);
        if (reconcileLease is null)
        {
            return;
        }

        var workers = await ListTaskRuntimesAsync(cancellationToken);
        foreach (var worker in workers.Where(x => x.IsRunning))
        {
            await store.UpsertTaskRuntimeRegistrationHeartbeatAsync(
                worker.TaskRuntimeId,
                worker.GrpcEndpoint,
                worker.ActiveSlots,
                worker.MaxSlots,
                cancellationToken);

            await store.UpsertTaskRuntimeStateAsync(
                new TaskRuntimeStateUpdate
                {
                    RuntimeId = worker.TaskRuntimeId,
                    RepositoryId = await ResolveRepositoryIdAsync(worker.TaskId, cancellationToken),
                    TaskId = worker.TaskId,
                    State = MapLifecycleState(worker.LifecycleState),
                    ActiveRuns = worker.ActiveSlots,
                    MaxParallelRuns = worker.MaxSlots,
                    Endpoint = worker.GrpcEndpoint,
                    ContainerId = worker.ContainerId,
                    ObservedAtUtc = DateTime.UtcNow,
                    UpdateLastActivityUtc = worker.ActiveSlots > 0,
                    ClearInactiveAfterUtc = worker.IsRunning,
                },
                cancellationToken);
        }

        await store.MarkStaleTaskRuntimeRegistrationsOfflineAsync(TimeSpan.FromMinutes(2), cancellationToken);
    }

    public Task SetScaleOutPausedAsync(bool paused, CancellationToken cancellationToken)
    {
        lock (_budgetLock)
        {
            _scaleOutPaused = paused;
        }

        return Task.CompletedTask;
    }

    public Task ClearScaleOutCooldownAsync(CancellationToken cancellationToken)
    {
        lock (_budgetLock)
        {
            _scaleOutCooldownUntilUtc = null;
            _startBudgetWindowUtc = DateTime.UtcNow;
            _startAttemptsInWindow = 0;
            _failedStartsInWindow = 0;
        }

        return Task.CompletedTask;
    }

    public async Task<OrchestratorHealthSnapshot> GetHealthSnapshotAsync(CancellationToken cancellationToken)
    {
        var workers = await ListTaskRuntimesAsync(cancellationToken);
        lock (_budgetLock)
        {
            return new OrchestratorHealthSnapshot(
                RunningTaskRuntimes: workers.Count(x => x.IsRunning),
                ReadyTaskRuntimes: workers.Count(x => x.IsRunning && x.LifecycleState == TaskRuntimeLifecycleState.Ready),
                BusyTaskRuntimes: workers.Count(x => x.IsRunning && x.LifecycleState == TaskRuntimeLifecycleState.Busy),
                DrainingTaskRuntimes: workers.Count(x => x.IsRunning && x.IsDraining),
                ScaleOutPaused: _scaleOutPaused,
                ScaleOutCooldownUntilUtc: _scaleOutCooldownUntilUtc,
                StartAttemptsInWindow: _startAttemptsInWindow,
                FailedStartsInWindow: _failedStartsInWindow);
        }
    }

    private async Task RefreshWorkersAsync(OrchestratorRuntimeSettings runtime, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (now - _lastRefreshUtc < RefreshInterval)
        {
            return;
        }

        await _refreshLock.WaitAsync(cancellationToken);
        try
        {
            now = DateTime.UtcNow;
            if (now - _lastRefreshUtc < RefreshInterval)
            {
                return;
            }

            var containers = await _dockerClient.Containers.ListContainersAsync(
                new ContainersListParameters { All = true },
                cancellationToken);

            var managed = containers
                .Where(x => IsManagedWorkerContainer(x, runtime))
                .ToList();

            var currentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var container in managed)
            {
                var containerName = container.Names.FirstOrDefault()?.Trim('/') ?? container.ID[..Math.Min(12, container.ID.Length)];
                var workerId = ResolveTaskRuntimeId(container, containerName);
                var taskId = ResolveTaskId(container);
                var maxSlots = ResolveMaxSlots(container, defaultValue: 1);
                var isRunning = string.Equals(container.State, "running", StringComparison.OrdinalIgnoreCase);
                var (grpcEndpoint, proxyEndpoint) = ResolveWorkerEndpoints(container, containerName, runtime);

                var state = _workers.AddOrUpdate(
                    workerId,
                    _ => TaskRuntimeStateEntry.Create(
                        workerId,
                        taskId,
                        container.ID,
                        containerName,
                        grpcEndpoint,
                        proxyEndpoint,
                        isRunning,
                        maxSlots),
                    (_, existing) =>
                    {
                        existing.TaskId = taskId;
                        existing.ContainerId = container.ID;
                        existing.ContainerName = containerName;
                        existing.GrpcEndpoint = grpcEndpoint;
                        existing.ProxyEndpoint = proxyEndpoint;
                        existing.IsRunning = isRunning;
                        existing.ImageRef = container.Image ?? existing.ImageRef;
                        existing.ImageDigest = container.ImageID ?? existing.ImageDigest;
                        if (maxSlots > 0)
                        {
                            existing.MaxSlots = maxSlots;
                        }
                        else if (existing.MaxSlots <= 0)
                        {
                            existing.MaxSlots = 1;
                        }

                        if (!existing.IsDraining)
                        {
                            existing.LifecycleState = isRunning
                                ? (existing.ActiveSlots > 0 ? TaskRuntimeLifecycleState.Busy : TaskRuntimeLifecycleState.Ready)
                                : TaskRuntimeLifecycleState.Stopped;
                        }

                        return existing;
                    });

                if (isRunning && ShouldRefreshPressure(state.LastPressureSampleUtc, now, runtime))
                {
                    try
                    {
                        var metrics = await TryGetPressureMetricsAsync(container.ID, cancellationToken);
                        state.CpuPercent = metrics.CpuPercent;
                        state.MemoryPercent = metrics.MemoryPercent;
                        state.LastPressureSampleUtc = now;
                    }
                    catch (Exception ex)
                    {
                        logger.ZLogDebug(ex, "Failed to refresh pressure metrics for worker {TaskRuntimeId}", workerId);
                    }
                }

                currentIds.Add(workerId);
            }

            foreach (var staleTaskRuntimeId in _workers.Keys.Where(id => !currentIds.Contains(id)).ToList())
            {
                if (_workers.TryRemove(staleTaskRuntimeId, out var staleState))
                {
                    staleState.IsRunning = false;
                    staleState.ActiveSlots = 0;
                    staleState.LifecycleState = TaskRuntimeLifecycleState.Stopped;
                    await PersistTaskRuntimeStateAsync(
                        staleState,
                        cancellationToken,
                        explicitState: TaskRuntimeState.Inactive,
                        updateLastActivityUtc: false);
                }

                clientFactory.RemoveTaskRuntime(staleTaskRuntimeId);
            }

            _lastRefreshUtc = DateTime.UtcNow;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private bool ShouldRefreshPressure(DateTime lastSampleUtc, DateTime now, OrchestratorRuntimeSettings runtime)
    {
        if (lastSampleUtc == DateTime.MinValue)
        {
            return true;
        }

        return now - lastSampleUtc >= TimeSpan.FromSeconds(runtime.PressureSampleWindowSeconds);
    }

    private SpawnImageSelection SelectSpawnImage(OrchestratorRuntimeSettings runtime, IReadOnlyList<TaskRuntimeInstance> workers)
    {
        var baseImage = ResolveEffectiveImageReference(runtime.ContainerImage, runtime.TaskRuntimeImageRegistry);
        var canaryImage = ResolveEffectiveImageReference(runtime.TaskRuntimeCanaryImage, runtime.TaskRuntimeImageRegistry);

        if (string.IsNullOrWhiteSpace(canaryImage) || runtime.CanaryPercent <= 0)
        {
            return new SpawnImageSelection(baseImage, false, string.Empty);
        }

        var runningWorkers = workers.Where(x => x.IsRunning).ToList();
        if (runningWorkers.Count == 0)
        {
            return new SpawnImageSelection(canaryImage, true, "bootstrapping");
        }

        var currentCanary = runningWorkers.Count(x =>
            string.Equals(x.ImageRef, canaryImage, StringComparison.OrdinalIgnoreCase));

        var projectedTotal = runningWorkers.Count + 1;
        var targetCanary = (int)Math.Ceiling(projectedTotal * (runtime.CanaryPercent / 100.0));
        if (currentCanary < targetCanary)
        {
            return new SpawnImageSelection(canaryImage, true, $"target {targetCanary}/{projectedTotal}");
        }

        return new SpawnImageSelection(baseImage, false, string.Empty);
    }

    private async Task<TaskRuntimeInstance?> SpawnWorkerAsync(
        OrchestratorRuntimeSettings runtime,
        string repositoryId,
        string taskId,
        int maxSlots,
        CancellationToken cancellationToken)
    {
        await _spawnLock.WaitAsync(cancellationToken);
        try
        {
            await using var scaleLease = await leaseCoordinator.TryAcquireAsync(
                "worker-scale",
                TimeSpan.FromSeconds(Math.Max(30, runtime.ContainerStartTimeoutSeconds + 30)),
                cancellationToken);
            if (scaleLease is null)
            {
                logger.ZLogDebug("Skipped worker spawn because scale lease is currently held by another orchestrator instance");
                return null;
            }

            var workers = await ListTaskRuntimesAsync(cancellationToken);
            var runningCount = workers.Count(x => x.IsRunning);
            if (runningCount >= runtime.MaxWorkers)
            {
                return null;
            }

            var existingTaskRuntime = workers.FirstOrDefault(x =>
                x.IsRunning &&
                string.Equals(x.TaskId, taskId, StringComparison.OrdinalIgnoreCase));
            if (existingTaskRuntime is not null)
            {
                return existingTaskRuntime;
            }

            var imageSelection = SelectSpawnImage(runtime, workers);
            var selectedImage = imageSelection.Image;
            var imageResolution = await EnsureTaskRuntimeImageResolvedWithSourceAsync(selectedImage, runtime, cancellationToken);
            if (!imageResolution.Available && imageSelection.IsCanary)
            {
                var baseImage = ResolveEffectiveImageReference(runtime.ContainerImage, runtime.TaskRuntimeImageRegistry);
                logger.ZLogWarning(
                    "Canary task runtime image {CanaryImage} could not be resolved ({Reason}); falling back to base image {BaseImage}.",
                    selectedImage,
                    imageSelection.CanaryReason,
                    baseImage);
                selectedImage = baseImage;
                imageResolution = await EnsureTaskRuntimeImageResolvedWithSourceAsync(selectedImage, runtime, cancellationToken);
            }

            if (!imageResolution.Available)
            {
                throw new InvalidOperationException($"Task runtime image '{selectedImage}' is unavailable.");
            }

            if (!CanScaleOut(runtime, out var blockedReason))
            {
                logger.ZLogWarning("Scale-out blocked while attempting to spawn worker: {Reason}", blockedReason);
                return null;
            }

            RegisterScaleOutAttempt(runtime);

            var workerId = BuildTaskRuntimeId(runtime.ContainerNamePrefix, taskId);
            var containerName = workerId;

            var codexApiKey = HostCredentialDiscovery.TryGetCodexApiKey();
            var openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            var configuredCodexApiKey = Environment.GetEnvironmentVariable("CODEX_API_KEY");

            if (string.IsNullOrWhiteSpace(codexApiKey))
                codexApiKey = configuredCodexApiKey;
            if (string.IsNullOrWhiteSpace(openAiApiKey))
                openAiApiKey = codexApiKey;
            if (string.IsNullOrWhiteSpace(codexApiKey))
                codexApiKey = openAiApiKey;

            var connectivityMode = ResolveConnectivityMode(runtime);
            var useHostPort = connectivityMode == TaskRuntimeConnectivityMode.HostPortOnly ||
                              (connectivityMode == TaskRuntimeConnectivityMode.AutoDetect && !IsRunningInsideContainer());
            await EnsureDockerNetworkAsync(runtime.DockerNetwork, cancellationToken);

            var createParameters = new CreateContainerParameters
            {
                Image = selectedImage,
                Name = containerName,
                Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [ManagedByLabel] = ManagedByValue,
                    [WorkerRoleLabel] = WorkerRoleValue,
                    [TaskRuntimeIdLabel] = workerId,
                    [TaskIdLabel] = taskId,
                    [RepositoryIdLabel] = repositoryId,
                    [MaxSlotsLabel] = maxSlots.ToString(CultureInfo.InvariantCulture),
                },
                Env =
                [
                    "TaskRuntime__UseDocker=true",
                    $"TaskRuntime__TaskRuntimeId={workerId}",
                    $"TaskRuntime__MaxSlots={Math.Clamp(maxSlots, 1, 64)}",
                    $"TaskRuntime__DefaultImage={Environment.GetEnvironmentVariable("TASK_RUNTIME_DEFAULT_IMAGE") ?? "ghcr.io/mariogk/ai-harness:latest"}",
                    $"CODEX_API_KEY={codexApiKey ?? string.Empty}",
                    $"OPENAI_API_KEY={openAiApiKey ?? string.Empty}",
                    $"OPENCODE_API_KEY={Environment.GetEnvironmentVariable("OPENCODE_API_KEY") ?? string.Empty}",
                    $"ANTHROPIC_API_KEY={Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? string.Empty}",
                    $"Z_AI_API_KEY={Environment.GetEnvironmentVariable("Z_AI_API_KEY") ?? string.Empty}",
                ],
                ExposedPorts = useHostPort
                    ? new Dictionary<string, EmptyStruct> { ["5201/tcp"] = default }
                    : null,
                HostConfig = BuildHostConfig(runtime, useHostPort, workerId, taskId),
                NetworkingConfig = new NetworkingConfig
                {
                    EndpointsConfig = new Dictionary<string, EndpointSettings>
                    {
                        [runtime.DockerNetwork] = new()
                    }
                }
            };

            CreateContainerResponse created;
            try
            {
                created = await _dockerClient.Containers.CreateContainerAsync(createParameters, cancellationToken);
            }
            catch (DockerApiException ex) when (ex.Message.Contains("already in use", StringComparison.OrdinalIgnoreCase))
            {
                logger.ZLogDebug(ex, "Task runtime container {ContainerName} already exists, reusing existing runtime", containerName);
                await RefreshWorkersAsync(runtime, cancellationToken);
                return await WaitForWorkerReadyAsync(workerId, runtime, cancellationToken);
            }
            catch (DockerApiException ex) when (IsMissingImageException(ex, createParameters.Image))
            {
                logger.ZLogWarning(ex, "Task runtime image {Image} was not found locally; attempting resolution and retry.", createParameters.Image);

                imageResolution = await EnsureTaskRuntimeImageResolvedWithSourceAsync(createParameters.Image, runtime, cancellationToken);
                if (!imageResolution.Available)
                {
                    throw new InvalidOperationException(
                        $"Task runtime image '{createParameters.Image}' is unavailable. Build from source or pull it, then retry.",
                        ex);
                }

                created = await _dockerClient.Containers.CreateContainerAsync(createParameters, cancellationToken);
            }
            catch (DockerApiException ex) when (
                createParameters.NetworkingConfig is not null &&
                ex.Message.Contains("network", StringComparison.OrdinalIgnoreCase))
            {
                logger.ZLogWarning(ex, "Worker network {Network} is unavailable; retrying worker create without explicit network", runtime.DockerNetwork);
                createParameters.NetworkingConfig = null;
                created = await _dockerClient.Containers.CreateContainerAsync(createParameters, cancellationToken);
            }

            await _dockerClient.Containers.StartContainerAsync(created.ID, new ContainerStartParameters(), cancellationToken);

            await RefreshWorkersAsync(runtime, cancellationToken);

            var worker = await WaitForWorkerReadyAsync(workerId, runtime, cancellationToken);
            if (worker is null)
            {
                RegisterScaleOutFailure(runtime);
                logger.ZLogWarning("Worker {TaskRuntimeId} did not become ready in time", workerId);
                if (_workers.TryGetValue(workerId, out var workerState))
                {
                    workerState.LifecycleState = TaskRuntimeLifecycleState.FailedStart;
                    await PersistTaskRuntimeStateAsync(
                        workerState,
                        cancellationToken,
                        explicitState: TaskRuntimeState.Failed,
                        updateLastActivityUtc: false,
                        lastError: "Task runtime did not become ready before startup timeout.");
                }

                return null;
            }

            RegisterScaleOutSuccess();
            if (_workers.TryGetValue(worker.TaskRuntimeId, out var readyWorkerState))
            {
                readyWorkerState.TaskId = taskId;
                readyWorkerState.ImageRef = createParameters.Image ?? string.Empty;
                readyWorkerState.ImageSource = imageResolution.Source;
                readyWorkerState.ImageDigest = worker.ImageDigest;
                readyWorkerState.MaxSlots = Math.Clamp(maxSlots, 1, 64);
                await PersistTaskRuntimeStateAsync(
                    readyWorkerState,
                    cancellationToken,
                    explicitState: TaskRuntimeState.Ready,
                    updateLastActivityUtc: false,
                    clearInactiveAfterUtc: true);
            }

            logger.ZLogInformation("Spawned worker {TaskRuntimeId} ({ContainerId})", worker.TaskRuntimeId, worker.ContainerId[..Math.Min(12, worker.ContainerId.Length)]);
            return worker;
        }
        catch (Exception ex)
        {
            RegisterScaleOutFailure(runtime);
            if (_workers.Values.FirstOrDefault(x => x.LifecycleState == TaskRuntimeLifecycleState.Starting) is { } startingState)
            {
                startingState.LifecycleState = TaskRuntimeLifecycleState.FailedStart;
                await PersistTaskRuntimeStateAsync(
                    startingState,
                    cancellationToken,
                    explicitState: TaskRuntimeState.Failed,
                    updateLastActivityUtc: false,
                    lastError: ex.Message);
            }

            logger.ZLogWarning(ex, "Failed to spawn worker");
            return null;
        }
        finally
        {
            _spawnLock.Release();
        }
    }

    private async Task EnsureDockerNetworkAsync(string networkName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(networkName))
        {
            return;
        }

        var networks = await _dockerClient.Networks.ListNetworksAsync(new NetworksListParameters(), cancellationToken);
        if (networks.Any(x => string.Equals(x.Name, networkName, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        try
        {
            await _dockerClient.Networks.CreateNetworkAsync(
                new NetworksCreateParameters
                {
                    Name = networkName,
                    Driver = "bridge",
                    Attachable = true,
                },
                cancellationToken);

            logger.ZLogInformation("Created missing Docker network {Network}", networkName);
        }
        catch (DockerApiException ex)
        {
            if (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            logger.ZLogWarning(ex, "Unable to create Docker network {Network}; worker creation may fall back.", networkName);
        }
    }

    private async Task<bool> StopWorkerAsync(
        string workerId,
        string containerId,
        OrchestratorRuntimeSettings runtime,
        bool force,
        CancellationToken cancellationToken,
        TaskRuntimeState finalState)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(runtime.ContainerStopTimeoutSeconds));

        try
        {
            if (_workers.TryGetValue(workerId, out var state))
            {
                state.LifecycleState = TaskRuntimeLifecycleState.Stopping;
                await PersistTaskRuntimeStateAsync(
                    state,
                    cancellationToken,
                    explicitState: TaskRuntimeState.Stopping,
                    updateLastActivityUtc: false,
                    clearInactiveAfterUtc: true);
            }

            if (!force)
            {
                try
                {
                    await _dockerClient.Containers.StopContainerAsync(
                        containerId,
                        new ContainerStopParameters(),
                        timeoutCts.Token);
                }
                catch (DockerContainerNotFoundException)
                {
                }
            }

            await _dockerClient.Containers.RemoveContainerAsync(
                containerId,
                new ContainerRemoveParameters { Force = true },
                timeoutCts.Token);

            if (_workers.TryGetValue(workerId, out var updated))
            {
                updated.IsRunning = false;
                updated.LifecycleState = TaskRuntimeLifecycleState.Stopped;
                updated.ActiveSlots = 0;
                await PersistTaskRuntimeStateAsync(
                    updated,
                    cancellationToken,
                    explicitState: finalState,
                    updateLastActivityUtc: false,
                    clearInactiveAfterUtc: finalState != TaskRuntimeState.Inactive);
            }

            clientFactory.RemoveTaskRuntime(workerId);
            logger.ZLogInformation("Stopped and removed worker {TaskRuntimeId} ({ContainerId})", workerId, containerId[..Math.Min(12, containerId.Length)]);
            return true;
        }
        catch (DockerContainerNotFoundException)
        {
            if (_workers.TryRemove(workerId, out var removed))
            {
                removed.IsRunning = false;
                removed.LifecycleState = TaskRuntimeLifecycleState.Stopped;
                removed.ActiveSlots = 0;
                await PersistTaskRuntimeStateAsync(
                    removed,
                    cancellationToken,
                    explicitState: finalState,
                    updateLastActivityUtc: false,
                    clearInactiveAfterUtc: finalState != TaskRuntimeState.Inactive);
            }

            clientFactory.RemoveTaskRuntime(workerId);
            return true;
        }
        catch (Exception ex)
        {
            if (_workers.TryGetValue(workerId, out var state))
            {
                state.LifecycleState = TaskRuntimeLifecycleState.FailedStart;
                await PersistTaskRuntimeStateAsync(
                    state,
                    cancellationToken,
                    explicitState: TaskRuntimeState.Failed,
                    updateLastActivityUtc: false,
                    lastError: ex.Message);
            }

            logger.ZLogWarning(ex, "Failed to stop worker {TaskRuntimeId}", workerId);
            return false;
        }
    }

    private static IEnumerable<string> GetWorkerStorageVolumeNames(string workerId)
    {
        var workerToken = NormalizeWorkerToken(workerId);
        yield return $"worker-artifacts-{workerToken}";
    }

    private static string NormalizeWorkerToken(string workerId)
    {
        if (string.IsNullOrWhiteSpace(workerId))
        {
            return "unknown";
        }

        return new string(workerId
            .ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-')
            .ToArray());
    }

    private async Task RemoveWorkerStorageAsync(string workerId, CancellationToken cancellationToken)
    {
        foreach (var volumeName in GetWorkerStorageVolumeNames(workerId))
        {
            try
            {
                var info = new ProcessStartInfo
                {
                    FileName = "docker",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                info.ArgumentList.Add("volume");
                info.ArgumentList.Add("rm");
                info.ArgumentList.Add("-f");
                info.ArgumentList.Add(volumeName);

                using var process = new Process { StartInfo = info };
                if (!process.Start())
                {
                    continue;
                }

                await process.WaitForExitAsync(cancellationToken);
                if (process.ExitCode == 0)
                {
                    logger.ZLogInformation("Removed worker storage volume {VolumeName} for {TaskRuntimeId}", volumeName, workerId);
                }
            }
            catch (Exception ex)
            {
                logger.ZLogDebug(ex, "Failed to remove worker volume {VolumeName} for {TaskRuntimeId}", volumeName, workerId);
            }
        }
    }

    private bool CanScaleOut(OrchestratorRuntimeSettings runtime, out string reason)
    {
        lock (_budgetLock)
        {
            ResetBudgetWindowIfExpired();

            if (_scaleOutPaused)
            {
                reason = "Scale-out is paused";
                return false;
            }

            if (_scaleOutCooldownUntilUtc.HasValue && _scaleOutCooldownUntilUtc.Value > DateTime.UtcNow)
            {
                reason = $"Scale-out cooldown active until {_scaleOutCooldownUntilUtc.Value:O}";
                return false;
            }

            if (_startAttemptsInWindow >= runtime.MaxWorkerStartAttemptsPer10Min)
            {
                _scaleOutCooldownUntilUtc = DateTime.UtcNow.AddMinutes(runtime.CooldownMinutes);
                reason = $"Start attempt budget exceeded ({_startAttemptsInWindow}/{runtime.MaxWorkerStartAttemptsPer10Min})";
                return false;
            }

            if (_failedStartsInWindow >= runtime.MaxFailedStartsPer10Min)
            {
                _scaleOutCooldownUntilUtc = DateTime.UtcNow.AddMinutes(runtime.CooldownMinutes);
                reason = $"Failed start budget exceeded ({_failedStartsInWindow}/{runtime.MaxFailedStartsPer10Min})";
                return false;
            }

            reason = string.Empty;
            return true;
        }
    }

    private void RegisterScaleOutAttempt(OrchestratorRuntimeSettings runtime)
    {
        lock (_budgetLock)
        {
            ResetBudgetWindowIfExpired();
            _startAttemptsInWindow++;
            if (_startAttemptsInWindow > runtime.MaxWorkerStartAttemptsPer10Min)
            {
                _scaleOutCooldownUntilUtc = DateTime.UtcNow.AddMinutes(runtime.CooldownMinutes);
            }
        }
    }

    private void RegisterScaleOutFailure(OrchestratorRuntimeSettings runtime)
    {
        lock (_budgetLock)
        {
            ResetBudgetWindowIfExpired();
            _failedStartsInWindow++;
            if (_failedStartsInWindow >= runtime.MaxFailedStartsPer10Min)
            {
                _scaleOutCooldownUntilUtc = DateTime.UtcNow.AddMinutes(runtime.CooldownMinutes);
            }
        }
    }

    private void RegisterScaleOutSuccess()
    {
        lock (_budgetLock)
        {
            ResetBudgetWindowIfExpired();
        }
    }

    private void ResetBudgetWindowIfExpired()
    {
        var now = DateTime.UtcNow;
        if (now - _startBudgetWindowUtc < ScaleOutAttemptWindow)
        {
            return;
        }

        _startBudgetWindowUtc = now;
        _startAttemptsInWindow = 0;
        _failedStartsInWindow = 0;
    }

    private static bool IsMissingImageException(DockerApiException ex, string image)
    {
        return ex.Message.Contains($"No such image: {image}", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("No such image", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ImageResolutionResult> EnsureTaskRuntimeImageResolvedWithSourceAsync(
        string imageReference,
        OrchestratorRuntimeSettings runtime,
        CancellationToken cancellationToken,
        IProgress<BackgroundWorkSnapshot>? progress = null,
        bool forceRefresh = false)
    {
        var localImageExists = await ImageExistsLocallyAsync(imageReference, cancellationToken);
        if (localImageExists && !forceRefresh)
        {
            ReportTaskRuntimeImageProgress(
                progress,
                $"Task runtime image {imageReference} is already present locally.",
                percentComplete: 100);
            return new ImageResolutionResult(true, "local");
        }

        if (!forceRefresh &&
            _imageFailureCooldownUntilUtc.TryGetValue(imageReference, out var cooldownUntil) &&
            cooldownUntil > DateTime.UtcNow)
        {
            logger.ZLogWarning("Skipping task runtime image resolution for {Image}; cooldown active until {CooldownUntil}", imageReference, cooldownUntil);
            ReportTaskRuntimeImageProgress(
                progress,
                $"Skipped task runtime image resolution for {imageReference}; cooldown active until {cooldownUntil:O}.",
                state: BackgroundWorkState.Failed,
                errorCode: "cooldown_active");
            return UnavailableImageResolution;
        }

        var imageLock = await GetOrCreateImageStateAsync(imageReference, cancellationToken);
        await imageLock.WaitAsync(cancellationToken);
        try
        {
            if (await ImageExistsLocallyAsync(imageReference, cancellationToken))
            {
                localImageExists = true;
                if (!forceRefresh)
                {
                    ReportTaskRuntimeImageProgress(
                        progress,
                        $"Task runtime image {imageReference} became available locally.",
                        percentComplete: 100);
                    return new ImageResolutionResult(true, "local");
                }
            }

            ReportTaskRuntimeImageProgress(progress, $"Acquiring distributed lease for image {imageReference}.");
            await using var lease = await leaseCoordinator.TryAcquireAsync(
                $"image-resolve:{imageReference}",
                TimeSpan.FromSeconds(runtime.ImageBuildTimeoutSeconds + runtime.ImagePullTimeoutSeconds + 120),
                cancellationToken);

            if (lease is null)
            {
                ReportTaskRuntimeImageProgress(progress, $"Waiting for peer to resolve image {imageReference}.");
                return await WaitForPeerImageResolutionAsync(imageReference, runtime, cancellationToken, progress);
            }

            var resolved = await BuildOrPullWithProgressAsync(imageReference, runtime, progress, cancellationToken);
            if (!resolved.Available)
            {
                if (localImageExists)
                {
                    logger.ZLogWarning(
                        "Task runtime image resolution failed for {Image}; keeping existing local image.",
                        imageReference);
                    ReportTaskRuntimeImageProgress(
                        progress,
                        $"Task runtime image resolution failed for {imageReference}; using existing local image.",
                        percentComplete: 100,
                        state: BackgroundWorkState.Succeeded);
                    return new ImageResolutionResult(true, "local-fallback");
                }

                _imageFailureCooldownUntilUtc[imageReference] = DateTime.UtcNow.AddMinutes(runtime.ImageFailureCooldownMinutes);
                ReportTaskRuntimeImageProgress(
                    progress,
                    $"Task runtime image resolution failed for {imageReference}.",
                    state: BackgroundWorkState.Failed,
                    errorCode: "image_resolution_failed",
                    errorMessage: $"Task runtime image resolution failed for {imageReference}.");
                return resolved;
            }

            _imageFailureCooldownUntilUtc.TryRemove(imageReference, out _);
            ReportTaskRuntimeImageProgress(
                progress,
                $"Task runtime image {imageReference} resolved from {resolved.Source}.",
                percentComplete: 100);
            return resolved;
        }
        finally
        {
            imageLock.Release();
        }
    }

    private async Task<ImageResolutionResult> WaitForPeerImageResolutionAsync(
        string imageReference,
        OrchestratorRuntimeSettings runtime,
        CancellationToken cancellationToken,
        IProgress<BackgroundWorkSnapshot>? progress = null)
    {
        var timeout = TimeSpan.FromSeconds(Math.Max(15, runtime.ImagePullTimeoutSeconds));
        var started = DateTime.UtcNow;

        while (DateTime.UtcNow - started < timeout && !cancellationToken.IsCancellationRequested)
        {
            if (await ImageExistsLocallyAsync(imageReference, cancellationToken))
            {
                ReportTaskRuntimeImageProgress(
                    progress,
                    $"Peer resolved task runtime image {imageReference}.",
                    percentComplete: 100);
                return new ImageResolutionResult(true, "peer");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        ReportTaskRuntimeImageProgress(
            progress,
            $"Timed out waiting for peer image resolution for {imageReference}.",
            state: BackgroundWorkState.Failed,
            errorCode: "peer_timeout");
        return UnavailableImageResolution;
    }

    private ValueTask<TaskRuntimeImagePolicy> ResolvePolicyAsync(
        OrchestratorRuntimeSettings runtime,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(runtime.TaskRuntimeImagePolicy);
    }

    private ValueTask<SemaphoreSlim> GetOrCreateImageStateAsync(
        string imageReference,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(_imageAcquireLocks.GetOrAdd(imageReference, _ => new SemaphoreSlim(1, 1)));
    }

    private async Task<ImageResolutionResult> BuildOrPullWithProgressAsync(
        string imageReference,
        OrchestratorRuntimeSettings runtime,
        IProgress<BackgroundWorkSnapshot>? progress,
        CancellationToken cancellationToken)
    {
        var policy = await ResolvePolicyAsync(runtime, cancellationToken);
        return await ResolveImageByPolicyAsync(imageReference, runtime, policy, progress, cancellationToken);
    }

    private async Task<ImageResolutionResult> ResolveImageByPolicyAsync(
        string imageReference,
        OrchestratorRuntimeSettings runtime,
        TaskRuntimeImagePolicy taskRuntimeImagePolicy,
        IProgress<BackgroundWorkSnapshot>? progress,
        CancellationToken cancellationToken)
    {
        logger.ZLogInformation("Resolving task runtime image {Image} with policy {Policy}", imageReference, taskRuntimeImagePolicy);
        ReportTaskRuntimeImageProgress(progress, $"Resolving task runtime image {imageReference} with policy {taskRuntimeImagePolicy}.", percentComplete: 10);

        if (taskRuntimeImagePolicy == TaskRuntimeImagePolicy.PullOnly)
        {
            return await PullWorkerContainerImageAsync(imageReference, runtime, progress, cancellationToken)
                ? new ImageResolutionResult(true, "pull")
                : UnavailableImageResolution;
        }

        if (taskRuntimeImagePolicy == TaskRuntimeImagePolicy.BuildOnly)
        {
            return await BuildWorkerContainerImageAsync(imageReference, runtime, progress, cancellationToken)
                ? new ImageResolutionResult(true, "build")
                : UnavailableImageResolution;
        }

        if (taskRuntimeImagePolicy == TaskRuntimeImagePolicy.PullThenBuild)
        {
            if (await PullWorkerContainerImageAsync(imageReference, runtime, progress, cancellationToken))
            {
                return new ImageResolutionResult(true, "pull");
            }

            return await BuildWorkerContainerImageAsync(imageReference, runtime, progress, cancellationToken)
                ? new ImageResolutionResult(true, "build")
                : UnavailableImageResolution;
        }

        if (taskRuntimeImagePolicy == TaskRuntimeImagePolicy.BuildThenPull)
        {
            if (await BuildWorkerContainerImageAsync(imageReference, runtime, progress, cancellationToken))
            {
                return new ImageResolutionResult(true, "build");
            }

            return await PullWorkerContainerImageAsync(imageReference, runtime, progress, cancellationToken)
                ? new ImageResolutionResult(true, "pull")
                : UnavailableImageResolution;
        }

        if (await PullWorkerContainerImageAsync(imageReference, runtime, progress, cancellationToken))
        {
            return new ImageResolutionResult(true, "pull");
        }

        return await BuildWorkerContainerImageAsync(imageReference, runtime, progress, cancellationToken)
            ? new ImageResolutionResult(true, "build")
            : UnavailableImageResolution;
    }

    private async Task<bool> ImageExistsLocallyAsync(string imageReference, CancellationToken cancellationToken)
    {
        try
        {
            await _dockerClient.Images.InspectImageAsync(imageReference, cancellationToken);
            return true;
        }
        catch (DockerApiException ex) when (IsMissingImageException(ex, imageReference))
        {
            return false;
        }
        catch (Exception ex)
        {
            logger.ZLogWarning(ex, "Failed to inspect task runtime image {Image}", imageReference);
            return false;
        }
    }

    private async Task<bool> PullWorkerContainerImageAsync(
        string imageReference,
        OrchestratorRuntimeSettings runtime,
        IProgress<BackgroundWorkSnapshot>? progress,
        CancellationToken cancellationToken)
    {
        await using var slot = await AcquireConcurrencySlotAsync(isBuild: false, runtime.MaxConcurrentPulls, cancellationToken);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(runtime.ImagePullTimeoutSeconds));

            var imageSplit = SplitImageReference(imageReference);

            logger.ZLogInformation("Pulling task runtime image {Image}", imageReference);
            ReportTaskRuntimeImageProgress(progress, $"Pulling task runtime image {imageReference}.", percentComplete: 20);
            var pullProgress = new Progress<JSONMessage>(message =>
            {
                if (!string.IsNullOrWhiteSpace(message.ProgressMessage))
                {
                    logger.ZLogDebug("Pull progress for {Image}: {Progress}", imageReference, message.ProgressMessage);
                    ReportTaskRuntimeImageProgress(
                        progress,
                        $"Pulling {imageReference}: {message.ProgressMessage}",
                        percentComplete: TryParsePullPercent(message.ProgressMessage));
                }
            });

            await _dockerClient.Images.CreateImageAsync(
                new ImagesCreateParameters
                {
                    FromImage = imageSplit.ImageName,
                    Tag = imageSplit.Tag
                },
                null,
                pullProgress,
                timeoutCts.Token);

            logger.ZLogInformation("Successfully pulled task runtime image {Image}", imageReference);
            ReportTaskRuntimeImageProgress(progress, $"Pulled task runtime image {imageReference}.", percentComplete: 75);
            return true;
        }
        catch (Exception ex)
        {
            logger.ZLogWarning(ex, "Failed to pull task runtime image {Image}", imageReference);
            ReportTaskRuntimeImageProgress(
                progress,
                $"Failed to pull task runtime image {imageReference}.",
                state: BackgroundWorkState.Running,
                errorCode: "pull_failed",
                errorMessage: ex.Message);
            return false;
        }
    }

    private async Task<bool> BuildWorkerContainerImageAsync(
        string imageReference,
        OrchestratorRuntimeSettings runtime,
        IProgress<BackgroundWorkSnapshot>? progress,
        CancellationToken cancellationToken)
    {
        var dockerfilePath = ResolveTaskRuntimeGatewayDockerfilePath(runtime);
        if (string.IsNullOrWhiteSpace(dockerfilePath))
        {
            logger.ZLogWarning("Worker Dockerfile could not be resolved for automatic image build");
            ReportTaskRuntimeImageProgress(
                progress,
                "Worker Dockerfile could not be resolved for automatic image build.",
                state: BackgroundWorkState.Running,
                errorCode: "dockerfile_missing");
            return false;
        }

        foreach (var buildContext in ResolveTaskRuntimeGatewayBuildContextCandidates(dockerfilePath, runtime).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(buildContext))
            {
                continue;
            }

            if (!IsValidTaskRuntimeGatewayBuildContext(buildContext, dockerfilePath))
            {
                logger.ZLogDebug("Skipping invalid worker build context {Context} for dockerfile {Dockerfile}", buildContext, dockerfilePath);
                continue;
            }

            logger.ZLogInformation("Building task runtime image {Image} from {Dockerfile} in context {Context}", imageReference, dockerfilePath, buildContext);
            ReportTaskRuntimeImageProgress(
                progress,
                $"Building task runtime image {imageReference} from context {buildContext}.",
                percentComplete: 35);

            try
            {
                await using var slot = await AcquireConcurrencySlotAsync(isBuild: true, runtime.MaxConcurrentBuilds, cancellationToken);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(runtime.ImageBuildTimeoutSeconds));

                await BuildImageWithCliAsync(imageReference, buildContext, dockerfilePath, progress, timeoutCts.Token);

                logger.ZLogInformation("Built task runtime image {Image}", imageReference);
                ReportTaskRuntimeImageProgress(progress, $"Built task runtime image {imageReference}.", percentComplete: 90);
                return true;
            }
            catch (Exception ex)
            {
                logger.ZLogWarning(ex, "Failed to build task runtime image {Image} from context {Context}", imageReference, buildContext);
                ReportTaskRuntimeImageProgress(
                    progress,
                    $"Failed to build task runtime image {imageReference} from context {buildContext}.",
                    state: BackgroundWorkState.Running,
                    errorCode: "build_failed",
                    errorMessage: ex.Message);
            }
        }

        return false;
    }

    private async Task<IAsyncDisposable> AcquireConcurrencySlotAsync(bool isBuild, int maxConcurrency, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            lock (_concurrencyLock)
            {
                var active = isBuild ? _activeBuilds : _activePulls;
                if (active < maxConcurrency)
                {
                    if (isBuild)
                    {
                        _activeBuilds++;
                    }
                    else
                    {
                        _activePulls++;
                    }

                    return new ConcurrencySlot(this, isBuild);
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(150), cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        throw new OperationCanceledException(cancellationToken);
    }

    private void ReleaseConcurrencySlot(bool isBuild)
    {
        lock (_concurrencyLock)
        {
            if (isBuild)
            {
                _activeBuilds = Math.Max(0, _activeBuilds - 1);
            }
            else
            {
                _activePulls = Math.Max(0, _activePulls - 1);
            }
        }
    }

    private async Task BuildImageWithCliAsync(
        string imageReference,
        string contextPath,
        string dockerfilePath,
        IProgress<BackgroundWorkSnapshot>? progress,
        CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo
        {
            FileName = "docker",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        info.ArgumentList.Add("build");
        info.ArgumentList.Add("-t");
        info.ArgumentList.Add(imageReference);
        info.ArgumentList.Add("-f");
        info.ArgumentList.Add(dockerfilePath);
        info.ArgumentList.Add(contextPath);

        using var process = new Process { StartInfo = info };
        process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                logger.ZLogDebug("Docker build output ({Image}): {Message}", imageReference, args.Data);
                ReportTaskRuntimeImageProgress(
                    progress,
                    $"Building {imageReference}: {args.Data}",
                    percentComplete: TryParseBuildPercent(args.Data));
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                logger.ZLogDebug("Docker build error ({Image}): {Message}", imageReference, args.Data);
                ReportTaskRuntimeImageProgress(
                    progress,
                    $"Building {imageReference}: {args.Data}",
                    percentComplete: TryParseBuildPercent(args.Data));
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to launch docker CLI for task runtime image build");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"docker build for {imageReference} failed with exit code {process.ExitCode}");
        }
    }

    private static void ReportTaskRuntimeImageProgress(
        IProgress<BackgroundWorkSnapshot>? progress,
        string message,
        int? percentComplete = null,
        BackgroundWorkState state = BackgroundWorkState.Running,
        string? errorCode = null,
        string? errorMessage = null)
    {
        if (progress is null)
        {
            return;
        }

        progress.Report(
            new BackgroundWorkSnapshot(
                WorkId: string.Empty,
                OperationKey: string.Empty,
                Kind: BackgroundWorkKind.TaskRuntimeImageResolution,
                State: state,
                PercentComplete: percentComplete,
                Message: message,
                StartedAt: null,
                UpdatedAt: DateTimeOffset.UtcNow,
                ErrorCode: errorCode,
                ErrorMessage: errorMessage));
    }

    private static int? TryParsePullPercent(string? progressMessage)
    {
        if (string.IsNullOrWhiteSpace(progressMessage))
        {
            return null;
        }

        var match = PullProgressRegex.Match(progressMessage);
        if (!match.Success)
        {
            return null;
        }

        if (!double.TryParse(match.Groups["current"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var current))
        {
            return null;
        }

        if (!double.TryParse(match.Groups["total"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var total))
        {
            return null;
        }

        var currentBytes = ConvertSizeToBytes(current, match.Groups["currentUnit"].Value);
        var totalBytes = ConvertSizeToBytes(total, match.Groups["totalUnit"].Value);
        if (totalBytes <= 0)
        {
            return null;
        }

        return Math.Clamp((int)Math.Round((currentBytes / totalBytes) * 100), 0, 99);
    }

    private static int? TryParseBuildPercent(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return null;
        }

        var stepMatch = BuildStepRegex.Match(line);
        if (stepMatch.Success &&
            int.TryParse(stepMatch.Groups["current"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var currentStep) &&
            int.TryParse(stepMatch.Groups["total"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var totalSteps) &&
            totalSteps > 0)
        {
            return Math.Clamp((int)Math.Round((double)currentStep / totalSteps * 100), 0, 95);
        }

        var percentMatch = BuildPercentRegex.Match(line);
        if (percentMatch.Success &&
            int.TryParse(percentMatch.Groups["percent"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPercent))
        {
            return Math.Clamp(parsedPercent, 0, 95);
        }

        return null;
    }

    private static double ConvertSizeToBytes(double value, string unit)
    {
        return unit.Trim().ToUpperInvariant() switch
        {
            "KB" => value * 1024d,
            "MB" => value * 1024d * 1024d,
            "GB" => value * 1024d * 1024d * 1024d,
            "TB" => value * 1024d * 1024d * 1024d * 1024d,
            "PB" => value * 1024d * 1024d * 1024d * 1024d * 1024d,
            _ => value,
        };
    }

    private static string? ResolveTaskRuntimeGatewayDockerfilePath(OrchestratorRuntimeSettings runtime)
    {
        if (!string.IsNullOrWhiteSpace(runtime.WorkerDockerfilePath))
        {
            var configured = Path.GetFullPath(runtime.WorkerDockerfilePath, Directory.GetCurrentDirectory());
            if (File.Exists(configured))
            {
                return configured;
            }
        }

        var configuredPath = Environment.GetEnvironmentVariable("TASK_RUNTIME_GATEWAY_DOCKERFILE_PATH");
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var resolvedConfigured = Path.GetFullPath(configuredPath, Directory.GetCurrentDirectory());
            if (File.Exists(resolvedConfigured))
            {
                return resolvedConfigured;
            }
        }

        var candidates = new[]
        {
            "src/AgentsDashboard.TaskRuntimeGateway/Dockerfile",
            Path.Combine("..", "src", "AgentsDashboard.TaskRuntimeGateway", "Dockerfile"),
            Path.Combine("..", "..", "src", "AgentsDashboard.TaskRuntimeGateway", "Dockerfile"),
            Path.Combine("..", "..", "..", "src", "AgentsDashboard.TaskRuntimeGateway", "Dockerfile"),
            Path.Combine("..", "..", "..", "..", "src", "AgentsDashboard.TaskRuntimeGateway", "Dockerfile")
        };

        foreach (var candidate in candidates)
        {
            var resolved = Path.GetFullPath(candidate, Directory.GetCurrentDirectory());
            if (File.Exists(resolved))
            {
                return resolved;
            }
        }

        foreach (var candidate in candidates)
        {
            var resolved = Path.GetFullPath(candidate, AppContext.BaseDirectory);
            if (File.Exists(resolved))
            {
                return resolved;
            }
        }

        foreach (var root in FindWorkspaceRoots(Directory.GetCurrentDirectory()).Concat(FindWorkspaceRoots(AppContext.BaseDirectory)))
        {
            var candidate = Path.Combine(root, "src", "AgentsDashboard.TaskRuntimeGateway", "Dockerfile");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> ResolveTaskRuntimeGatewayBuildContextCandidates(string dockerfilePath, OrchestratorRuntimeSettings runtime)
    {
        if (!string.IsNullOrWhiteSpace(runtime.WorkerDockerBuildContextPath))
        {
            yield return Path.GetFullPath(runtime.WorkerDockerBuildContextPath, Directory.GetCurrentDirectory());
            yield return Path.GetFullPath(runtime.WorkerDockerBuildContextPath, AppContext.BaseDirectory);
        }

        var configuredContext = Environment.GetEnvironmentVariable("TASK_RUNTIME_GATEWAY_BUILD_CONTEXT");
        if (!string.IsNullOrWhiteSpace(configuredContext))
        {
            yield return Path.GetFullPath(configuredContext, Directory.GetCurrentDirectory());
            yield return Path.GetFullPath(configuredContext, AppContext.BaseDirectory);
        }

        var dockerfileDirectory = Path.GetDirectoryName(dockerfilePath);
        if (!string.IsNullOrWhiteSpace(dockerfileDirectory))
        {
            foreach (var repositoryRoot in FindRepositoryRootCandidates(dockerfileDirectory))
            {
                yield return repositoryRoot;
            }

            yield return Path.GetFullPath(Path.Combine(dockerfileDirectory, ".."));
            yield return dockerfileDirectory;
        }
    }

    private static bool IsValidTaskRuntimeGatewayBuildContext(string buildContext, string dockerfilePath)
    {
        var dockerfileDirectory = Path.GetDirectoryName(dockerfilePath);
        if (string.IsNullOrWhiteSpace(dockerfileDirectory))
        {
            return false;
        }

        var absoluteContext = Path.GetFullPath(buildContext);
        var absoluteDockerfile = Path.GetFullPath(dockerfilePath);
        var dockerfileRelativeToContext = Path.GetRelativePath(absoluteContext, absoluteDockerfile);
        if (dockerfileRelativeToContext.StartsWith("..", StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.Equals(Path.GetFileName(dockerfileDirectory), "AgentsDashboard.TaskRuntimeGateway", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (var requiredPath in TaskRuntimeGatewayBuildContextRequirements)
        {
            if (!Path.Exists(Path.Combine(absoluteContext, requiredPath)))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<string> FindWorkspaceRoots(string startDirectory)
    {
        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "src", "AgentsDashboard.TaskRuntimeGateway", "Dockerfile");
            if (File.Exists(candidate))
            {
                yield return current.FullName;
            }

            current = current.Parent;
        }
    }

    private static IEnumerable<string> FindRepositoryRootCandidates(string? startDirectory)
    {
        if (string.IsNullOrWhiteSpace(startDirectory))
        {
            yield break;
        }

        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "AgentsDashboard.slnx"))
                || File.Exists(Path.Combine(current.FullName, "Directory.Build.props"))
                || File.Exists(Path.Combine(current.FullName, "Directory.Packages.props")))
            {
                yield return current.FullName;
            }

            current = current.Parent;
        }
    }

    private static (string ImageName, string? Tag) SplitImageReference(string imageReference)
    {
        var digestIndex = imageReference.IndexOf('@', StringComparison.Ordinal);
        if (digestIndex >= 0)
        {
            return (imageReference[..digestIndex], "latest");
        }

        var lastColon = imageReference.LastIndexOf(':');
        var lastSlash = imageReference.LastIndexOf('/');
        if (lastColon > lastSlash && lastColon > 0)
        {
            var image = imageReference[..lastColon];
            var tag = imageReference[(lastColon + 1)..];
            return (image, tag);
        }

        return (imageReference, "latest");
    }

    private static string ResolveEffectiveImageReference(string imageReference, string registry)
    {
        var normalizedImage = imageReference?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedImage))
        {
            return string.Empty;
        }

        var normalizedRegistry = registry?.Trim().TrimEnd('/') ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedRegistry))
        {
            return normalizedImage;
        }

        if (normalizedImage.StartsWith($"{normalizedRegistry}/", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedImage;
        }

        return $"{normalizedRegistry}/{normalizedImage.TrimStart('/')}";
    }

    private async Task<TaskRuntimeInstance?> WaitForWorkerReadyAsync(string workerId, OrchestratorRuntimeSettings runtime, CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(runtime.ContainerStartTimeoutSeconds);
        var started = DateTime.UtcNow;
        var delay = TimeSpan.FromMilliseconds(Math.Max(300, runtime.HealthProbeIntervalSeconds * 200));

        if (_workers.TryGetValue(workerId, out var stateBeforeReady))
        {
            stateBeforeReady.LifecycleState = TaskRuntimeLifecycleState.Starting;
            await PersistTaskRuntimeStateAsync(
                stateBeforeReady,
                cancellationToken,
                explicitState: TaskRuntimeState.Starting,
                updateLastActivityUtc: false,
                clearInactiveAfterUtc: true);
        }

        while (DateTime.UtcNow - started < timeout && !cancellationToken.IsCancellationRequested)
        {
            await RefreshWorkersAsync(runtime, cancellationToken);

            if (_workers.TryGetValue(workerId, out var state) && state.IsRunning)
            {
                try
                {
                    var client = clientFactory.CreateTaskRuntimeGatewayService(state.TaskRuntimeId, state.GrpcEndpoint);
                    await client.HeartbeatAsync(new HeartbeatRequest
                    {
                        TaskRuntimeId = "control-plane-startup-probe",
                        HostName = "control-plane",
                        ActiveSlots = 0,
                        MaxSlots = state.MaxSlots,
                        Timestamp = DateTimeOffset.UtcNow
                    });

                    state.LifecycleState = TaskRuntimeLifecycleState.Ready;
                    await PersistTaskRuntimeStateAsync(
                        state,
                        cancellationToken,
                        explicitState: TaskRuntimeState.Ready,
                        updateLastActivityUtc: false,
                        clearInactiveAfterUtc: true);
                    return state.ToRuntime();
                }
                catch
                {
                }
            }

            await Task.Delay(delay, cancellationToken);
        }

        return null;
    }

    private async Task PersistTaskRuntimeStateAsync(
        TaskRuntimeStateEntry state,
        CancellationToken cancellationToken,
        TaskRuntimeState? explicitState = null,
        bool updateLastActivityUtc = true,
        bool clearInactiveAfterUtc = false,
        string? lastError = null)
    {
        try
        {
            var now = DateTime.UtcNow;
            var resolvedState = explicitState ?? MapLifecycleState(state.LifecycleState);
            DateTime? inactiveAfterUtc = null;
            if (resolvedState is TaskRuntimeState.Ready or TaskRuntimeState.Busy)
            {
                var runtime = await runtimeSettingsProvider.GetAsync(cancellationToken);
                inactiveAfterUtc = now.AddMinutes(runtime.TaskRuntimeInactiveTimeoutMinutes);
            }

            await store.UpsertTaskRuntimeStateAsync(
                new TaskRuntimeStateUpdate
                {
                    RuntimeId = state.TaskRuntimeId,
                    RepositoryId = await ResolveRepositoryIdAsync(state.TaskId, cancellationToken),
                    TaskId = state.TaskId,
                    State = resolvedState,
                    ActiveRuns = state.ActiveSlots,
                    MaxParallelRuns = state.MaxSlots,
                    Endpoint = state.GrpcEndpoint,
                    ContainerId = state.ContainerId,
                    ObservedAtUtc = now,
                    UpdateLastActivityUtc = updateLastActivityUtc,
                    InactiveAfterUtc = inactiveAfterUtc,
                    ClearInactiveAfterUtc = clearInactiveAfterUtc,
                    LastError = lastError ?? string.Empty,
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.ZLogDebug(ex, "Failed to persist task runtime state for {TaskRuntimeId}", state.TaskRuntimeId);
        }
    }

    private async Task<string> ResolveRepositoryIdAsync(string taskId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return string.Empty;
        }

        if (_taskRepositoryCache.TryGetValue(taskId, out var cached))
        {
            return cached;
        }

        var task = await store.GetTaskAsync(taskId, cancellationToken);
        if (task is null || string.IsNullOrWhiteSpace(task.RepositoryId))
        {
            return string.Empty;
        }

        _taskRepositoryCache[taskId] = task.RepositoryId;
        return task.RepositoryId;
    }

    private static TaskRuntimeState MapLifecycleState(TaskRuntimeLifecycleState state)
    {
        return state switch
        {
            TaskRuntimeLifecycleState.Provisioning => TaskRuntimeState.Starting,
            TaskRuntimeLifecycleState.Starting => TaskRuntimeState.Starting,
            TaskRuntimeLifecycleState.Ready => TaskRuntimeState.Ready,
            TaskRuntimeLifecycleState.Busy => TaskRuntimeState.Busy,
            TaskRuntimeLifecycleState.Draining => TaskRuntimeState.Busy,
            TaskRuntimeLifecycleState.Stopping => TaskRuntimeState.Stopping,
            TaskRuntimeLifecycleState.Stopped => TaskRuntimeState.Inactive,
            TaskRuntimeLifecycleState.Quarantined => TaskRuntimeState.Failed,
            TaskRuntimeLifecycleState.FailedStart => TaskRuntimeState.Failed,
            _ => TaskRuntimeState.Inactive
        };
    }

    private TaskRuntimeInstance? SelectAvailableWorker(List<TaskRuntimeInstance> runningWorkers)
    {
        return runningWorkers
            .Where(x => x.ActiveSlots < x.MaxSlots && !x.IsDraining && x.LifecycleState is not TaskRuntimeLifecycleState.Quarantined)
            .OrderBy(x => x.ActiveSlots)
            .ThenBy(x => x.CpuPercent)
            .ThenBy(x => x.MemoryPercent)
            .ThenBy(x => x.LastActivityUtc)
            .FirstOrDefault();
    }

    private static HostConfig BuildHostConfig(OrchestratorRuntimeSettings runtime, bool useHostPort, string workerId, string taskId)
    {
        var hostConfig = new HostConfig
        {
            Binds = BuildWorkerBinds(workerId, taskId),
            RestartPolicy = new RestartPolicy { Name = RestartPolicyKind.No },
            PortBindings = useHostPort
                ? new Dictionary<string, IList<PortBinding>>
                {
                    ["5201/tcp"] = [new PortBinding { HostPort = string.Empty }]
                }
                : null
        };

        var nanoCpus = ParseCpuLimitToNanoCpus(runtime.WorkerCpuLimit);
        if (nanoCpus > 0)
        {
            hostConfig.NanoCPUs = nanoCpus;
        }

        if (runtime.WorkerMemoryLimitMb > 0)
        {
            hostConfig.Memory = runtime.WorkerMemoryLimitMb * 1024L * 1024L;
        }

        if (runtime.WorkerPidsLimit > 0)
        {
            hostConfig.PidsLimit = runtime.WorkerPidsLimit;
        }

        if (runtime.WorkerFileDescriptorLimit > 0)
        {
            hostConfig.Ulimits =
            [
                new Ulimit
                {
                    Name = "nofile",
                    Soft = runtime.WorkerFileDescriptorLimit,
                    Hard = runtime.WorkerFileDescriptorLimit
                }
            ];
        }

        return hostConfig;
    }

    private static long ParseCpuLimitToNanoCpus(string cpuLimit)
    {
        if (string.IsNullOrWhiteSpace(cpuLimit))
        {
            return 0;
        }

        if (!double.TryParse(cpuLimit, out var cpu) || cpu <= 0)
        {
            return 0;
        }

        return (long)(cpu * 1_000_000_000L);
    }

    private bool IsManagedWorkerContainer(ContainerListResponse container, OrchestratorRuntimeSettings runtime)
    {
        if (container.Labels is not null &&
            container.Labels.TryGetValue(ManagedByLabel, out var managedBy) &&
            string.Equals(managedBy, ManagedByValue, StringComparison.OrdinalIgnoreCase) &&
            container.Labels.TryGetValue(WorkerRoleLabel, out var role) &&
            string.Equals(role, WorkerRoleValue, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = runtime.ContainerNamePrefix;
        return container.Names.Any(name => name.Trim('/').StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private string ResolveTaskRuntimeId(ContainerListResponse container, string containerName)
    {
        if (container.Labels is not null &&
            container.Labels.TryGetValue(TaskRuntimeIdLabel, out var workerId) &&
            !string.IsNullOrWhiteSpace(workerId))
        {
            return workerId;
        }

        return containerName;
    }

    private static string ResolveTaskId(ContainerListResponse container)
    {
        if (container.Labels is not null &&
            container.Labels.TryGetValue(TaskIdLabel, out var taskId) &&
            !string.IsNullOrWhiteSpace(taskId))
        {
            return taskId;
        }

        return string.Empty;
    }

    private static int ResolveMaxSlots(ContainerListResponse container, int defaultValue)
    {
        if (container.Labels is not null &&
            container.Labels.TryGetValue(MaxSlotsLabel, out var maxSlotsRaw) &&
            int.TryParse(maxSlotsRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
            parsed > 0)
        {
            return parsed;
        }

        return defaultValue;
    }

    private static string BuildTaskRuntimeId(string prefix, string taskId)
    {
        var normalizedPrefix = string.IsNullOrWhiteSpace(prefix) ? "task-runtime" : prefix.Trim();
        var token = NormalizeWorkerToken(taskId);
        var value = $"{normalizedPrefix}-task-{token}";
        return value.Length > 60 ? value[..60] : value;
    }

    private (string grpcEndpoint, string proxyEndpoint) ResolveWorkerEndpoints(ContainerListResponse container, string containerName, OrchestratorRuntimeSettings runtime)
    {
        var mode = ResolveConnectivityMode(runtime);
        if (mode == TaskRuntimeConnectivityMode.DockerDnsOnly)
        {
            var endpoint = $"http://{containerName}:{WorkerGrpcPort}";
            return (endpoint, endpoint);
        }

        if (mode == TaskRuntimeConnectivityMode.HostPortOnly)
        {
            var hostEndpoint = ResolveHostPortEndpoint(container);
            return (hostEndpoint, hostEndpoint);
        }

        if (IsRunningInsideContainer())
        {
            var endpoint = $"http://{containerName}:{WorkerGrpcPort}";
            return (endpoint, endpoint);
        }

        var fallbackHostEndpoint = ResolveHostPortEndpoint(container);
        return (fallbackHostEndpoint, fallbackHostEndpoint);
    }

    private string ResolveHostPortEndpoint(ContainerListResponse container)
    {
        var mapping = container.Ports.FirstOrDefault(x => x.PrivatePort == WorkerGrpcPort && string.Equals(x.Type, "tcp", StringComparison.OrdinalIgnoreCase));
        if (mapping is not null && mapping.PublicPort > 0)
        {
            return $"http://127.0.0.1:{mapping.PublicPort}";
        }

        return $"http://127.0.0.1:{WorkerGrpcPort}";
    }

    private static TaskRuntimeConnectivityMode ResolveConnectivityMode(OrchestratorRuntimeSettings runtime)
    {
        return runtime.ConnectivityMode;
    }

    private static bool IsRunningInsideContainer()
    {
        var runningInContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
        return string.Equals(runningInContainer, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> BuildWorkerBinds(string workerId, string taskId)
    {
        var workerToken = NormalizeWorkerToken(workerId);
        var taskToken = NormalizeWorkerToken(taskId);
        var artifactsDefault = $"worker-artifacts-{workerToken}:/artifacts";
        var runtimeHomeDefault = $"task-runtime-home-{taskToken}:/home/agent";
        var workspacesBind = ResolveWorkspacesBind();

        var binds = new List<string>
        {
            Environment.GetEnvironmentVariable("TASK_RUNTIME_DOCKER_SOCKET_BIND") ?? "/var/run/docker.sock:/var/run/docker.sock",
            artifactsDefault,
            runtimeHomeDefault,
            workspacesBind,
        };

        return binds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolveWorkspacesBind()
    {
        var configuredBind = Environment.GetEnvironmentVariable("TASK_RUNTIME_SHARED_WORKSPACES_BIND");
        if (!string.IsNullOrWhiteSpace(configuredBind))
        {
            return configuredBind;
        }

        return $"{SharedWorkspacesVolumeName}:/workspaces";
    }

    private async Task<(double CpuPercent, double MemoryPercent)> TryGetPressureMetricsAsync(string containerId, CancellationToken cancellationToken)
    {
#pragma warning disable CS0618
        await using var statsStream = await _dockerClient.Containers.GetContainerStatsAsync(
            containerId,
            new ContainerStatsParameters { Stream = false },
            cancellationToken);
#pragma warning restore CS0618

        var stats = await JsonSerializer.DeserializeAsync<ContainerStatsResponse>(statsStream, cancellationToken: cancellationToken);
        if (stats is null)
        {
            return (0, 0);
        }

        var cpuPercent = CalculateCpuPercent(stats);
        var memoryPercent = CalculateMemoryPercent(stats);

        return (cpuPercent, memoryPercent);
    }

    private static double CalculateCpuPercent(ContainerStatsResponse stats)
    {
        var cpuUsage = (double)(stats.CPUStats?.CPUUsage?.TotalUsage ?? 0UL);
        var preCpuUsage = (double)(stats.PreCPUStats?.CPUUsage?.TotalUsage ?? 0UL);
        var cpuDelta = cpuUsage - preCpuUsage;
        var systemUsage = (double)(stats.CPUStats?.SystemUsage ?? 0UL);
        var preSystemUsage = (double)(stats.PreCPUStats?.SystemUsage ?? 0UL);
        var systemDelta = systemUsage - preSystemUsage;
        var onlineCpuCount = stats.CPUStats?.OnlineCPUs is uint online && online > 0
            ? (double)online
            : (double)(stats.CPUStats?.CPUUsage?.PercpuUsage?.Count ?? 1);

        if (cpuDelta <= 0 || systemDelta <= 0 || onlineCpuCount <= 0)
        {
            return 0;
        }

        return cpuDelta / systemDelta * onlineCpuCount * 100.0;
    }

    private static double CalculateMemoryPercent(ContainerStatsResponse stats)
    {
        var usage = stats.MemoryStats?.Usage ?? 0;
        var limit = stats.MemoryStats?.Limit ?? 0;
        if (limit <= 0)
        {
            return 0;
        }

        return (double)usage / limit * 100.0;
    }

    private sealed class ConcurrencySlot(DockerTaskRuntimeLifecycleManager parent, bool isBuild) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            parent.ReleaseConcurrencySlot(isBuild);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TaskRuntimeStateEntry
    {
        public required string TaskRuntimeId { get; init; }
        public required string TaskId { get; set; }
        public required string ContainerId { get; set; }
        public required string ContainerName { get; set; }
        public required bool IsRunning { get; set; }
        public required TaskRuntimeLifecycleState LifecycleState { get; set; }
        public required bool IsDraining { get; set; }
        public required string GrpcEndpoint { get; set; }
        public required string ProxyEndpoint { get; set; }
        public required int ActiveSlots { get; set; }
        public required int MaxSlots { get; set; }
        public required double CpuPercent { get; set; }
        public required double MemoryPercent { get; set; }
        public required DateTime LastActivityUtc { get; set; }
        public required DateTime StartedAtUtc { get; set; }
        public required DateTime DrainingSinceUtc { get; set; }
        public required DateTime LastPressureSampleUtc { get; set; }
        public required int DispatchCount { get; set; }
        public required string ImageRef { get; set; }
        public required string ImageDigest { get; set; }
        public required string ImageSource { get; set; }

        public static TaskRuntimeStateEntry Create(
            string workerId,
            string taskId,
            string containerId,
            string containerName,
            string grpcEndpoint,
            string proxyEndpoint,
            bool isRunning,
            int slotsPerWorker)
        {
            return new TaskRuntimeStateEntry
            {
                TaskRuntimeId = workerId,
                TaskId = taskId,
                ContainerId = containerId,
                ContainerName = containerName,
                IsRunning = isRunning,
                LifecycleState = isRunning ? TaskRuntimeLifecycleState.Ready : TaskRuntimeLifecycleState.Stopped,
                IsDraining = false,
                GrpcEndpoint = grpcEndpoint,
                ProxyEndpoint = proxyEndpoint,
                ActiveSlots = 0,
                MaxSlots = slotsPerWorker,
                CpuPercent = 0,
                MemoryPercent = 0,
                LastActivityUtc = DateTime.UtcNow,
                StartedAtUtc = DateTime.UtcNow,
                DrainingSinceUtc = DateTime.MinValue,
                LastPressureSampleUtc = DateTime.MinValue,
                DispatchCount = 0,
                ImageRef = string.Empty,
                ImageDigest = string.Empty,
                ImageSource = string.Empty
            };
        }

        public TaskRuntimeInstance ToRuntime()
            => new(
                TaskRuntimeId,
                TaskId,
                ContainerId,
                ContainerName,
                IsRunning,
                LifecycleState,
                IsDraining,
                GrpcEndpoint,
                ProxyEndpoint,
                ActiveSlots,
                MaxSlots,
                CpuPercent,
                MemoryPercent,
                LastActivityUtc,
                StartedAtUtc,
                DispatchCount,
                ImageRef,
                ImageDigest,
                ImageSource);
    }
}
