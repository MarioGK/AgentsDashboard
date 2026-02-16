using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.ControlPlane.Configuration;
using AgentsDashboard.ControlPlane.Data;
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class DockerWorkerLifecycleManager(
    IOptions<OrchestratorOptions> options,
    IOrchestratorRuntimeSettingsProvider runtimeSettingsProvider,
    ILeaseCoordinator leaseCoordinator,
    IOrchestratorStore store,
    IMagicOnionClientFactory clientFactory,
    ILogger<DockerWorkerLifecycleManager> logger) : IWorkerLifecycleManager
{
    private const string ManagedByLabel = "orchestrator.managed-by";
    private const string ManagedByValue = "control-plane";
    private const string WorkerRoleLabel = "orchestrator.role";
    private const string WorkerRoleValue = "worker-gateway";
    private const string WorkerIdLabel = "orchestrator.worker-id";
    private const string SharedWorkspacesVolumeName = "agentsdashboard-workspaces";
    private const int WorkerGrpcPort = 5201;
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ScaleOutAttemptWindow = TimeSpan.FromMinutes(10);

    private readonly OrchestratorOptions _options = options.Value;
    private readonly DockerClient _dockerClient = new DockerClientConfiguration().CreateClient();
    private readonly ConcurrentDictionary<string, WorkerState> _workers = new(StringComparer.OrdinalIgnoreCase);
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

    public async Task EnsureWorkerImageAvailableAsync(CancellationToken cancellationToken)
    {
        var runtime = await runtimeSettingsProvider.GetAsync(cancellationToken);
        var baseImage = ResolveEffectiveImageReference(runtime.ContainerImage, runtime.WorkerImageRegistry);
        if (string.IsNullOrWhiteSpace(baseImage))
        {
            return;
        }

        var baseResolution = await EnsureWorkerImageResolvedWithSourceAsync(
            baseImage,
            runtime,
            cancellationToken,
            forceRefresh: true);
        if (!baseResolution.Available)
        {
            logger.LogWarning(
                "Worker image {Image} is unavailable after image policy execution. Dispatch will fail until the image is available.",
                baseImage);
            return;
        }

        if (!string.IsNullOrWhiteSpace(runtime.WorkerCanaryImage) && runtime.CanaryPercent > 0)
        {
            var canaryImage = ResolveEffectiveImageReference(runtime.WorkerCanaryImage, runtime.WorkerImageRegistry);
            var canaryResolution = await EnsureWorkerImageResolvedWithSourceAsync(
                canaryImage,
                runtime,
                cancellationToken,
                forceRefresh: true);
            if (!canaryResolution.Available)
            {
                logger.LogWarning(
                    "Worker canary image {Image} is unavailable; continuing with base image {BaseImage}.",
                    canaryImage,
                    baseImage);
            }
        }

        logger.LogInformation("Worker image {Image} is available", baseImage);
    }

    private sealed record ImageResolutionResult(bool Available, string Source);

    private static readonly ImageResolutionResult UnavailableImageResolution = new(false, string.Empty);

    private sealed record SpawnImageSelection(string Image, bool IsCanary, string CanaryReason);

    public async Task EnsureMinimumWorkersAsync(CancellationToken cancellationToken)
    {
        var runtime = await runtimeSettingsProvider.GetAsync(cancellationToken);
        var workers = await ListWorkersAsync(cancellationToken);
        var runningWorkers = workers.Where(x => x.IsRunning).ToList();

        while (runningWorkers.Count < runtime.MinWorkers)
        {
            if (!CanScaleOut(runtime, out var reason))
            {
                logger.LogWarning(
                    "Unable to satisfy MinWorkers={MinWorkers}. Scale-out blocked: {Reason}",
                    runtime.MinWorkers,
                    reason);
                break;
            }

            var spawned = await SpawnWorkerAsync(runtime, cancellationToken);
            if (spawned is null)
            {
                break;
            }

            runningWorkers.Add(spawned);
        }
    }

    public async Task<WorkerLease?> AcquireWorkerForDispatchAsync(CancellationToken cancellationToken)
    {
        var runtime = await runtimeSettingsProvider.GetAsync(cancellationToken);
        var workers = await ListWorkersAsync(cancellationToken);
        var runningCount = workers.Count(x => x.IsRunning);
        if (runningCount >= runtime.MaxWorkers)
        {
            return null;
        }

        var candidate = await SpawnWorkerAsync(runtime, cancellationToken);
        if (candidate is null)
        {
            return null;
        }

        await RecordDispatchActivityAsync(candidate.WorkerId, cancellationToken);
        return new WorkerLease(
            candidate.WorkerId,
            candidate.ContainerId,
            candidate.GrpcEndpoint,
            candidate.ProxyEndpoint);
    }

    public async Task<WorkerRuntime?> GetWorkerAsync(string workerId, CancellationToken cancellationToken)
    {
        var workers = await ListWorkersAsync(cancellationToken);
        return workers.FirstOrDefault(x => string.Equals(x.WorkerId, workerId, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<IReadOnlyList<WorkerRuntime>> ListWorkersAsync(CancellationToken cancellationToken)
    {
        var runtime = await runtimeSettingsProvider.GetAsync(cancellationToken);
        await RefreshWorkersAsync(runtime, cancellationToken);
        return _workers.Values
            .Select(x => x.ToRuntime())
            .OrderBy(x => x.WorkerId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public Task ReportWorkerHeartbeatAsync(string workerId, int activeSlots, int maxSlots, CancellationToken cancellationToken)
    {
        if (_workers.TryGetValue(workerId, out var state))
        {
            state.ActiveSlots = Math.Max(0, activeSlots);
            state.MaxSlots = maxSlots > 0 ? maxSlots : state.MaxSlots;
            state.LastActivityUtc = DateTime.UtcNow;
            state.LifecycleState = state.ActiveSlots > 0 ? WorkerLifecycleState.Busy : state.IsDraining ? WorkerLifecycleState.Draining : WorkerLifecycleState.Ready;
        }

        return Task.CompletedTask;
    }

    public Task RecordDispatchActivityAsync(string workerId, CancellationToken cancellationToken)
    {
        if (_workers.TryGetValue(workerId, out var state))
        {
            state.LastActivityUtc = DateTime.UtcNow;
            state.DispatchCount++;
            state.LifecycleState = WorkerLifecycleState.Busy;
        }

        return Task.CompletedTask;
    }

    public async Task ScaleDownIdleWorkersAsync(CancellationToken cancellationToken)
    {
        var runtime = await runtimeSettingsProvider.GetAsync(cancellationToken);
        var workers = await ListWorkersAsync(cancellationToken);
        var runningWorkers = workers.Where(x => x.IsRunning).OrderBy(x => x.LastActivityUtc).ToList();
        var idleThreshold = TimeSpan.FromMinutes(_options.Workers.IdleTimeoutMinutes);
        var now = DateTime.UtcNow;
        var runningCount = runningWorkers.Count;

        foreach (var worker in runningWorkers)
        {
            if (runningCount <= runtime.MinWorkers)
            {
                break;
            }

            var isTimedOutIdle = worker.ActiveSlots == 0 && now - worker.LastActivityUtc >= idleThreshold;
            var isDrainReady = worker.IsDraining && worker.ActiveSlots == 0;
            var isDrainTimedOut = worker.IsDraining && now - worker.LastActivityUtc >= TimeSpan.FromSeconds(runtime.DrainTimeoutSeconds);
            var shouldAutoRecycle = runtime.EnableAutoRecycle && worker.ActiveSlots == 0 &&
                                    (worker.DispatchCount >= runtime.RecycleAfterRuns ||
                                     now - worker.StartedAtUtc >= TimeSpan.FromMinutes(runtime.RecycleAfterUptimeMinutes));

            if (!isTimedOutIdle && !isDrainReady && !isDrainTimedOut && !shouldAutoRecycle)
            {
                continue;
            }

            if (await StopWorkerAsync(worker.WorkerId, worker.ContainerId, runtime, force: isDrainTimedOut, cancellationToken))
            {
                runningCount--;
            }
        }
    }

    public Task SetWorkerDrainingAsync(string workerId, bool draining, CancellationToken cancellationToken)
    {
        if (_workers.TryGetValue(workerId, out var state))
        {
            state.IsDraining = draining;
            state.DrainingSinceUtc = draining ? DateTime.UtcNow : DateTime.MinValue;
            state.LifecycleState = draining ? WorkerLifecycleState.Draining : state.ActiveSlots > 0 ? WorkerLifecycleState.Busy : WorkerLifecycleState.Ready;
        }

        return Task.CompletedTask;
    }

    public async Task RecycleWorkerAsync(string workerId, CancellationToken cancellationToken)
    {
        var runtime = await runtimeSettingsProvider.GetAsync(cancellationToken);
        var worker = await GetWorkerAsync(workerId, cancellationToken);
        if (worker is null)
        {
            return;
        }

        await SetWorkerDrainingAsync(workerId, true, cancellationToken);
        await StopWorkerAsync(workerId, worker.ContainerId, runtime, force: true, cancellationToken);
    }

    public async Task RecycleWorkerPoolAsync(CancellationToken cancellationToken)
    {
        var workers = await ListWorkersAsync(cancellationToken);
        foreach (var worker in workers.Where(x => x.IsRunning))
        {
            await SetWorkerDrainingAsync(worker.WorkerId, true, cancellationToken);
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

        var workers = await ListWorkersAsync(cancellationToken);
        foreach (var worker in workers.Where(x => x.IsRunning))
        {
            await store.UpsertWorkerHeartbeatAsync(
                worker.WorkerId,
                worker.GrpcEndpoint,
                worker.ActiveSlots,
                worker.MaxSlots,
                cancellationToken);
        }

        await store.MarkStaleWorkersOfflineAsync(TimeSpan.FromMinutes(2), cancellationToken);
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
        var workers = await ListWorkersAsync(cancellationToken);
        lock (_budgetLock)
        {
            return new OrchestratorHealthSnapshot(
                RunningWorkers: workers.Count(x => x.IsRunning),
                ReadyWorkers: workers.Count(x => x.IsRunning && x.LifecycleState == WorkerLifecycleState.Ready),
                BusyWorkers: workers.Count(x => x.IsRunning && x.LifecycleState == WorkerLifecycleState.Busy),
                DrainingWorkers: workers.Count(x => x.IsRunning && x.IsDraining),
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
                var workerId = ResolveWorkerId(container, containerName);
                var isRunning = string.Equals(container.State, "running", StringComparison.OrdinalIgnoreCase);
                var (grpcEndpoint, proxyEndpoint) = ResolveWorkerEndpoints(container, containerName, runtime);

                var state = _workers.AddOrUpdate(
                    workerId,
                    _ => WorkerState.Create(
                        workerId,
                        container.ID,
                        containerName,
                        grpcEndpoint,
                        proxyEndpoint,
                        isRunning,
                        1),
                    (_, existing) =>
                    {
                        existing.ContainerId = container.ID;
                        existing.ContainerName = containerName;
                        existing.GrpcEndpoint = grpcEndpoint;
                        existing.ProxyEndpoint = proxyEndpoint;
                        existing.IsRunning = isRunning;
                        existing.ImageRef = container.Image ?? existing.ImageRef;
                        existing.ImageDigest = container.ImageID ?? existing.ImageDigest;
                        if (existing.MaxSlots <= 0)
                        {
                            existing.MaxSlots = 1;
                        }

                        if (!existing.IsDraining)
                        {
                            existing.LifecycleState = isRunning
                                ? (existing.ActiveSlots > 0 ? WorkerLifecycleState.Busy : WorkerLifecycleState.Ready)
                                : WorkerLifecycleState.Stopped;
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
                        logger.LogDebug(ex, "Failed to refresh pressure metrics for worker {WorkerId}", workerId);
                    }
                }

                currentIds.Add(workerId);
            }

            foreach (var staleWorkerId in _workers.Keys.Where(id => !currentIds.Contains(id)).ToList())
            {
                _workers.TryRemove(staleWorkerId, out _);
                clientFactory.RemoveWorker(staleWorkerId);
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

    private SpawnImageSelection SelectSpawnImage(OrchestratorRuntimeSettings runtime, IReadOnlyList<WorkerRuntime> workers)
    {
        var baseImage = ResolveEffectiveImageReference(runtime.ContainerImage, runtime.WorkerImageRegistry);
        var canaryImage = ResolveEffectiveImageReference(runtime.WorkerCanaryImage, runtime.WorkerImageRegistry);

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

    private async Task<WorkerRuntime?> SpawnWorkerAsync(OrchestratorRuntimeSettings runtime, CancellationToken cancellationToken)
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
                logger.LogDebug("Skipped worker spawn because scale lease is currently held by another orchestrator instance");
                return null;
            }

            var workers = await ListWorkersAsync(cancellationToken);
            var runningCount = workers.Count(x => x.IsRunning);
            if (runningCount >= runtime.MaxWorkers)
            {
                return null;
            }

            var imageSelection = SelectSpawnImage(runtime, workers);
            var selectedImage = imageSelection.Image;
            var imageResolution = await EnsureWorkerImageResolvedWithSourceAsync(selectedImage, runtime, cancellationToken);
            if (!imageResolution.Available && imageSelection.IsCanary)
            {
                var baseImage = ResolveEffectiveImageReference(runtime.ContainerImage, runtime.WorkerImageRegistry);
                logger.LogWarning(
                    "Canary worker image {CanaryImage} could not be resolved ({Reason}); falling back to base image {BaseImage}.",
                    selectedImage,
                    imageSelection.CanaryReason,
                    baseImage);
                selectedImage = baseImage;
                imageResolution = await EnsureWorkerImageResolvedWithSourceAsync(selectedImage, runtime, cancellationToken);
            }

            if (!imageResolution.Available)
            {
                throw new InvalidOperationException($"Worker image '{selectedImage}' is unavailable.");
            }

            if (!CanScaleOut(runtime, out var blockedReason))
            {
                logger.LogWarning("Scale-out blocked while attempting to spawn worker: {Reason}", blockedReason);
                return null;
            }

            RegisterScaleOutAttempt(runtime);

            var workerNameSeed = $"{runtime.ContainerNamePrefix}-{Guid.NewGuid():N}";
            var workerId = workerNameSeed[..Math.Min(40, workerNameSeed.Length)];
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
            var useHostPort = connectivityMode == WorkerConnectivityMode.HostPortOnly ||
                              (connectivityMode == WorkerConnectivityMode.AutoDetect && !IsRunningInsideContainer());
            await EnsureDockerNetworkAsync(runtime.DockerNetwork, cancellationToken);

            var createParameters = new CreateContainerParameters
            {
                Image = selectedImage,
                Name = containerName,
                Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [ManagedByLabel] = ManagedByValue,
                    [WorkerRoleLabel] = WorkerRoleValue,
                    [WorkerIdLabel] = workerId,
                },
                Env =
                [
                    "Worker__UseDocker=true",
                    $"Worker__WorkerId={workerId}",
                    "Worker__MaxSlots=1",
                    $"Worker__ControlPlaneUrl={ResolveControlPlaneUrl()}",
                    $"Worker__DefaultImage={Environment.GetEnvironmentVariable("WORKER_DEFAULT_IMAGE") ?? "ghcr.io/mariogk/ai-harness:latest"}",
                    $"CODEX_API_KEY={codexApiKey ?? string.Empty}",
                    $"OPENAI_API_KEY={openAiApiKey ?? string.Empty}",
                    $"OPENCODE_API_KEY={Environment.GetEnvironmentVariable("OPENCODE_API_KEY") ?? string.Empty}",
                    $"ANTHROPIC_API_KEY={Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? string.Empty}",
                    $"Z_AI_API_KEY={Environment.GetEnvironmentVariable("Z_AI_API_KEY") ?? string.Empty}",
                ],
                ExposedPorts = useHostPort
                    ? new Dictionary<string, EmptyStruct> { ["5201/tcp"] = default }
                    : null,
                HostConfig = BuildHostConfig(runtime, useHostPort, workerId),
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
            catch (DockerApiException ex) when (IsMissingImageException(ex, createParameters.Image))
            {
                logger.LogWarning(ex, "Worker image {Image} was not found locally; attempting resolution and retry.", createParameters.Image);

                imageResolution = await EnsureWorkerImageResolvedWithSourceAsync(createParameters.Image, runtime, cancellationToken);
                if (!imageResolution.Available)
                {
                    throw new InvalidOperationException(
                        $"Worker image '{createParameters.Image}' is unavailable. Build from source or pull it, then retry.",
                        ex);
                }

                created = await _dockerClient.Containers.CreateContainerAsync(createParameters, cancellationToken);
            }
            catch (DockerApiException ex) when (
                createParameters.NetworkingConfig is not null &&
                ex.Message.Contains("network", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogWarning(ex, "Worker network {Network} is unavailable; retrying worker create without explicit network", runtime.DockerNetwork);
                createParameters.NetworkingConfig = null;
                created = await _dockerClient.Containers.CreateContainerAsync(createParameters, cancellationToken);
            }

            await _dockerClient.Containers.StartContainerAsync(created.ID, new ContainerStartParameters(), cancellationToken);

            await RefreshWorkersAsync(runtime, cancellationToken);

            var worker = await WaitForWorkerReadyAsync(workerId, runtime, cancellationToken);
            if (worker is null)
            {
                RegisterScaleOutFailure(runtime);
                logger.LogWarning("Worker {WorkerId} did not become ready in time", workerId);
                if (_workers.TryGetValue(workerId, out var workerState))
                {
                    workerState.LifecycleState = WorkerLifecycleState.FailedStart;
                }

                return null;
            }

            RegisterScaleOutSuccess();
            if (_workers.TryGetValue(worker.WorkerId, out var readyWorkerState))
            {
                readyWorkerState.ImageRef = createParameters.Image ?? string.Empty;
                readyWorkerState.ImageSource = imageResolution.Source;
                readyWorkerState.ImageDigest = worker.ImageDigest;
            }

            logger.LogInformation("Spawned worker {WorkerId} ({ContainerId})", worker.WorkerId, worker.ContainerId[..Math.Min(12, worker.ContainerId.Length)]);
            return worker;
        }
        catch (Exception ex)
        {
            RegisterScaleOutFailure(runtime);
            logger.LogWarning(ex, "Failed to spawn worker");
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

            logger.LogInformation("Created missing Docker network {Network}", networkName);
        }
        catch (DockerApiException ex)
        {
            if (ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            logger.LogWarning(ex, "Unable to create Docker network {Network}; worker creation may fall back.", networkName);
        }
    }

    private async Task<bool> StopWorkerAsync(
        string workerId,
        string containerId,
        OrchestratorRuntimeSettings runtime,
        bool force,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(runtime.ContainerStopTimeoutSeconds));

        try
        {
            if (_workers.TryGetValue(workerId, out var state))
            {
                state.LifecycleState = WorkerLifecycleState.Stopping;
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

            await RemoveWorkerStorageAsync(workerId, CancellationToken.None);

            if (_workers.TryGetValue(workerId, out var updated))
            {
                updated.IsRunning = false;
                updated.LifecycleState = WorkerLifecycleState.Stopped;
            }

            clientFactory.RemoveWorker(workerId);
            logger.LogInformation("Stopped and removed worker {WorkerId} ({ContainerId})", workerId, containerId[..Math.Min(12, containerId.Length)]);
            return true;
        }
        catch (DockerContainerNotFoundException)
        {
            await RemoveWorkerStorageAsync(workerId, CancellationToken.None);
            _workers.TryRemove(workerId, out _);
            clientFactory.RemoveWorker(workerId);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to stop worker {WorkerId}", workerId);
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
                    logger.LogInformation("Removed worker storage volume {VolumeName} for {WorkerId}", volumeName, workerId);
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to remove worker volume {VolumeName} for {WorkerId}", volumeName, workerId);
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

    private async Task<ImageResolutionResult> EnsureWorkerImageResolvedWithSourceAsync(
        string imageReference,
        OrchestratorRuntimeSettings runtime,
        CancellationToken cancellationToken,
        bool forceRefresh = false)
    {
        var localImageExists = await ImageExistsLocallyAsync(imageReference, cancellationToken);
        if (localImageExists && !forceRefresh)
        {
            return new ImageResolutionResult(true, "local");
        }

        if (!forceRefresh &&
            _imageFailureCooldownUntilUtc.TryGetValue(imageReference, out var cooldownUntil) &&
            cooldownUntil > DateTime.UtcNow)
        {
            logger.LogWarning("Skipping worker image resolution for {Image}; cooldown active until {CooldownUntil}", imageReference, cooldownUntil);
            return UnavailableImageResolution;
        }

        var imageLock = _imageAcquireLocks.GetOrAdd(imageReference, _ => new SemaphoreSlim(1, 1));
        await imageLock.WaitAsync(cancellationToken);
        try
        {
            if (await ImageExistsLocallyAsync(imageReference, cancellationToken))
            {
                localImageExists = true;
                if (!forceRefresh)
                {
                    return new ImageResolutionResult(true, "local");
                }
            }

            await using var lease = await leaseCoordinator.TryAcquireAsync(
                $"image-resolve:{imageReference}",
                TimeSpan.FromSeconds(runtime.ImageBuildTimeoutSeconds + runtime.ImagePullTimeoutSeconds + 120),
                cancellationToken);

            if (lease is null)
            {
                return await WaitForPeerImageResolutionAsync(imageReference, runtime, cancellationToken);
            }

            var resolved = await ResolveImageByPolicyAsync(imageReference, runtime, cancellationToken);
            if (!resolved.Available)
            {
                if (localImageExists)
                {
                    logger.LogWarning(
                        "Worker image resolution failed for {Image}; keeping existing local image.",
                        imageReference);
                    return new ImageResolutionResult(true, "local-fallback");
                }

                _imageFailureCooldownUntilUtc[imageReference] = DateTime.UtcNow.AddMinutes(runtime.ImageFailureCooldownMinutes);
                return resolved;
            }

            _imageFailureCooldownUntilUtc.TryRemove(imageReference, out _);
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
        CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(Math.Max(15, runtime.ImagePullTimeoutSeconds));
        var started = DateTime.UtcNow;

        while (DateTime.UtcNow - started < timeout && !cancellationToken.IsCancellationRequested)
        {
            if (await ImageExistsLocallyAsync(imageReference, cancellationToken))
            {
                return new ImageResolutionResult(true, "peer");
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        return UnavailableImageResolution;
    }

    private async Task<ImageResolutionResult> ResolveImageByPolicyAsync(
        string imageReference,
        OrchestratorRuntimeSettings runtime,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Resolving worker image {Image} with policy {Policy}", imageReference, runtime.WorkerImagePolicy);

        if (runtime.WorkerImagePolicy == WorkerImagePolicy.PullOnly)
        {
            return await PullWorkerContainerImageAsync(imageReference, runtime, cancellationToken)
                ? new ImageResolutionResult(true, "pull")
                : UnavailableImageResolution;
        }

        if (runtime.WorkerImagePolicy == WorkerImagePolicy.BuildOnly)
        {
            return await BuildWorkerContainerImageAsync(imageReference, runtime, cancellationToken)
                ? new ImageResolutionResult(true, "build")
                : UnavailableImageResolution;
        }

        if (runtime.WorkerImagePolicy == WorkerImagePolicy.PullThenBuild)
        {
            if (await PullWorkerContainerImageAsync(imageReference, runtime, cancellationToken))
            {
                return new ImageResolutionResult(true, "pull");
            }

            return await BuildWorkerContainerImageAsync(imageReference, runtime, cancellationToken)
                ? new ImageResolutionResult(true, "build")
                : UnavailableImageResolution;
        }

        if (runtime.WorkerImagePolicy == WorkerImagePolicy.BuildThenPull)
        {
            if (await BuildWorkerContainerImageAsync(imageReference, runtime, cancellationToken))
            {
                return new ImageResolutionResult(true, "build");
            }

            return await PullWorkerContainerImageAsync(imageReference, runtime, cancellationToken)
                ? new ImageResolutionResult(true, "pull")
                : UnavailableImageResolution;
        }

        if (await PullWorkerContainerImageAsync(imageReference, runtime, cancellationToken))
        {
            return new ImageResolutionResult(true, "pull");
        }

        return await BuildWorkerContainerImageAsync(imageReference, runtime, cancellationToken)
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
            logger.LogWarning(ex, "Failed to inspect worker image {Image}", imageReference);
            return false;
        }
    }

    private async Task<bool> PullWorkerContainerImageAsync(string imageReference, OrchestratorRuntimeSettings runtime, CancellationToken cancellationToken)
    {
        await using var slot = await AcquireConcurrencySlotAsync(isBuild: false, runtime.MaxConcurrentPulls, cancellationToken);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(runtime.ImagePullTimeoutSeconds));

            var imageSplit = SplitImageReference(imageReference);

            logger.LogInformation("Pulling worker image {Image}", imageReference);
            var progress = new Progress<JSONMessage>(message =>
            {
                if (!string.IsNullOrWhiteSpace(message.ProgressMessage))
                {
                    logger.LogDebug("Pull progress for {Image}: {Progress}", imageReference, message.ProgressMessage);
                }
            });

            await _dockerClient.Images.CreateImageAsync(
                new ImagesCreateParameters
                {
                    FromImage = imageSplit.ImageName,
                    Tag = imageSplit.Tag
                },
                null,
                progress,
                timeoutCts.Token);

            logger.LogInformation("Successfully pulled worker image {Image}", imageReference);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to pull worker image {Image}", imageReference);
            return false;
        }
    }

    private async Task<bool> BuildWorkerContainerImageAsync(string imageReference, OrchestratorRuntimeSettings runtime, CancellationToken cancellationToken)
    {
        var dockerfilePath = ResolveWorkerGatewayDockerfilePath(runtime);
        if (string.IsNullOrWhiteSpace(dockerfilePath))
        {
            logger.LogWarning("Worker Dockerfile could not be resolved for automatic image build");
            return false;
        }

        foreach (var buildContext in ResolveWorkerGatewayBuildContextCandidates(dockerfilePath, runtime).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(buildContext))
            {
                continue;
            }

            logger.LogInformation("Building worker image {Image} from {Dockerfile} in context {Context}", imageReference, dockerfilePath, buildContext);

            try
            {
                await using var slot = await AcquireConcurrencySlotAsync(isBuild: true, runtime.MaxConcurrentBuilds, cancellationToken);

                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(runtime.ImageBuildTimeoutSeconds));

                await BuildImageWithCliAsync(imageReference, buildContext, dockerfilePath, timeoutCts.Token);

                logger.LogInformation("Built worker image {Image}", imageReference);
                return true;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to build worker image {Image} from context {Context}", imageReference, buildContext);
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

    private async Task BuildImageWithCliAsync(string imageReference, string contextPath, string dockerfilePath, CancellationToken cancellationToken)
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
                logger.LogDebug("Docker build output ({Image}): {Message}", imageReference, args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                logger.LogDebug("Docker build error ({Image}): {Message}", imageReference, args.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to launch docker CLI for worker image build");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"docker build for {imageReference} failed with exit code {process.ExitCode}");
        }
    }

    private static string? ResolveWorkerGatewayDockerfilePath(OrchestratorRuntimeSettings runtime)
    {
        if (!string.IsNullOrWhiteSpace(runtime.WorkerDockerfilePath))
        {
            var configured = Path.GetFullPath(runtime.WorkerDockerfilePath, Directory.GetCurrentDirectory());
            if (File.Exists(configured))
            {
                return configured;
            }
        }

        var configuredPath = Environment.GetEnvironmentVariable("WORKER_GATEWAY_DOCKERFILE_PATH");
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
            "src/AgentsDashboard.WorkerGateway/Dockerfile",
            Path.Combine("..", "src", "AgentsDashboard.WorkerGateway", "Dockerfile"),
            Path.Combine("..", "..", "src", "AgentsDashboard.WorkerGateway", "Dockerfile"),
            Path.Combine("..", "..", "..", "src", "AgentsDashboard.WorkerGateway", "Dockerfile"),
            Path.Combine("..", "..", "..", "..", "src", "AgentsDashboard.WorkerGateway", "Dockerfile")
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
            var candidate = Path.Combine(root, "src", "AgentsDashboard.WorkerGateway", "Dockerfile");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> ResolveWorkerGatewayBuildContextCandidates(string dockerfilePath, OrchestratorRuntimeSettings runtime)
    {
        if (!string.IsNullOrWhiteSpace(runtime.WorkerDockerBuildContextPath))
        {
            yield return Path.GetFullPath(runtime.WorkerDockerBuildContextPath, Directory.GetCurrentDirectory());
            yield return Path.GetFullPath(runtime.WorkerDockerBuildContextPath, AppContext.BaseDirectory);
        }

        var configuredContext = Environment.GetEnvironmentVariable("WORKER_GATEWAY_BUILD_CONTEXT");
        if (!string.IsNullOrWhiteSpace(configuredContext))
        {
            yield return Path.GetFullPath(configuredContext, Directory.GetCurrentDirectory());
            yield return Path.GetFullPath(configuredContext, AppContext.BaseDirectory);
        }

        var dockerfileDirectory = Path.GetDirectoryName(dockerfilePath);
        if (!string.IsNullOrWhiteSpace(dockerfileDirectory))
        {
            yield return Path.GetFullPath(Path.Combine(dockerfileDirectory, ".."));
            yield return dockerfileDirectory;
        }
    }

    private static IEnumerable<string> FindWorkspaceRoots(string startDirectory)
    {
        var current = new DirectoryInfo(startDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "src", "AgentsDashboard.WorkerGateway", "Dockerfile");
            if (File.Exists(candidate))
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

    private async Task<WorkerRuntime?> WaitForWorkerReadyAsync(string workerId, OrchestratorRuntimeSettings runtime, CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(runtime.ContainerStartTimeoutSeconds);
        var started = DateTime.UtcNow;
        var delay = TimeSpan.FromMilliseconds(Math.Max(300, runtime.HealthProbeIntervalSeconds * 200));

        if (_workers.TryGetValue(workerId, out var stateBeforeReady))
        {
            stateBeforeReady.LifecycleState = WorkerLifecycleState.Starting;
        }

        while (DateTime.UtcNow - started < timeout && !cancellationToken.IsCancellationRequested)
        {
            await RefreshWorkersAsync(runtime, cancellationToken);

            if (_workers.TryGetValue(workerId, out var state) && state.IsRunning)
            {
                try
                {
                    var client = clientFactory.CreateWorkerGatewayService(state.WorkerId, state.GrpcEndpoint);
                    await client.HeartbeatAsync(new HeartbeatRequest
                    {
                        WorkerId = "control-plane-startup-probe",
                        HostName = "control-plane",
                        ActiveSlots = 0,
                        MaxSlots = state.MaxSlots,
                        Timestamp = DateTimeOffset.UtcNow
                    });

                    state.LifecycleState = WorkerLifecycleState.Ready;
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

    private WorkerRuntime? SelectAvailableWorker(List<WorkerRuntime> runningWorkers)
    {
        return runningWorkers
            .Where(x => x.ActiveSlots < x.MaxSlots && !x.IsDraining && x.LifecycleState is not WorkerLifecycleState.Quarantined)
            .OrderBy(x => x.ActiveSlots)
            .ThenBy(x => x.CpuPercent)
            .ThenBy(x => x.MemoryPercent)
            .ThenBy(x => x.LastActivityUtc)
            .FirstOrDefault();
    }

    private static HostConfig BuildHostConfig(OrchestratorRuntimeSettings runtime, bool useHostPort, string workerId)
    {
        var hostConfig = new HostConfig
        {
            Binds = BuildWorkerBinds(workerId),
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

    private string ResolveWorkerId(ContainerListResponse container, string containerName)
    {
        if (container.Labels is not null &&
            container.Labels.TryGetValue(WorkerIdLabel, out var workerId) &&
            !string.IsNullOrWhiteSpace(workerId))
        {
            return workerId;
        }

        return containerName;
    }

    private (string grpcEndpoint, string proxyEndpoint) ResolveWorkerEndpoints(ContainerListResponse container, string containerName, OrchestratorRuntimeSettings runtime)
    {
        var mode = ResolveConnectivityMode(runtime);
        if (mode == WorkerConnectivityMode.DockerDnsOnly)
        {
            var endpoint = $"http://{containerName}:{WorkerGrpcPort}";
            return (endpoint, endpoint);
        }

        if (mode == WorkerConnectivityMode.HostPortOnly)
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

    private static WorkerConnectivityMode ResolveConnectivityMode(OrchestratorRuntimeSettings runtime)
    {
        return runtime.ConnectivityMode;
    }

    private static bool IsRunningInsideContainer()
    {
        var runningInContainer = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
        return string.Equals(runningInContainer, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> BuildWorkerBinds(string workerId)
    {
        var workerToken = NormalizeWorkerToken(workerId);
        var artifactsDefault = $"worker-artifacts-{workerToken}:/artifacts";
        var workspacesBind = ResolveWorkspacesBind();

        var binds = new List<string>
        {
            Environment.GetEnvironmentVariable("WORKER_DOCKER_SOCKET_BIND") ?? "/var/run/docker.sock:/var/run/docker.sock",
            artifactsDefault,
            workspacesBind,
        };

        return binds
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolveWorkspacesBind()
    {
        var configuredBind = Environment.GetEnvironmentVariable("WORKER_SHARED_WORKSPACES_BIND");
        if (!string.IsNullOrWhiteSpace(configuredBind))
        {
            return configuredBind;
        }

        return $"{SharedWorkspacesVolumeName}:/workspaces";
    }

    private string ResolveControlPlaneUrl()
    {
        var inDockerUrl = Environment.GetEnvironmentVariable("WORKER_CONTROL_PLANE_URL");
        if (!string.IsNullOrWhiteSpace(inDockerUrl))
        {
            return inDockerUrl;
        }

        return "http://control-plane:8080";
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

    private sealed class ConcurrencySlot(DockerWorkerLifecycleManager parent, bool isBuild) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            parent.ReleaseConcurrencySlot(isBuild);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class WorkerState
    {
        public required string WorkerId { get; init; }
        public required string ContainerId { get; set; }
        public required string ContainerName { get; set; }
        public required bool IsRunning { get; set; }
        public required WorkerLifecycleState LifecycleState { get; set; }
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

        public static WorkerState Create(
            string workerId,
            string containerId,
            string containerName,
            string grpcEndpoint,
            string proxyEndpoint,
            bool isRunning,
            int slotsPerWorker)
        {
            return new WorkerState
            {
                WorkerId = workerId,
                ContainerId = containerId,
                ContainerName = containerName,
                IsRunning = isRunning,
                LifecycleState = isRunning ? WorkerLifecycleState.Ready : WorkerLifecycleState.Stopped,
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

        public WorkerRuntime ToRuntime()
            => new(
                WorkerId,
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
