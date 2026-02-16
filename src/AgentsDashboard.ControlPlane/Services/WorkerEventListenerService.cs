using System.Text.Json;
using System.Threading.Tasks;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Proxy;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class WorkerEventListenerService(
    IMagicOnionClientFactory clientFactory,
    IOrchestratorStore store,
    IWorkerRegistryService workerRegistry,
    IRunEventPublisher publisher,
    InMemoryYarpConfigProvider yarpProvider,
    RunDispatcher dispatcher,
    ILogger<WorkerEventListenerService> logger) : BackgroundService, IWorkerEventReceiver
{
    private IWorkerEventHub? _eventHub;
    private readonly object _hubLock = new();
    private int _reconnectDelayMs = 1000;
    private const int MaxReconnectDelayMs = 30000;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndListenAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Worker event hub connection failed. Reconnecting in {Delay}ms.", _reconnectDelayMs);
                await Task.Delay(_reconnectDelayMs, stoppingToken);

                _reconnectDelayMs = Math.Min(_reconnectDelayMs * 2, MaxReconnectDelayMs);
            }
        }
    }

    private async Task ConnectAndListenAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Connecting to WorkerEventHub...");

        var hub = await clientFactory.ConnectEventHubAsync(this, cancellationToken);

        lock (_hubLock)
        {
            _eventHub = hub;
        }

        _reconnectDelayMs = 1000;
        logger.LogInformation("Connected to WorkerEventHub. Subscribing to all events...");

        await hub.SubscribeAsync(runIds: null);

        await hub.WaitForDisconnect();
        logger.LogWarning("WorkerEventHub disconnected.");
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

            workerRegistry.RecordHeartbeat(
                statusMessage.WorkerId,
                statusMessage.WorkerId,
                statusMessage.ActiveSlots,
                statusMessage.MaxSlots);

            await publisher.PublishWorkerHeartbeatAsync(
                statusMessage.WorkerId,
                statusMessage.WorkerId,
                statusMessage.ActiveSlots,
                statusMessage.MaxSlots,
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to handle worker status for worker {WorkerId}", statusMessage.WorkerId);
        }
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

    public override void Dispose()
    {
        IWorkerEventHub? hubToDispose = null;
        lock (_hubLock)
        {
            hubToDispose = _eventHub;
            _eventHub = null;
        }

        if (hubToDispose is not null)
        {
            try
            {
                hubToDispose.DisposeAsync().Wait(TimeSpan.FromSeconds(5));
            }
            catch
            {
                // Ignore disposal errors
            }
        }
        base.Dispose();
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        IWorkerEventHub? hubToDispose = null;
        lock (_hubLock)
        {
            hubToDispose = _eventHub;
            _eventHub = null;
        }

        if (hubToDispose is not null)
        {
            try
            {
                await hubToDispose.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // Ignore disposal errors during shutdown
            }
        }
        await base.StopAsync(cancellationToken);
    }
}
