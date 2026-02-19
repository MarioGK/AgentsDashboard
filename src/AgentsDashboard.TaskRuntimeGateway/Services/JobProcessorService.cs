using System.Text.Json;
using AgentsDashboard.Contracts.TaskRuntime;
using AgentsDashboard.TaskRuntimeGateway;
using AgentsDashboard.TaskRuntimeGateway.Models;

namespace AgentsDashboard.TaskRuntimeGateway.Services;

public class JobProcessorService(
    ITaskRuntimeQueue queue,
    IHarnessExecutor executor,
    TaskRuntimeEventBus eventBus,
    ILogger<JobProcessorService> logger) : BackgroundService
{
    private const string RuntimeEventWireMarker = "agentsdashboard.harness-runtime-event.v1";
    private const string DefaultStructuredSchemaVersion = "harness-structured-event-v2";

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
        var request = queuedJob.Request;
        var parsedChunks = 0;
        long maxSequence = 0;
        var fallbackChunks = 0;

        logger.LogInformation(
            "Starting job execution {@Job}",
            new
            {
                request.RunId,
                request.TaskId,
                request.HarnessType,
                request.Mode,
                request.ArtifactPolicyMaxArtifacts,
                request.ArtifactPolicyMaxTotalSizeBytes,
                request.TimeoutSeconds,
                request.PreferNativeMultimodal,
                InputParts = request.InputParts?.Count ?? 0,
                ImageAttachments = request.ImageAttachments?.Count ?? 0,
                Schema = ResolveStructuredSchemaVersion(request),
            });

        try
        {
            await eventBus.PublishAsync(CreateEvent(request.RunId, "log", "Job started", string.Empty), cancellationToken);

            async Task OnLogChunk(string chunk, CancellationToken ct)
            {
                if (TryParseRuntimeEventChunk(chunk, out var runtimeEvent))
                {
                    parsedChunks++;
                    if (runtimeEvent.Sequence > maxSequence)
                    {
                        maxSequence = runtimeEvent.Sequence;
                    }

                    var structuredProjection = BuildStructuredProjection(
                        runtimeEvent,
                        ResolveStructuredSchemaVersion(request));

                    var logEvent = CreateEvent(
                        request.RunId,
                        "log_chunk",
                        BuildLogSummary(runtimeEvent),
                        string.Empty,
                        sequence: runtimeEvent.Sequence,
                        category: structuredProjection.Category,
                        structuredPayloadJson: structuredProjection.PayloadJson,
                        schemaVersion: structuredProjection.SchemaVersion);

                    await eventBus.PublishAsync(logEvent, ct);
                    return;
                }

                fallbackChunks++;
                var fallbackLog = CreateEvent(request.RunId, "log_chunk", chunk, string.Empty);
                await eventBus.PublishAsync(fallbackLog, ct);
                return;
            }

            var envelope = await executor.ExecuteAsync(queuedJob, OnLogChunk, cancellationToken);
            var payload = JsonSerializer.Serialize(envelope);

            await eventBus.PublishAsync(
                CreateEvent(request.RunId, "completed", envelope.Summary, payload),
                cancellationToken);

            logger.LogInformation(
                "Job processing completed {@Result}",
                new
                {
                    request.RunId,
                    request.TaskId,
                    envelope.Status,
                    envelope.Summary,
                    HasError = !string.IsNullOrWhiteSpace(envelope.Error),
                    ParsedChunks = parsedChunks,
                    FallbackChunks = fallbackChunks,
                    MaxSequence = maxSequence,
                    ArtifactCount = envelope.Artifacts?.Count ?? 0,
                    MetadataCount = envelope.Metadata.Count,
                });
        }
        catch (OperationCanceledException)
        {
            await eventBus.PublishAsync(
                CreateEvent(request.RunId, "completed", "Job cancelled", "{\"status\":\"failed\",\"summary\":\"Cancelled\",\"error\":\"Cancelled\"}"),
                CancellationToken.None);

            logger.LogWarning(
                "Job processing cancelled {@Cancellation}",
                new
                {
                    request.RunId,
                    request.TaskId,
                    request.HarnessType,
                    ParsedChunks = parsedChunks,
                    FallbackChunks = fallbackChunks,
                });
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Job processing crashed {@Failure}",
                new
                {
                    request.RunId,
                    request.TaskId,
                    request.HarnessType,
                    ParsedChunks = parsedChunks,
                    FallbackChunks = fallbackChunks,
                    MaxSequence = maxSequence,
                });
            await eventBus.PublishAsync(
                CreateEvent(request.RunId, "completed", "Job crashed", "{\"status\":\"failed\",\"summary\":\"Crash\",\"error\":\"Worker crashed\"}"),
                CancellationToken.None);
        }
        finally
        {
            queue.MarkCompleted(request.RunId);
            linkedCts.Dispose();
            queuedJob.CancellationSource.Dispose();
        }
    }

    private static JobEventMessage CreateEvent(
        string runId,
        string eventType,
        string summary,
        string payloadJson,
        long sequence = 0,
        string category = "",
        string? structuredPayloadJson = null,
        string schemaVersion = "")
        => new()
        {
            RunId = runId,
            EventType = eventType,
            Summary = summary,
            Metadata = string.IsNullOrEmpty(payloadJson) ? null : new Dictionary<string, string> { ["payload"] = payloadJson },
            Sequence = sequence,
            Category = category ?? string.Empty,
            PayloadJson = string.IsNullOrWhiteSpace(structuredPayloadJson) ? null : structuredPayloadJson,
            SchemaVersion = schemaVersion ?? string.Empty,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

    private static string ResolveStructuredSchemaVersion(DispatchJobRequest request)
    {
        return string.IsNullOrWhiteSpace(request.StructuredProtocolVersion)
            ? DefaultStructuredSchemaVersion
            : request.StructuredProtocolVersion.Trim();
    }

    private static bool TryParseRuntimeEventChunk(string chunk, out RuntimeEventWireEnvelope runtimeEvent)
    {
        runtimeEvent = default!;
        if (string.IsNullOrWhiteSpace(chunk))
        {
            return false;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<RuntimeEventWireEnvelope>(chunk);
            if (parsed is null ||
                !string.Equals(parsed.Marker, RuntimeEventWireMarker, StringComparison.Ordinal) ||
                parsed.Sequence <= 0 ||
                string.IsNullOrWhiteSpace(parsed.Type))
            {
                return false;
            }

            runtimeEvent = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static StructuredProjection BuildStructuredProjection(
        RuntimeEventWireEnvelope runtimeEvent,
        string defaultSchemaVersion)
    {
        if (TryExtractEmbeddedStructuredProjection(runtimeEvent, defaultSchemaVersion, out var embeddedProjection))
        {
            return embeddedProjection;
        }

        var normalizedType = runtimeEvent.Type.Trim().ToLowerInvariant();
        var canonicalCategory = ToCanonicalCategory(normalizedType);
        var payload = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["content"] = runtimeEvent.Content ?? string.Empty,
        };

        if (runtimeEvent.Metadata is { Count: > 0 })
        {
            payload["metadata"] = runtimeEvent.Metadata;
        }

        switch (canonicalCategory)
        {
            case "reasoning.delta":
                payload["thinking"] = runtimeEvent.Content ?? string.Empty;
                payload["reasoning"] = runtimeEvent.Content ?? string.Empty;
                break;
            case "assistant.delta":
                payload["text"] = runtimeEvent.Content ?? string.Empty;
                break;
            case "command.delta":
                payload["output"] = runtimeEvent.Content ?? string.Empty;
                break;
            case "diff.updated":
                payload["diffPatch"] = runtimeEvent.Content ?? string.Empty;
                payload["diff"] = runtimeEvent.Content ?? string.Empty;
                break;
            case "error":
                payload["message"] = runtimeEvent.Content ?? string.Empty;
                break;
            case "run.completed":
                if (runtimeEvent.Metadata?.TryGetValue("status", out var completionStatus) == true &&
                    !string.IsNullOrWhiteSpace(completionStatus))
                {
                    payload["status"] = completionStatus.Trim();
                }
                break;
        }

        return new StructuredProjection(
            canonicalCategory,
            JsonSerializer.Serialize(payload),
            defaultSchemaVersion);
    }

    private static bool TryExtractEmbeddedStructuredProjection(
        RuntimeEventWireEnvelope runtimeEvent,
        string defaultSchemaVersion,
        out StructuredProjection projection)
    {
        projection = default!;
        if (!string.Equals(runtimeEvent.Type, "log", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(runtimeEvent.Content) ||
            runtimeEvent.Content[0] != '{')
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(runtimeEvent.Content);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var typeElement) ||
                typeElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var category = typeElement.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(category))
            {
                return false;
            }

            category = ToCanonicalCategory(category);

            var schemaVersion = defaultSchemaVersion;
            if (root.TryGetProperty("schemaVersion", out var schemaElement) &&
                schemaElement.ValueKind == JsonValueKind.String)
            {
                var parsedSchema = schemaElement.GetString()?.Trim();
                if (!string.IsNullOrWhiteSpace(parsedSchema))
                {
                    schemaVersion = parsedSchema;
                }
            }

            var payloadElement = root.TryGetProperty("properties", out var propertiesElement)
                ? propertiesElement
                : root;

            projection = new StructuredProjection(
                category,
                payloadElement.GetRawText(),
                schemaVersion);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string BuildLogSummary(RuntimeEventWireEnvelope runtimeEvent)
    {
        var type = ToCanonicalCategory(runtimeEvent.Type);
        var content = runtimeEvent.Content ?? string.Empty;

        return type switch
        {
            "run.lifecycle" or "assistant.delta" or "reasoning.delta" or "command.delta" => content,
            _ => $"[{type}] {content}"
        };
    }

    private static string ToCanonicalCategory(string? type)
    {
        var normalized = type?.Trim().ToLowerInvariant() ?? string.Empty;
        return normalized switch
        {
            "reasoning_delta" or "reasoning.delta" => "reasoning.delta",
            "assistant_delta" or "assistant.delta" => "assistant.delta",
            "command_output" or "command.delta" => "command.delta",
            "diff_update" or "diff.updated" or "session.diff" => "diff.updated",
            "diagnostic" or "error" => "error",
            "completion" or "run.completed" => "run.completed",
            "log" or "session.status" or "session.idle" or "run.lifecycle" => "run.lifecycle",
            "message.part.delta" or "message.part.updated" => "assistant.delta",
            "session.usage" or "usage.updated" => "usage.updated",
            _ when normalized.Length == 0 => "run.lifecycle",
            _ => normalized
        };
    }

    private sealed record StructuredProjection(
        string Category,
        string PayloadJson,
        string SchemaVersion);

    private sealed record RuntimeEventWireEnvelope(
        string Marker,
        long Sequence,
        string Type,
        string Content,
        Dictionary<string, string>? Metadata);
}
