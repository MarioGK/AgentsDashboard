using System.Collections.Concurrent;
using System.Text.Json;




namespace AgentsDashboard.ControlPlane.Features.RuntimeOrchestration.Services;

public sealed class TaskRuntimeEventListenerService(
    IMagicOnionClientFactory clientFactory,
    ITaskRuntimeLifecycleManager lifecycleManager,
    IOrchestratorStore store,
    ITaskSemanticEmbeddingService taskSemanticEmbeddingService,
    ITaskRuntimeRegistryService workerRegistry,
    IRunEventPublisher publisher,
    RunDispatcher dispatcher,
    ILogger<TaskRuntimeEventListenerService> logger,
    IRunStructuredViewService? runStructuredViewService = null) : BackgroundService, ITaskRuntimeEventReceiver
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan TaskRuntimeTtl = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DiffPublishThrottle = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan ToolPublishThrottle = TimeSpan.FromMilliseconds(125);
    private const long MaxArtifactBytesPerArtifact = 104_857_600;
    private const long MaxArtifactBytesPerRun = 262_144_000;

    private readonly ConcurrentDictionary<string, TaskRuntimeHubConnection> _connections = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _structuredPublishWatermarks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _structuredSequenceWatermarks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ArtifactAssemblyState> _artifactAssemblies = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _artifactRunByteTotals = new(StringComparer.OrdinalIgnoreCase);
    private readonly IRunStructuredViewService _runStructuredViewService = runStructuredViewService ?? NullRunStructuredViewService.Instance;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncConnectionsAsync(stoppingToken);
                await store.MarkStaleTaskRuntimeRegistrationsOfflineAsync(TaskRuntimeTtl, stoppingToken);
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
                logger.LogInformation("Connecting worker event hub for {TaskRuntimeId} at {Endpoint}", connection.TaskRuntimeId, connection.Endpoint);
                var hub = await clientFactory.ConnectEventHubAsync(connection.TaskRuntimeId, connection.Endpoint, this, cancellationToken);
                connection.SetHub(hub);
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
                logger.LogWarning(
                    "Worker event hub connection failed for {TaskRuntimeId}: {ErrorMessage}",
                    connection.TaskRuntimeId,
                    ex.Message.ReplaceLineEndings(" "));
                logger.LogDebug(ex, "Worker event hub connection failure details for {TaskRuntimeId}", connection.TaskRuntimeId);
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
        _ = HandleJobEventAsync(eventMessage);
    }

    void ITaskRuntimeEventReceiver.OnTaskRuntimeStatusChanged(TaskRuntimeStatusMessage statusMessage)
    {
        _ = HandleTaskRuntimeStatusAsync(statusMessage);
    }

    private async Task HandleJobEventAsync(JobEventMessage message)
    {
        try
        {
            if (IsArtifactEvent(message.EventType))
            {
                await HandleArtifactEventAsync(message, CancellationToken.None);
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
                return;
            }

            var logEvent = new RunLogEvent
            {
                RunId = message.RunId,
                Level = message.EventType,
                Message = message.Summary ?? message.Error ?? string.Empty,
                TimestampUtc = timestamp,
            };

            await store.AddRunLogAsync(logEvent, CancellationToken.None);
            await publisher.PublishLogAsync(logEvent, CancellationToken.None);

            if (!string.Equals(message.EventType, "completed", StringComparison.OrdinalIgnoreCase))
                return;

            var payloadJson = message.Metadata?.GetValueOrDefault("payload");
            var envelope = ParseEnvelope(payloadJson);
            var succeeded = string.Equals(envelope.Status, "succeeded", StringComparison.OrdinalIgnoreCase);
            var isObsoleteDisposition =
                (envelope.Metadata.TryGetValue("runDisposition", out var runDisposition) &&
                 string.Equals(runDisposition, "obsolete", StringComparison.OrdinalIgnoreCase)) ||
                (message.Metadata?.TryGetValue("runDisposition", out var messageDisposition) == true &&
                 string.Equals(messageDisposition, "obsolete", StringComparison.OrdinalIgnoreCase));

            string? prUrl = envelope.Metadata.TryGetValue("prUrl", out var url) ? url : null;

            string? failureClass = null;
            if (!succeeded && !string.IsNullOrWhiteSpace(envelope.Error))
            {
                if (envelope.Error.Contains("Envelope validation", StringComparison.OrdinalIgnoreCase))
                    failureClass = "EnvelopeValidation";
                else if (envelope.Error.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                         envelope.Error.Contains("cancelled", StringComparison.OrdinalIgnoreCase))
                    failureClass = "Timeout";
            }

            var completedRun = await store.MarkRunCompletedAsync(
                message.RunId,
                succeeded,
                envelope.Summary,
                payloadJson ?? string.Empty,
                CancellationToken.None,
                failureClass: failureClass,
                prUrl: prUrl);

            if (completedRun is null)
                return;

            if (isObsoleteDisposition)
            {
                var obsoleteRun = await store.MarkRunObsoleteAsync(message.RunId, CancellationToken.None);
                if (obsoleteRun is not null)
                {
                    completedRun = obsoleteRun;
                }
            }

            await FinalizePendingArtifactsAsync(completedRun.Id, CancellationToken.None);

            var gitSyncError = ResolveGitSyncError(envelope);
            try
            {
                await store.UpdateTaskGitMetadataAsync(
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
            var stored = await store.AppendRunStructuredEventAsync(structuredEvent, CancellationToken.None);
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
                await store.UpsertRunDiffSnapshotAsync(
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

    private async Task HandleTaskRuntimeStatusAsync(TaskRuntimeStatusMessage statusMessage)
    {
        try
        {
            logger.LogDebug("Worker {TaskRuntimeId} status: {Status}, ActiveSlots: {ActiveSlots}/{MaxSlots}",
                statusMessage.TaskRuntimeId, statusMessage.Status, statusMessage.ActiveSlots, statusMessage.MaxSlots);

            var endpoint = await ResolveTaskRuntimeEndpointAsync(statusMessage.TaskRuntimeId);
            workerRegistry.RecordHeartbeat(
                statusMessage.TaskRuntimeId,
                endpoint ?? statusMessage.TaskRuntimeId,
                statusMessage.ActiveSlots,
                statusMessage.MaxSlots);

            await lifecycleManager.ReportTaskRuntimeHeartbeatAsync(
                statusMessage.TaskRuntimeId,
                statusMessage.ActiveSlots,
                statusMessage.MaxSlots,
                CancellationToken.None);

            await store.UpsertTaskRuntimeRegistrationHeartbeatAsync(
                statusMessage.TaskRuntimeId,
                endpoint ?? statusMessage.TaskRuntimeId,
                statusMessage.ActiveSlots,
                statusMessage.MaxSlots,
                CancellationToken.None);

            await publisher.PublishTaskRuntimeHeartbeatAsync(
                statusMessage.TaskRuntimeId,
                endpoint ?? statusMessage.TaskRuntimeId,
                statusMessage.ActiveSlots,
                statusMessage.MaxSlots,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to handle worker status for worker {TaskRuntimeId}", statusMessage.TaskRuntimeId);
        }
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
        var task = await store.GetTaskAsync(failedRun.TaskId, CancellationToken.None);
        if (task is null)
            return;

        var maxAttempts = task.RetryPolicy.MaxAttempts;
        if (maxAttempts <= 1 || failedRun.Attempt >= maxAttempts)
            return;

        var repo = await store.GetRepositoryAsync(task.RepositoryId, CancellationToken.None);
        if (repo is null)
            return;

        var nextAttempt = failedRun.Attempt + 1;
        var delaySeconds = task.RetryPolicy.BackoffBaseSeconds * Math.Pow(task.RetryPolicy.BackoffMultiplier, failedRun.Attempt - 1);

        logger.LogInformation("Scheduling retry {Attempt}/{Max} for run {RunId} in {Delay}s",
            nextAttempt, maxAttempts, failedRun.Id, delaySeconds);

        await Task.Delay(TimeSpan.FromSeconds(Math.Min(delaySeconds, 300)));

        var retryRun = await store.CreateRunAsync(
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

            await store.SaveArtifactAsync(assembly.RunId, assembly.FileName, assembly.Buffer, cancellationToken);
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
        public required string Endpoint { get; init; }
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
}
