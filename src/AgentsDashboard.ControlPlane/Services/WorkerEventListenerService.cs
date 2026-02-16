using System.Collections.Concurrent;
using System.Text.Json;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Proxy;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class WorkerEventListenerService(
    IMagicOnionClientFactory clientFactory,
    IWorkerLifecycleManager lifecycleManager,
    IOrchestratorStore store,
    IWorkerRegistryService workerRegistry,
    IRunEventPublisher publisher,
    InMemoryYarpConfigProvider yarpProvider,
    RunDispatcher dispatcher,
    ILogger<WorkerEventListenerService> logger) : BackgroundService, IWorkerEventReceiver
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan WorkerTtl = TimeSpan.FromMinutes(2);

    private readonly ConcurrentDictionary<string, WorkerHubConnection> _connections = new(StringComparer.OrdinalIgnoreCase);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncConnectionsAsync(stoppingToken);
                await store.MarkStaleWorkersOfflineAsync(WorkerTtl, stoppingToken);
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
        var workers = await lifecycleManager.ListWorkersAsync(cancellationToken);
        var runningWorkers = workers.Where(x => x.IsRunning).ToDictionary(x => x.WorkerId, StringComparer.OrdinalIgnoreCase);

        foreach (var worker in runningWorkers.Values)
        {
            if (_connections.TryGetValue(worker.WorkerId, out var existing))
            {
                if (string.Equals(existing.Endpoint, worker.GrpcEndpoint, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                await RemoveConnectionAsync(worker.WorkerId);
            }

            var connection = WorkerHubConnection.Create(worker.WorkerId, worker.GrpcEndpoint);
            if (_connections.TryAdd(worker.WorkerId, connection))
            {
                connection.ConnectionTask = Task.Run(() => RunConnectionLoopAsync(connection, connection.Cancellation.Token), CancellationToken.None);
            }
        }

        foreach (var knownWorkerId in _connections.Keys.ToList())
        {
            if (runningWorkers.ContainsKey(knownWorkerId))
            {
                continue;
            }

            await RemoveConnectionAsync(knownWorkerId);
        }
    }

    private async Task RunConnectionLoopAsync(WorkerHubConnection connection, CancellationToken cancellationToken)
    {
        var reconnectDelay = TimeSpan.FromSeconds(1);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                logger.LogInformation("Connecting worker event hub for {WorkerId} at {Endpoint}", connection.WorkerId, connection.Endpoint);
                var hub = await clientFactory.ConnectEventHubAsync(connection.WorkerId, connection.Endpoint, this, cancellationToken);
                connection.SetHub(hub);
                reconnectDelay = TimeSpan.FromSeconds(1);

                await hub.SubscribeAsync(runIds: null);
                await hub.WaitForDisconnect();

                logger.LogWarning("Worker event hub disconnected for {WorkerId}", connection.WorkerId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Worker event hub connection failed for {WorkerId}", connection.WorkerId);
                await Task.Delay(reconnectDelay, cancellationToken);
                reconnectDelay = TimeSpan.FromMilliseconds(Math.Min(reconnectDelay.TotalMilliseconds * 2, 30000));
            }
            finally
            {
                await connection.DisposeHubAsync();
            }
        }
    }

    private async Task RemoveConnectionAsync(string workerId)
    {
        if (!_connections.TryRemove(workerId, out var connection))
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
            clientFactory.RemoveWorker(workerId);
        }
    }

    void IWorkerEventReceiver.OnJobEvent(JobEventMessage eventMessage)
    {
        _ = HandleJobEventAsync(eventMessage);
    }

    void IWorkerEventReceiver.OnWorkerStatusChanged(WorkerStatusMessage statusMessage)
    {
        _ = HandleWorkerStatusAsync(statusMessage);
    }

    private async Task HandleJobEventAsync(JobEventMessage message)
    {
        try
        {
            var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(message.Timestamp).UtcDateTime;

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

            yarpProvider.RemoveRoute($"run-{message.RunId}");

            await publisher.PublishStatusAsync(completedRun, CancellationToken.None);

            if (!string.IsNullOrWhiteSpace(completedRun.WorkerId))
            {
                try
                {
                    await lifecycleManager.RecycleWorkerAsync(completedRun.WorkerId, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed recycling worker {WorkerId} after run {RunId} completion", completedRun.WorkerId, completedRun.Id);
                }
            }

            if (!succeeded)
            {
                await store.CreateFindingFromFailureAsync(completedRun, envelope.Error, CancellationToken.None);
                await TryRetryAsync(completedRun);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to handle job event for run {RunId}", message.RunId);
        }
    }

    private async Task HandleWorkerStatusAsync(WorkerStatusMessage statusMessage)
    {
        try
        {
            logger.LogDebug("Worker {WorkerId} status: {Status}, ActiveSlots: {ActiveSlots}/{MaxSlots}",
                statusMessage.WorkerId, statusMessage.Status, statusMessage.ActiveSlots, statusMessage.MaxSlots);

            var endpoint = await ResolveWorkerEndpointAsync(statusMessage.WorkerId);
            workerRegistry.RecordHeartbeat(
                statusMessage.WorkerId,
                endpoint ?? statusMessage.WorkerId,
                statusMessage.ActiveSlots,
                statusMessage.MaxSlots);

            await lifecycleManager.ReportWorkerHeartbeatAsync(
                statusMessage.WorkerId,
                statusMessage.ActiveSlots,
                statusMessage.MaxSlots,
                CancellationToken.None);

            await store.UpsertWorkerHeartbeatAsync(
                statusMessage.WorkerId,
                endpoint ?? statusMessage.WorkerId,
                statusMessage.ActiveSlots,
                statusMessage.MaxSlots,
                CancellationToken.None);

            await publisher.PublishWorkerHeartbeatAsync(
                statusMessage.WorkerId,
                endpoint ?? statusMessage.WorkerId,
                statusMessage.ActiveSlots,
                statusMessage.MaxSlots,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to handle worker status for worker {WorkerId}", statusMessage.WorkerId);
        }
    }

    private async Task<string?> ResolveWorkerEndpointAsync(string workerId)
    {
        if (_connections.TryGetValue(workerId, out var connection))
        {
            return connection.Endpoint;
        }

        var worker = await lifecycleManager.GetWorkerAsync(workerId, CancellationToken.None);
        return worker?.GrpcEndpoint;
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

        var project = await store.GetProjectAsync(repo.ProjectId, CancellationToken.None);
        if (project is null)
            return;

        var nextAttempt = failedRun.Attempt + 1;
        var delaySeconds = task.RetryPolicy.BackoffBaseSeconds * Math.Pow(task.RetryPolicy.BackoffMultiplier, failedRun.Attempt - 1);

        logger.LogInformation("Scheduling retry {Attempt}/{Max} for run {RunId} in {Delay}s",
            nextAttempt, maxAttempts, failedRun.Id, delaySeconds);

        await Task.Delay(TimeSpan.FromSeconds(Math.Min(delaySeconds, 300)));

        var retryRun = await store.CreateRunAsync(task, project.Id, CancellationToken.None, nextAttempt);
        await dispatcher.DispatchAsync(project, repo, task, retryRun, CancellationToken.None);
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

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        var knownWorkerIds = _connections.Keys.ToList();
        foreach (var workerId in knownWorkerIds)
        {
            await RemoveConnectionAsync(workerId);
        }

        await base.StopAsync(cancellationToken);
    }

    private sealed class WorkerHubConnection
    {
        public required string WorkerId { get; init; }
        public required string Endpoint { get; init; }
        public required CancellationTokenSource Cancellation { get; init; }
        public Task? ConnectionTask { get; set; }
        private IWorkerEventHub? _hub;
        private readonly SemaphoreSlim _hubLock = new(1, 1);

        public static WorkerHubConnection Create(string workerId, string endpoint)
        {
            return new WorkerHubConnection
            {
                WorkerId = workerId,
                Endpoint = endpoint,
                Cancellation = new CancellationTokenSource(),
            };
        }

        public void SetHub(IWorkerEventHub hub)
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
