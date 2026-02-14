using System.Text.Json;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.ControlPlane.Data;
using AgentsDashboard.ControlPlane.Proxy;
using Grpc.Core;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class WorkerEventListenerService(
    WorkerGateway.WorkerGatewayClient workerClient,
    OrchestratorStore store,
    IRunEventPublisher publisher,
    InMemoryYarpConfigProvider yarpProvider,
    RunDispatcher dispatcher,
    ILogger<WorkerEventListenerService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ListenOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled && stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Worker event stream failed. Reconnecting in 2 seconds.");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    private async Task ListenOnceAsync(CancellationToken cancellationToken)
    {
        using var call = workerClient.SubscribeEvents(new SubscribeEventsRequest(), cancellationToken: cancellationToken);

        while (await call.ResponseStream.MoveNext(cancellationToken))
        {
            var message = call.ResponseStream.Current;

            if (string.Equals(message.Kind, "log_chunk", StringComparison.OrdinalIgnoreCase))
            {
                var logChunkEvent = new RunLogEvent
                {
                    RunId = message.RunId,
                    Level = "chunk",
                    Message = message.Message,
                    TimestampUtc = DateTimeOffset.FromUnixTimeMilliseconds(message.TimestampUnixMs).UtcDateTime,
                };
                await publisher.PublishLogAsync(logChunkEvent, cancellationToken);
                continue;
            }

            var logEvent = new RunLogEvent
            {
                RunId = message.RunId,
                Level = message.Kind,
                Message = message.Message,
                TimestampUtc = DateTimeOffset.FromUnixTimeMilliseconds(message.TimestampUnixMs).UtcDateTime,
            };

            await store.AddRunLogAsync(logEvent, cancellationToken);
            await publisher.PublishLogAsync(logEvent, cancellationToken);

            if (!string.Equals(message.Kind, "completed", StringComparison.OrdinalIgnoreCase))
                continue;

            var envelope = ParseEnvelope(message.PayloadJson);
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
                message.PayloadJson,
                cancellationToken,
                failureClass: failureClass,
                prUrl: prUrl);

            if (completedRun is null)
                continue;

            // Cleanup YARP routes for this run
            yarpProvider.RemoveRoute($"run-{message.RunId}");

            await publisher.PublishStatusAsync(completedRun, cancellationToken);

            if (!succeeded)
            {
                await store.CreateFindingFromFailureAsync(completedRun, envelope.Error, cancellationToken);
                await TryRetryAsync(completedRun, cancellationToken);
            }
        }
    }

    private async Task TryRetryAsync(RunDocument failedRun, CancellationToken cancellationToken)
    {
        var task = await store.GetTaskAsync(failedRun.TaskId, cancellationToken);
        if (task is null) return;

        var maxAttempts = task.RetryPolicy.MaxAttempts;
        if (maxAttempts <= 1 || failedRun.Attempt >= maxAttempts)
            return;

        var repo = await store.GetRepositoryAsync(task.RepositoryId, cancellationToken);
        if (repo is null) return;

        var project = await store.GetProjectAsync(repo.ProjectId, cancellationToken);
        if (project is null) return;

        var nextAttempt = failedRun.Attempt + 1;
        var delaySeconds = task.RetryPolicy.BackoffBaseSeconds * Math.Pow(task.RetryPolicy.BackoffMultiplier, failedRun.Attempt - 1);

        logger.LogInformation("Scheduling retry {Attempt}/{Max} for run {RunId} in {Delay}s",
            nextAttempt, maxAttempts, failedRun.Id, delaySeconds);

        await Task.Delay(TimeSpan.FromSeconds(Math.Min(delaySeconds, 300)), cancellationToken);

        var retryRun = await store.CreateRunAsync(task, project.Id, cancellationToken, nextAttempt);
        await dispatcher.DispatchAsync(project, repo, task, retryRun, cancellationToken);
    }

    private static HarnessResultEnvelope ParseEnvelope(string payloadJson)
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
}
