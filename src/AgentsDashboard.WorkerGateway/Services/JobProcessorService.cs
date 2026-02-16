using System.Text.Json;
using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.WorkerGateway.Models;

namespace AgentsDashboard.WorkerGateway.Services;

public class JobProcessorService(
    IWorkerQueue queue,
    IHarnessExecutor executor,
    WorkerEventBus eventBus,
    ILogger<JobProcessorService> logger) : BackgroundService, IJobProcessorService
{
    private readonly List<Task> _runningJobs = [];
    private readonly object _lock = new();
    private readonly TimeSpan _shutdownTimeout = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var queuedJob in queue.ReadAllAsync(stoppingToken))
        {
            var jobTask = ProcessOneAsync(queuedJob, stoppingToken);
            lock (_lock)
            {
                _runningJobs.Add(jobTask);
            }

            _ = jobTask.ContinueWith(_ =>
            {
                lock (_lock)
                {
                    _runningJobs.Remove(jobTask);
                }
            }, TaskScheduler.Default);
        }

        try
        {
            List<Task> jobsToWait;
            lock (_lock)
            {
                jobsToWait = _runningJobs.ToList();
            }

            if (jobsToWait.Count > 0)
            {
                logger.LogInformation("Waiting for {Count} running jobs to complete (timeout: {Timeout}s)...",
                    jobsToWait.Count, _shutdownTimeout.TotalSeconds);

                var timeoutTask = Task.Delay(_shutdownTimeout, CancellationToken.None);
                var allJobsTask = Task.WhenAll(jobsToWait);
                var completedTask = await Task.WhenAny(allJobsTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    logger.LogWarning("Shutdown timeout reached, {Count} jobs still running", jobsToWait.Count);
                }
                else
                {
                    logger.LogInformation("All jobs completed gracefully");
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during graceful shutdown");
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

    private static JobEventMessage CreateEvent(string runId, string eventType, string summary, string payloadJson)
        => new()
        {
            RunId = runId,
            EventType = eventType,
            Summary = summary,
            Metadata = string.IsNullOrEmpty(payloadJson) ? null : new Dictionary<string, string> { ["payload"] = payloadJson },
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
}
