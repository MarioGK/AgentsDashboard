using System.Collections.Concurrent;
using System.Text.Json;




namespace AgentsDashboard.ControlPlane.Features.RuntimeOrchestration.Services;

public sealed class TaskRuntimeEventListenerService(
    IMagicOnionClientFactory clientFactory,
    ITaskRuntimeLifecycleManager lifecycleManager,
    IRepositoryStore repositoryStore,
    ITaskStore taskStore,
    IRunStore runStore,
    IRuntimeStore runtimeStore,
    ITaskSemanticEmbeddingService taskSemanticEmbeddingService,
    ITaskRuntimeRegistryService workerRegistry,
    IRunEventPublisher publisher,
    RunDispatcher dispatcher,
    ILogger<TaskRuntimeEventListenerService> logger,
    IRunStructuredViewService? runStructuredViewService = null) : BackgroundService, ITaskRuntimeEventReceiver
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TaskRuntimeTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan EventHubProbeTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DiffPublishThrottle = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ToolPublishThrottle = TimeSpan.FromMilliseconds(125);
    private const int EventHubFailureWarnThreshold = 3;
    private const long MaxArtifactBytesPerArtifact = 104_857_600;
    private const long MaxArtifactBytesPerRun = 262_144_000;

    private readonly ConcurrentDictionary<string, TaskRuntimeHubConnection> _connections = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _connectionFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _structuredPublishWatermarks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _structuredSequenceWatermarks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ArtifactAssemblyState> _artifactAssemblies = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _artifactRunByteTotals = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _backlogReplayInFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _runtimeEventCheckpoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly IRunStructuredViewService _runStructuredViewService = runStructuredViewService ?? NullRunStructuredViewService.Instance;
    private static readonly ITaskRuntimeEventReceiver s_eventHubProbeReceiver = new EventHubProbeReceiver();

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncConnectionsAsync(stoppingToken);
                await runtimeStore.MarkStaleTaskRuntimeRegistrationsOfflineAsync(TaskRuntimeTtl, stoppingToken);
                await ReplayConnectedRuntimeBacklogsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Worker event listener synchronization failed");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task SyncConnectionsAsync(CancellationToken cancellationToken)
    {
        var workers = await lifecycleManager.ListTaskRuntimesAsync(cancellationToken);
        var runningWorkers = workers.Where(x => x.IsRunning).ToDictionary(x => x.TaskRuntimeId, StringComparer.OrdinalIgnoreCase);

        foreach (var worker in runningWorkers.Values)
        {
            if (string.IsNullOrWhiteSpace(worker.GrpcEndpoint))
            {
                logger.LogDebug("Skipping event hub connection for {TaskRuntimeId} because gRPC endpoint is empty", worker.TaskRuntimeId);
                continue;
            }

            if (_connections.TryGetValue(worker.TaskRuntimeId, out var existing))
            {
                if (string.Equals(existing.Endpoint, worker.GrpcEndpoint, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                await RemoveConnectionAsync(worker.TaskRuntimeId);
            }

            var connection = TaskRuntimeHubConnection.Create(worker.TaskRuntimeId, worker.GrpcEndpoint);
            if (_connections.TryAdd(worker.TaskRuntimeId, connection))
            {
                connection.ConnectionTask = Task.Run(() => RunConnectionLoopAsync(connection, connection.Cancellation.Token), CancellationToken.None);
            }
        }

        foreach (var knownTaskRuntimeId in _connections.Keys.ToList())
        {
            if (runningWorkers.ContainsKey(knownTaskRuntimeId))
            {
                continue;
            }

            await RemoveConnectionAsync(knownTaskRuntimeId);
        }
    }

    private async Task RunConnectionLoopAsync(TaskRuntimeHubConnection connection, CancellationToken cancellationToken)
    {
        var reconnectDelay = TimeSpan.FromSeconds(1);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var endpoint = await ResolveWorkerEventHubEndpointAsync(connection.TaskRuntimeId, connection.Endpoint, cancellationToken);
                if (endpoint is null)
                {
                    RecordWorkerEventHubConnectionFailure(
                        connection.TaskRuntimeId,
                        connection.Endpoint,
                        "Worker event hub is not yet accepting streaming connections.");
                    await Task.Delay(reconnectDelay, cancellationToken);
                    reconnectDelay = TimeSpan.FromMilliseconds(Math.Min(reconnectDelay.TotalMilliseconds * 2, 30000));
                    continue;
                }

                if (!string.Equals(endpoint, connection.Endpoint, StringComparison.OrdinalIgnoreCase))
                {
                    connection.Endpoint = endpoint;
                }

                await TryReplayRuntimeBacklogAsync(connection.TaskRuntimeId, connection.Endpoint, cancellationToken);

                logger.LogInformation("Connecting worker event hub for {TaskRuntimeId} at {Endpoint}", connection.TaskRuntimeId, connection.Endpoint);
                var receiver = new RuntimeScopedEventReceiver(connection.TaskRuntimeId, this);
                var hub = await clientFactory.ConnectEventHubAsync(connection.TaskRuntimeId, connection.Endpoint, receiver, cancellationToken);
                connection.SetHub(hub);
                _connectionFailures.TryRemove(connection.TaskRuntimeId, out _);
                reconnectDelay = TimeSpan.FromSeconds(1);

                await hub.SubscribeAsync(runIds: null);
                await hub.WaitForDisconnect();

                logger.LogWarning("Worker event hub disconnected for {TaskRuntimeId}", connection.TaskRuntimeId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                RecordWorkerEventHubConnectionFailure(connection.TaskRuntimeId, connection.Endpoint, ex.Message.ReplaceLineEndings(" "), ex);
                await Task.Delay(reconnectDelay, cancellationToken);
                reconnectDelay = TimeSpan.FromMilliseconds(Math.Min(reconnectDelay.TotalMilliseconds * 2, 30000));
            }
            finally
            {
                await connection.DisposeHubAsync();
            }
        }
    }

    private async Task RemoveConnectionAsync(string runtimeId)
    {
        if (!_connections.TryRemove(runtimeId, out var connection))
        {
            return;
        }

        _connectionFailures.TryRemove(runtimeId, out _);
        _backlogReplayInFlight.TryRemove(runtimeId, out _);
        _runtimeEventCheckpoints.TryRemove(runtimeId, out _);
        connection.Cancellation.Cancel();

        try
        {
            if (connection.ConnectionTask is not null)
            {
                await connection.ConnectionTask;
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            await connection.DisposeHubAsync();
            connection.Cancellation.Dispose();
            clientFactory.RemoveTaskRuntime(runtimeId);
        }
    }

    void ITaskRuntimeEventReceiver.OnJobEvent(JobEventMessage eventMessage)
    {
        _ = HandleJobEventAsync(string.Empty, eventMessage);
    }

    void ITaskRuntimeEventReceiver.OnTaskRuntimeStatusChanged(TaskRuntimeStatusMessage statusMessage)
    {
        _ = HandleTaskRuntimeStatusAsync(string.Empty, statusMessage);
    }

    private async Task<string?> ResolveWorkerEventHubEndpointAsync(string runtimeId, string preferredEndpoint, CancellationToken cancellationToken)
    {
        if (await IsEventHubReachableAsync(runtimeId, preferredEndpoint, cancellationToken))
        {
            return preferredEndpoint;
        }

        var workers = await lifecycleManager.ListTaskRuntimesAsync(cancellationToken);
        var worker = workers.FirstOrDefault(x => string.Equals(x.TaskRuntimeId, runtimeId, StringComparison.OrdinalIgnoreCase));
        var candidateEndpoint = worker?.ProxyEndpoint;

        if (string.IsNullOrWhiteSpace(candidateEndpoint) ||
            string.Equals(candidateEndpoint, preferredEndpoint, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (await IsEventHubReachableAsync(runtimeId, candidateEndpoint, cancellationToken))
        {
            logger.LogInformation(
                "Worker event hub endpoint fallback used for {TaskRuntimeId}: {Endpoint}",
                runtimeId,
                candidateEndpoint);
            return candidateEndpoint;
        }

        return null;
    }

    private async Task<bool> IsEventHubReachableAsync(string runtimeId, string endpoint, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return false;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(EventHubProbeTimeout);
        try
        {
            var hub = await clientFactory.ConnectEventHubAsync(runtimeId, endpoint, s_eventHubProbeReceiver, timeout.Token);
            await hub.DisposeAsync();
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    private void RecordWorkerEventHubConnectionFailure(string runtimeId, string endpoint, string message, Exception? ex = null)
    {
        var consecutiveFailures = _connectionFailures.AddOrUpdate(runtimeId, 1, (_, value) => value + 1);
        var shouldWarn = consecutiveFailures >= EventHubFailureWarnThreshold && consecutiveFailures % EventHubFailureWarnThreshold == 0;
        if (!shouldWarn)
        {
            if (ex is null)
            {
                logger.LogDebug(
                    "Worker event hub connection failed for {TaskRuntimeId} at {Endpoint}: {ErrorMessage}",
                    runtimeId,
                    endpoint,
                    message);
                return;
            }

            logger.LogDebug(ex,
                "Worker event hub connection failed for {TaskRuntimeId} at {Endpoint}: {ErrorMessage}",
                runtimeId,
                endpoint,
                message);
            return;
        }

        if (ex is null)
        {
            logger.LogWarning(
                "Worker event hub connection failed for {TaskRuntimeId} at {Endpoint}: {ErrorMessage}",
                runtimeId,
                endpoint,
                message);
            return;
        }

        logger.LogWarning(ex,
            "Worker event hub connection failed for {TaskRuntimeId} at {Endpoint}: {ErrorMessage}",
            runtimeId,
            endpoint,
            message);
    }

    private async Task HandleJobEventAsync(string runtimeId, JobEventMessage message)
    {
        try
        {
            if (!await ShouldProcessDeliveryAsync(runtimeId, message.DeliveryId))
            {
                return;
            }

            if (IsArtifactEvent(message.EventType))
            {
                await HandleArtifactEventAsync(message, CancellationToken.None);
                await AdvanceRuntimeCheckpointAsync(runtimeId, message.DeliveryId);
                return;
            }

            var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(message.Timestamp).UtcDateTime;
            await HandleStructuredEventAsync(message, timestamp);

            if (string.Equals(message.EventType, "log_chunk", StringComparison.OrdinalIgnoreCase))
            {
                var logChunkEvent = new RunLogEvent
                {
                    RunId = message.RunId,
                    Level = "chunk",
                    Message = message.Summary ?? string.Empty,
                    TimestampUtc = timestamp,
                };
                await publisher.PublishLogAsync(logChunkEvent, CancellationToken.None);
                await AdvanceRuntimeCheckpointAsync(runtimeId, message.DeliveryId);
                return;
            }

            var logEvent = new RunLogEvent
            {
                RunId = message.RunId,
                Level = message.EventType,
                Message = message.Summary ?? message.Error ?? string.Empty,
                TimestampUtc = timestamp,
            };

            await runStore.AddRunLogAsync(logEvent, CancellationToken.None);
            await publisher.PublishLogAsync(logEvent, CancellationToken.None);

            if (!string.Equals(message.EventType, "completed", StringComparison.OrdinalIgnoreCase))
            {
                await AdvanceRuntimeCheckpointAsync(runtimeId, message.DeliveryId);
                return;
            }

            var payloadJson = message.Metadata?.GetValueOrDefault("payload");
            var envelope = ParseEnvelope(payloadJson);
            var succeeded = string.Equals(envelope.Status, "succeeded", StringComparison.OrdinalIgnoreCase);
            var isObsoleteDisposition =
                (envelope.Metadata.TryGetValue("runDisposition", out var runDisposition) &&
                 string.Equals(runDisposition, "obsolete", StringComparison.OrdinalIgnoreCase)) ||
                (message.Metadata?.TryGetValue("runDisposition", out var messageDisposition) == true &&
                 string.Equals(messageDisposition, "obsolete", StringComparison.OrdinalIgnoreCase));

            string? prUrl = envelope.Metadata.TryGetValue("prUrl", out var url) ? url : null;

            var failureClass = ResolveFailureClassFromCompletion(envelope, succeeded);

            var completedRun = await runStore.MarkRunCompletedAsync(
                message.RunId,
                succeeded,
                envelope.Summary,
                payloadJson ?? string.Empty,
                CancellationToken.None,
                failureClass: failureClass,
                prUrl: prUrl);

            if (completedRun is null)
            {
                await AdvanceRuntimeCheckpointAsync(runtimeId, message.DeliveryId);
                return;
            }

            if (isObsoleteDisposition)
            {
                var obsoleteRun = await runStore.MarkRunObsoleteAsync(message.RunId, CancellationToken.None);
                if (obsoleteRun is not null)
                {
                    completedRun = obsoleteRun;
                }
            }

            await FinalizePendingArtifactsAsync(completedRun.Id, CancellationToken.None);

            var gitSyncError = ResolveGitSyncError(envelope);
            try
            {
                await taskStore.UpdateTaskGitMetadataAsync(
                    completedRun.TaskId,
                    timestamp,
                    gitSyncError,
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to update task git metadata for task {TaskId}", completedRun.TaskId);
            }

            taskSemanticEmbeddingService.QueueTaskEmbedding(
                completedRun.RepositoryId,
                completedRun.TaskId,
                "run-history",
                runId: completedRun.Id);

            await publisher.PublishStatusAsync(completedRun, CancellationToken.None);

            await TryDispatchNextQueuedRunAsync(completedRun.TaskId);

            if (!succeeded)
            {
                await TryRetryAsync(completedRun);
            }

            ClearStructuredRunCaches(completedRun.Id);
            ClearArtifactCaches(completedRun.Id);
            await AdvanceRuntimeCheckpointAsync(runtimeId, message.DeliveryId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to handle job event for run {RunId}", message.RunId);
        }
    }

    private async Task HandleStructuredEventAsync(JobEventMessage message, DateTime timestampUtc)
    {
        if (!TryCreateStructuredEventDocument(message, timestampUtc, out var structuredEvent))
        {
            return;
        }

        try
        {
            var stored = await runStore.AppendRunStructuredEventAsync(structuredEvent, CancellationToken.None);
            var decoded = RunStructuredEventCodec.Decode(stored);

            var projectionDelta = await _runStructuredViewService.ApplyStructuredEventAsync(stored, CancellationToken.None);
            await publisher.PublishStructuredEventChangedAsync(
                stored.RunId,
                stored.Sequence,
                decoded.Category,
                decoded.PayloadJson,
                decoded.Schema,
                decoded.TimestampUtc,
                CancellationToken.None);

            if (projectionDelta.DiffUpdated is not null)
            {
                var diff = projectionDelta.DiffUpdated;
                await runStore.UpsertRunDiffSnapshotAsync(
                    new RunDiffSnapshotDocument
                    {
                        RunId = stored.RunId,
                        RepositoryId = stored.RepositoryId,
                        TaskId = stored.TaskId,
                        Sequence = diff.Sequence,
                        Summary = diff.Summary,
                        DiffStat = diff.DiffStat,
                        DiffPatch = diff.DiffPatch,
                        SchemaVersion = diff.Schema,
                        TimestampUtc = diff.TimestampUtc == default ? stored.TimestampUtc : diff.TimestampUtc,
                        CreatedAtUtc = diff.TimestampUtc == default ? stored.TimestampUtc : diff.TimestampUtc,
                    },
                    CancellationToken.None);

                if (ShouldPublishStructuredDelta(stored.RunId, "diff", DiffPublishThrottle))
                {
                    await publisher.PublishDiffUpdatedAsync(
                        stored.RunId,
                        diff.Sequence,
                        diff.Category,
                        diff.Payload,
                        diff.Schema,
                        diff.TimestampUtc,
                        CancellationToken.None);
                }
            }

            if (projectionDelta.ToolUpdated is not null &&
                ShouldPublishStructuredDelta(stored.RunId, "tool", ToolPublishThrottle))
            {
                var tool = projectionDelta.ToolUpdated;
                await publisher.PublishToolTimelineUpdatedAsync(
                    stored.RunId,
                    tool.Sequence,
                    tool.Category,
                    tool.ToolName,
                    tool.ToolCallId,
                    tool.State,
                    tool.Payload,
                    tool.Schema,
                    tool.TimestampUtc,
                    CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed handling structured event for run {RunId}", message.RunId);
        }
    }

    private bool TryCreateStructuredEventDocument(JobEventMessage message, DateTime timestampUtc, out RunStructuredEventDocument structuredEvent)
    {
        structuredEvent = new RunStructuredEventDocument();
        var category = message.Category?.Trim() ?? string.Empty;
        var payloadJson = message.PayloadJson?.Trim();
        var schema = message.SchemaVersion?.Trim() ?? string.Empty;

        if (message.Sequence <= 0 &&
            category.Length == 0 &&
            string.IsNullOrWhiteSpace(payloadJson) &&
            schema.Length == 0)
        {
            return false;
        }

        var sequence = ResolveStructuredSequence(message.RunId, message.Sequence, timestampUtc);
        var eventType = string.IsNullOrWhiteSpace(message.EventType)
            ? "structured"
            : message.EventType.Trim();
        if (category.Length == 0)
        {
            category = eventType;
        }

        structuredEvent = new RunStructuredEventDocument
        {
            RunId = message.RunId,
            Sequence = sequence,
            EventType = eventType,
            Category = category,
            Summary = message.Summary ?? string.Empty,
            Error = message.Error ?? string.Empty,
            PayloadJson = RunStructuredEventCodec.NormalizePayloadJson(payloadJson),
            SchemaVersion = schema,
            TimestampUtc = timestampUtc,
            CreatedAtUtc = timestampUtc,
        };

        return true;
    }

    private long ResolveStructuredSequence(string runId, long sequence, DateTime timestampUtc)
    {
        if (sequence > 0)
        {
            _structuredSequenceWatermarks.AddOrUpdate(
                runId,
                sequence,
                (_, existing) => Math.Max(existing, sequence));
            return sequence;
        }

        var seed = timestampUtc.Ticks;
        return _structuredSequenceWatermarks.AddOrUpdate(
            runId,
            seed,
            (_, existing) => Math.Max(existing + 1, seed));
    }

    private bool ShouldPublishStructuredDelta(string runId, string projectionType, TimeSpan throttleWindow)
    {
        var key = $"{projectionType}:{runId}";
        var now = DateTime.UtcNow;

        while (true)
        {
            if (!_structuredPublishWatermarks.TryGetValue(key, out var lastPublishedAtUtc))
            {
                if (_structuredPublishWatermarks.TryAdd(key, now))
                {
                    TrimStructuredPublishWatermarks(now);
                    return true;
                }

                continue;
            }

            if (now - lastPublishedAtUtc < throttleWindow)
            {
                return false;
            }

            if (_structuredPublishWatermarks.TryUpdate(key, now, lastPublishedAtUtc))
            {
                TrimStructuredPublishWatermarks(now);
                return true;
            }
        }
    }

    private void TrimStructuredPublishWatermarks(DateTime now)
    {
        if (_structuredPublishWatermarks.Count < 2000)
        {
            return;
        }

        var cutoff = now - TimeSpan.FromMinutes(15);
        foreach (var watermark in _structuredPublishWatermarks)
        {
            if (watermark.Value < cutoff)
            {
                _structuredPublishWatermarks.TryRemove(watermark.Key, out _);
            }
        }
    }

    private void ClearStructuredRunCaches(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return;
        }

        _structuredSequenceWatermarks.TryRemove(runId, out _);
        _structuredPublishWatermarks.TryRemove($"diff:{runId}", out _);
        _structuredPublishWatermarks.TryRemove($"tool:{runId}", out _);
    }

    private async Task HandleTaskRuntimeStatusAsync(string runtimeId, TaskRuntimeStatusMessage statusMessage)
    {
        try
        {
            var resolvedRuntimeId = string.IsNullOrWhiteSpace(runtimeId)
                ? statusMessage.TaskRuntimeId
                : runtimeId;
            logger.LogDebug("Worker {TaskRuntimeId} status: {Status}, ActiveSlots: {ActiveSlots}/{MaxSlots}",
                resolvedRuntimeId, statusMessage.Status, statusMessage.ActiveSlots, statusMessage.MaxSlots);

            var endpoint = await ResolveTaskRuntimeEndpointAsync(resolvedRuntimeId);
            workerRegistry.RecordHeartbeat(
                resolvedRuntimeId,
                endpoint ?? resolvedRuntimeId,
                statusMessage.ActiveSlots,
                statusMessage.MaxSlots);

            await lifecycleManager.ReportTaskRuntimeHeartbeatAsync(
                resolvedRuntimeId,
                statusMessage.ActiveSlots,
                statusMessage.MaxSlots,
                CancellationToken.None);

            await runtimeStore.UpsertTaskRuntimeRegistrationHeartbeatAsync(
                resolvedRuntimeId,
                endpoint ?? resolvedRuntimeId,
                statusMessage.ActiveSlots,
                statusMessage.MaxSlots,
                CancellationToken.None);

            await publisher.PublishTaskRuntimeHeartbeatAsync(
                resolvedRuntimeId,
                endpoint ?? resolvedRuntimeId,
                statusMessage.ActiveSlots,
                statusMessage.MaxSlots,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to handle worker status for worker {TaskRuntimeId}", statusMessage.TaskRuntimeId);
        }
    }

    private async Task ReplayConnectedRuntimeBacklogsAsync(CancellationToken cancellationToken)
    {
        var connections = _connections.Values.ToList();
        foreach (var connection in connections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await TryReplayRuntimeBacklogAsync(connection.TaskRuntimeId, connection.Endpoint, cancellationToken);
        }
    }

    private async Task TryReplayRuntimeBacklogAsync(string runtimeId, string endpoint, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runtimeId) ||
            string.IsNullOrWhiteSpace(endpoint))
        {
            return;
        }

        if (!_backlogReplayInFlight.TryAdd(runtimeId, 0))
        {
            return;
        }

        try
        {
            var cursor = await GetRuntimeEventCheckpointAsync(runtimeId, cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = clientFactory.CreateTaskRuntimeService(runtimeId, endpoint);
                var backlog = await client.WithCancellationToken(cancellationToken).ReadEventBacklogAsync(new ReadEventBacklogRequest
                {
                    AfterDeliveryId = cursor,
                    MaxEvents = 500
                });

                if (!backlog.Success)
                {
                    if (!string.IsNullOrWhiteSpace(backlog.ErrorMessage))
                    {
                        logger.LogDebug(
                            "Runtime backlog replay failed for {TaskRuntimeId}: {ErrorMessage}",
                            runtimeId,
                            backlog.ErrorMessage);
                    }

                    break;
                }

                if (backlog.Events.Count == 0)
                {
                    break;
                }

                foreach (var replayEvent in backlog.Events.OrderBy(x => x.DeliveryId))
                {
                    await HandleJobEventAsync(runtimeId, replayEvent);
                    cursor = Math.Max(cursor, replayEvent.DeliveryId);
                }

                if (!backlog.HasMore)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Runtime backlog replay cycle failed for {TaskRuntimeId}", runtimeId);
        }
        finally
        {
            _backlogReplayInFlight.TryRemove(runtimeId, out _);
        }
    }

    private async Task<bool> ShouldProcessDeliveryAsync(string runtimeId, long deliveryId)
    {
        if (deliveryId <= 0 || string.IsNullOrWhiteSpace(runtimeId))
        {
            return true;
        }

        if (!_runtimeEventCheckpoints.TryGetValue(runtimeId, out var knownCheckpoint))
        {
            knownCheckpoint = await GetRuntimeEventCheckpointAsync(runtimeId, CancellationToken.None);
        }

        return deliveryId > knownCheckpoint;
    }

    private async Task<long> GetRuntimeEventCheckpointAsync(string runtimeId, CancellationToken cancellationToken)
    {
        if (_runtimeEventCheckpoints.TryGetValue(runtimeId, out var cached))
        {
            return cached;
        }

        var stored = await runtimeStore.GetTaskRuntimeEventCheckpointAsync(runtimeId, cancellationToken);
        var checkpoint = stored?.LastDeliveryId ?? 0;
        _runtimeEventCheckpoints[runtimeId] = checkpoint;
        return checkpoint;
    }

    private async Task AdvanceRuntimeCheckpointAsync(string runtimeId, long deliveryId)
    {
        if (deliveryId <= 0 || string.IsNullOrWhiteSpace(runtimeId))
        {
            return;
        }

        _runtimeEventCheckpoints.AddOrUpdate(
            runtimeId,
            deliveryId,
            (_, existing) => Math.Max(existing, deliveryId));

        await runtimeStore.UpsertTaskRuntimeEventCheckpointAsync(runtimeId, deliveryId, CancellationToken.None);
    }

    private async Task<string?> ResolveTaskRuntimeEndpointAsync(string runtimeId)
    {
        if (_connections.TryGetValue(runtimeId, out var connection))
        {
            return connection.Endpoint;
        }

        var runtime = await lifecycleManager.GetTaskRuntimeAsync(runtimeId, CancellationToken.None);
        return runtime?.GrpcEndpoint;
    }

    private async Task TryRetryAsync(RunDocument failedRun)
    {
        var task = await taskStore.GetTaskAsync(failedRun.TaskId, CancellationToken.None);
        if (task is null)
            return;

        var maxAttempts = task.RetryPolicy.MaxAttempts;
        if (maxAttempts <= 1 || failedRun.Attempt >= maxAttempts)
            return;

        var repo = await repositoryStore.GetRepositoryAsync(task.RepositoryId, CancellationToken.None);
        if (repo is null)
            return;

        var nextAttempt = failedRun.Attempt + 1;
        var delaySeconds = task.RetryPolicy.BackoffBaseSeconds * Math.Pow(task.RetryPolicy.BackoffMultiplier, failedRun.Attempt - 1);

        logger.LogInformation("Scheduling retry {Attempt}/{Max} for run {RunId} in {Delay}s",
            nextAttempt, maxAttempts, failedRun.Id, delaySeconds);

        await Task.Delay(TimeSpan.FromSeconds(Math.Min(delaySeconds, 300)));

        var retryRun = await runStore.CreateRunAsync(
            task,
            CancellationToken.None,
            nextAttempt,
            executionModeOverride: failedRun.ExecutionMode,
            sessionProfileId: failedRun.SessionProfileId,
            mcpConfigSnapshotJson: failedRun.McpConfigSnapshotJson);
        await dispatcher.DispatchAsync(repo, task, retryRun, CancellationToken.None);
    }

    private async Task TryDispatchNextQueuedRunAsync(string taskId)
    {
        if (string.IsNullOrWhiteSpace(taskId))
        {
            return;
        }

        try
        {
            var dispatched = await dispatcher.DispatchNextQueuedRunForTaskAsync(taskId, CancellationToken.None);
            if (dispatched)
            {
                logger.LogInformation("Dispatched next queued run for task {TaskId}", taskId);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed dispatching next queued run for task {TaskId}", taskId);
        }
    }

    private static string? ResolveFailureClassFromCompletion(HarnessResultEnvelope envelope, bool succeeded)
    {
        if (succeeded)
        {
            return null;
        }

        if (envelope.Metadata.TryGetValue("failureClass", out var metadataFailureClass) &&
            !string.IsNullOrWhiteSpace(metadataFailureClass))
        {
            return metadataFailureClass;
        }

        if (!string.IsNullOrWhiteSpace(envelope.Summary) &&
            envelope.Summary.Contains("Workspace preparation failed", StringComparison.OrdinalIgnoreCase))
        {
            return "WorkspacePreparation";
        }

        if (string.IsNullOrWhiteSpace(envelope.Error))
        {
            return null;
        }

        if (envelope.Error.Contains("Envelope validation", StringComparison.OrdinalIgnoreCase))
            return "EnvelopeValidation";

        if (envelope.Error.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
            envelope.Error.Contains("cancelled", StringComparison.OrdinalIgnoreCase))
            return "Timeout";

        return null;
    }

    private static HarnessResultEnvelope ParseEnvelope(string? payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return new HarnessResultEnvelope
            {
                Status = "failed",
                Summary = "Worker completed without payload",
                Error = "Missing payload",
            };
        }

        return JsonSerializer.Deserialize<HarnessResultEnvelope>(payloadJson) ?? new HarnessResultEnvelope
        {
            Status = "failed",
            Summary = "Invalid payload",
            Error = "JSON parse failed",
        };
    }

    private static string ResolveGitSyncError(HarnessResultEnvelope envelope)
    {
        if (envelope.Metadata.TryGetValue("gitFailure", out var gitFailure))
        {
            return gitFailure?.Trim() ?? string.Empty;
        }

        if (envelope.Metadata.TryGetValue("gitWorkflow", out var gitWorkflow) &&
            string.Equals(gitWorkflow, "failed", StringComparison.OrdinalIgnoreCase))
        {
            return envelope.Error?.Trim() ?? string.Empty;
        }

        return string.Empty;
    }

    private static bool IsArtifactEvent(string? eventType)
    {
        return string.Equals(eventType, "artifact_manifest", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "artifact_chunk", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(eventType, "artifact_commit", StringComparison.OrdinalIgnoreCase);
    }

    private async Task HandleArtifactEventAsync(JobEventMessage message, CancellationToken cancellationToken)
    {
        var artifactId = message.ArtifactId?.Trim();
        if (string.IsNullOrWhiteSpace(artifactId))
        {
            artifactId = TryReadJsonString(message.PayloadJson, "artifactId");
        }

        if (string.IsNullOrWhiteSpace(artifactId))
        {
            return;
        }

        var artifactKey = BuildArtifactKey(message.RunId, artifactId);
        if (string.Equals(message.EventType, "artifact_manifest", StringComparison.OrdinalIgnoreCase))
        {
            HandleArtifactManifest(message, artifactKey, artifactId);
            return;
        }

        if (string.Equals(message.EventType, "artifact_chunk", StringComparison.OrdinalIgnoreCase))
        {
            await HandleArtifactChunkAsync(message, artifactKey, artifactId, cancellationToken);
            return;
        }

        if (string.Equals(message.EventType, "artifact_commit", StringComparison.OrdinalIgnoreCase))
        {
            await HandleArtifactCommitAsync(message, artifactKey, cancellationToken);
        }
    }

    private void HandleArtifactManifest(JobEventMessage message, string artifactKey, string artifactId)
    {
        var payloadFileName = TryReadJsonString(message.PayloadJson, "fileName");
        var fileName = NormalizeArtifactFileName(
            payloadFileName ?? message.Summary,
            artifactId);
        var contentType = message.ContentType?.Trim() ??
            TryReadJsonString(message.PayloadJson, "contentType") ??
            "application/octet-stream";
        var declaredSizeBytes = TryReadJsonLong(message.PayloadJson, "sizeBytes");
        var assembly = new ArtifactAssemblyState(
            message.RunId,
            artifactId,
            fileName,
            contentType,
            declaredSizeBytes);

        _artifactAssemblies.AddOrUpdate(
            artifactKey,
            _ => assembly,
            (_, existing) =>
            {
                existing.Dispose();
                return assembly;
            });
    }

    private async Task HandleArtifactChunkAsync(
        JobEventMessage message,
        string artifactKey,
        string artifactId,
        CancellationToken cancellationToken)
    {
        var chunk = message.BinaryPayload;
        if (chunk is null || chunk.Length == 0)
        {
            return;
        }

        var assembly = _artifactAssemblies.GetOrAdd(
            artifactKey,
            _ => new ArtifactAssemblyState(
                message.RunId,
                artifactId,
                NormalizeArtifactFileName(message.Summary, artifactId),
                message.ContentType?.Trim() ?? "application/octet-stream",
                declaredSizeBytes: 0));

        var shouldPersist = false;
        lock (assembly.SyncRoot)
        {
            if (assembly.Rejected || assembly.Persisted)
            {
                return;
            }

            var nextArtifactSize = assembly.ReceivedBytes + chunk.Length;
            if (nextArtifactSize > MaxArtifactBytesPerArtifact)
            {
                assembly.Rejected = true;
                logger.LogWarning("Rejecting artifact {ArtifactId} for run {RunId}: artifact size cap exceeded", assembly.ArtifactId, assembly.RunId);
                return;
            }

            var runTotal = _artifactRunByteTotals.AddOrUpdate(
                message.RunId,
                chunk.Length,
                (_, existing) => existing + chunk.Length);
            if (runTotal > MaxArtifactBytesPerRun)
            {
                assembly.Rejected = true;
                logger.LogWarning("Rejecting artifact {ArtifactId} for run {RunId}: run artifact size cap exceeded", assembly.ArtifactId, assembly.RunId);
                return;
            }

            assembly.Buffer.Write(chunk, 0, chunk.Length);
            assembly.ReceivedBytes = nextArtifactSize;
            if (message.IsLastChunk == true)
            {
                shouldPersist = true;
            }
        }

        if (shouldPersist)
        {
            await PersistArtifactAssemblyAsync(artifactKey, cancellationToken);
        }
    }

    private async Task HandleArtifactCommitAsync(JobEventMessage message, string artifactKey, CancellationToken cancellationToken)
    {
        if (_artifactAssemblies.TryGetValue(artifactKey, out var assembly))
        {
            var sha256 = TryReadJsonString(message.PayloadJson, "sha256");
            if (!string.IsNullOrWhiteSpace(sha256))
            {
                lock (assembly.SyncRoot)
                {
                    assembly.Sha256 = sha256.Trim().ToLowerInvariant();
                }
            }
        }

        await PersistArtifactAssemblyAsync(artifactKey, cancellationToken);
    }

    private async Task PersistArtifactAssemblyAsync(string artifactKey, CancellationToken cancellationToken)
    {
        if (!_artifactAssemblies.TryRemove(artifactKey, out var assembly))
        {
            return;
        }

        try
        {
            lock (assembly.SyncRoot)
            {
                if (assembly.Rejected || assembly.Persisted || assembly.Buffer.Length == 0)
                {
                    return;
                }

                assembly.Buffer.Position = 0;
                assembly.Persisted = true;
            }

            await runStore.SaveArtifactAsync(assembly.RunId, assembly.FileName, assembly.Buffer, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist streamed artifact {ArtifactId} for run {RunId}", assembly.ArtifactId, assembly.RunId);
        }
        finally
        {
            assembly.Dispose();
        }
    }

    private async Task FinalizePendingArtifactsAsync(string runId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return;
        }

        var prefix = $"{runId.Trim()}:";
        var pendingKeys = _artifactAssemblies.Keys
            .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var pendingKey in pendingKeys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await PersistArtifactAssemblyAsync(pendingKey, cancellationToken);
        }
    }

    private void ClearArtifactCaches(string runId)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            return;
        }

        _artifactRunByteTotals.TryRemove(runId, out _);
        var prefix = $"{runId.Trim()}:";
        var staleKeys = _artifactAssemblies.Keys
            .Where(key => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var staleKey in staleKeys)
        {
            if (_artifactAssemblies.TryRemove(staleKey, out var staleAssembly))
            {
                staleAssembly.Dispose();
            }
        }
    }

    private static string BuildArtifactKey(string runId, string artifactId)
    {
        return $"{runId.Trim()}:{artifactId.Trim()}";
    }

    private static string NormalizeArtifactFileName(string? fileName, string artifactId)
    {
        var normalized = Path.GetFileName(fileName?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            normalized = $"artifact-{artifactId}.bin";
        }

        return normalized;
    }

    private static string? TryReadJsonString(string? payloadJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(propertyName, out var property))
            {
                return null;
            }

            return property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : property.ToString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static long TryReadJsonLong(string? payloadJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return 0;
        }

        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty(propertyName, out var property))
            {
                return 0;
            }

            if (property.ValueKind == JsonValueKind.Number &&
                property.TryGetInt64(out var number))
            {
                return number;
            }

            if (property.ValueKind == JsonValueKind.String &&
                long.TryParse(property.GetString(), out var parsed))
            {
                return parsed;
            }

            return 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        var knownTaskRuntimeIds = _connections.Keys.ToList();
        foreach (var runtimeId in knownTaskRuntimeIds)
        {
            await RemoveConnectionAsync(runtimeId);
        }

        foreach (var key in _artifactAssemblies.Keys.ToList())
        {
            if (_artifactAssemblies.TryRemove(key, out var assembly))
            {
                assembly.Dispose();
            }
        }

        _artifactRunByteTotals.Clear();

        await base.StopAsync(cancellationToken);
    }

    private sealed class ArtifactAssemblyState(
        string runId,
        string artifactId,
        string fileName,
        string contentType,
        long declaredSizeBytes) : IDisposable
    {
        public object SyncRoot { get; } = new();
        public string RunId { get; } = runId;
        public string ArtifactId { get; } = artifactId;
        public string FileName { get; } = fileName;
        public string ContentType { get; } = contentType;
        public long DeclaredSizeBytes { get; } = declaredSizeBytes;
        public MemoryStream Buffer { get; } = declaredSizeBytes > 0 && declaredSizeBytes < int.MaxValue
            ? new MemoryStream((int)declaredSizeBytes)
            : new MemoryStream();
        public long ReceivedBytes { get; set; }
        public string? Sha256 { get; set; }
        public bool Rejected { get; set; }
        public bool Persisted { get; set; }

        public void Dispose()
        {
            Buffer.Dispose();
        }
    }

    private sealed class NullRunStructuredViewService : IRunStructuredViewService
    {
        public static readonly NullRunStructuredViewService Instance = new();

        public Task<RunStructuredProjectionDelta> ApplyStructuredEventAsync(RunStructuredEventDocument structuredEvent, CancellationToken cancellationToken)
        {
            var snapshot = new RunStructuredViewSnapshot(
                structuredEvent.RunId,
                structuredEvent.Sequence,
                [],
                [],
                [],
                null,
                structuredEvent.CreatedAtUtc == default ? DateTime.UtcNow : structuredEvent.CreatedAtUtc);
            return Task.FromResult(new RunStructuredProjectionDelta(snapshot, null, null));
        }

        public Task<RunStructuredViewSnapshot> GetViewAsync(string runId, CancellationToken cancellationToken)
        {
            return Task.FromResult(new RunStructuredViewSnapshot(runId, 0, [], [], [], null, DateTime.UtcNow));
        }
    }

    private sealed class TaskRuntimeHubConnection
    {
        public required string TaskRuntimeId { get; init; }
        public required string Endpoint { get; set; }
        public required CancellationTokenSource Cancellation { get; init; }
        public Task? ConnectionTask { get; set; }
        private ITaskRuntimeEventHub? _hub;
        private readonly SemaphoreSlim _hubLock = new(1, 1);

        public static TaskRuntimeHubConnection Create(string runtimeId, string endpoint)
        {
            return new TaskRuntimeHubConnection
            {
                TaskRuntimeId = runtimeId,
                Endpoint = endpoint,
                Cancellation = new CancellationTokenSource(),
            };
        }

        public void SetHub(ITaskRuntimeEventHub hub)
        {
            _hub = hub;
        }

        public async Task DisposeHubAsync()
        {
            await _hubLock.WaitAsync();
            try
            {
                if (_hub is null)
                {
                    return;
                }

                await _hub.DisposeAsync();
                _hub = null;
            }
            catch
            {
            }
            finally
            {
                _hubLock.Release();
            }
        }
    }

    private sealed class RuntimeScopedEventReceiver(
        string runtimeId,
        TaskRuntimeEventListenerService service) : ITaskRuntimeEventReceiver
    {
        public void OnJobEvent(JobEventMessage eventMessage)
        {
            _ = service.HandleJobEventAsync(runtimeId, eventMessage);
        }

        public void OnTaskRuntimeStatusChanged(TaskRuntimeStatusMessage statusMessage)
        {
            _ = service.HandleTaskRuntimeStatusAsync(runtimeId, statusMessage);
        }
    }

    private sealed class EventHubProbeReceiver : ITaskRuntimeEventReceiver
    {
        public void OnJobEvent(JobEventMessage eventMessage)
        {
        }

        public void OnTaskRuntimeStatusChanged(TaskRuntimeStatusMessage statusMessage)
        {
        }
    }
}
