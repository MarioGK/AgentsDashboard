using System.Text.Json;
using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.WorkerGateway.Models;

namespace AgentsDashboard.WorkerGateway.Services;

public sealed class JobProcessorService(
    WorkerQueue queue,
    HarnessExecutor executor,
    WorkerEventBus eventBus,
    ILogger<JobProcessorService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var queuedJob in queue.ReadAllAsync(stoppingToken))
        {
            _ = ProcessOneAsync(queuedJob, stoppingToken);
        }
    }

    private async Task ProcessOneAsync(QueuedJob queuedJob, CancellationToken serviceToken)
    {
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(serviceToken, queuedJob.CancellationSource.Token);
        var cancellationToken = linkedCts.Token;

        try
        {
            await eventBus.PublishAsync(CreateEvent(queuedJob.Request.RunId, "log", "Job started", string.Empty), cancellationToken);

            async Task OnLogChunk(string chunk, CancellationToken ct)
            {
                var logEvent = CreateEvent(queuedJob.Request.RunId, "log_chunk", chunk, string.Empty);
                await eventBus.PublishAsync(logEvent, ct);
            }

            var envelope = await executor.ExecuteAsync(queuedJob, OnLogChunk, cancellationToken);
            var payload = JsonSerializer.Serialize(envelope);

            await eventBus.PublishAsync(
                CreateEvent(queuedJob.Request.RunId, "completed", envelope.Summary, payload),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await eventBus.PublishAsync(
                CreateEvent(queuedJob.Request.RunId, "completed", "Job cancelled", "{\"status\":\"failed\",\"summary\":\"Cancelled\",\"error\":\"Cancelled\"}"),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Job processing crashed for run {RunId}", queuedJob.Request.RunId);
            await eventBus.PublishAsync(
                CreateEvent(queuedJob.Request.RunId, "completed", "Job crashed", "{\"status\":\"failed\",\"summary\":\"Crash\",\"error\":\"Worker crashed\"}"),
                CancellationToken.None);
        }
        finally
        {
            queue.MarkCompleted(queuedJob.Request.RunId);
            linkedCts.Dispose();
            queuedJob.CancellationSource.Dispose();
        }
    }

    private static JobEventReply CreateEvent(string runId, string kind, string message, string payloadJson)
        => new()
        {
            RunId = runId,
            Kind = kind,
            Message = message,
            PayloadJson = payloadJson,
            TimestampUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
}
