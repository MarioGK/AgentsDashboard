using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.TaskRuntime;

namespace AgentsDashboard.TaskRuntimeGateway.Services.HarnessRuntimes;

public sealed partial class OpenCodeSseRuntime(
    SecretRedactor secretRedactor,
    ILogger<OpenCodeSseRuntime> logger) : IHarnessRuntime
{
    private const int DefaultServerStartupTimeoutSeconds = 30;
    private const int MinimumWaitSeconds = 10;
    private const int SessionStatusPollDelayMs = 400;
    private const int MaxTextMetadataLength = 12_000;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string Name => "opencode-sse";

    public async Task<HarnessRuntimeResult> RunAsync(HarnessRunRequest request, IHarnessEventSink sink, CancellationToken ct)
    {
        if (!IsOpenCodeHarness(request.Harness))
        {
            throw new InvalidOperationException($"Runtime '{Name}' only supports opencode harness.");
        }

        var timeout = request.Timeout > TimeSpan.Zero ? request.Timeout : TimeSpan.FromMinutes(10);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout < TimeSpan.FromSeconds(MinimumWaitSeconds)
            ? TimeSpan.FromSeconds(MinimumWaitSeconds)
            : timeout);
        var runtimeCt = timeoutCts.Token;

        var policy = OpenCodeRuntimePolicies.Resolve(request);
        var model = ResolveModel(request.Environment);
        var directory = ResolveDirectory(request.WorkspacePath);

        try
        {
            await using var server = await StartServerAsync(
                directory,
                request.Environment,
                sink,
                runtimeCt);

            var sessionPayload = BuildSessionPayload(request, policy);
            var sessionId = await server.Client.CreateSessionAsync(directory, sessionPayload, runtimeCt);

            await PublishNormalizedEventAsync(
                sink,
                sequence: 0,
                sessionId,
                "session.status",
                JsonSerializer.SerializeToElement(new Dictionary<string, object?>
                {
                    ["sessionID"] = sessionId,
                    ["status"] = new Dictionary<string, string>
                    {
                        ["type"] = "created",
                    }
                }),
                request.Environment,
                runtimeCt);

            var useNativeInput = request.PreferNativeMultimodal && HasImageInputParts(request.InputParts);
            var promptPayload = BuildPromptPayload(request, policy, model, useNativeInput);
            try
            {
                await server.Client.PromptAsync(sessionId, directory, promptPayload, runtimeCt);
            }
            catch (Exception ex) when (useNativeInput)
            {
                await sink.PublishAsync(
                    new HarnessRuntimeEvent(
                        HarnessRuntimeEventType.Diagnostic,
                        $"OpenCode native multimodal payload failed; retrying with text fallback. {ex.Message}"),
                    runtimeCt);

                promptPayload = BuildPromptPayload(request, policy, model, useNativeInput: false);
                await server.Client.PromptAsync(sessionId, directory, promptPayload, runtimeCt);
            }

            var terminalStatus = await WaitForSessionIdleAsync(
                server.Client,
                directory,
                sessionId,
                timeout,
                sink,
                request.Environment,
                runtimeCt);

            using var messages = await server.Client.GetMessagesAsync(sessionId, directory, runtimeCt);
            using var diff = await server.Client.GetDiffAsync(sessionId, directory, runtimeCt);

            var envelope = BuildEnvelope(
                request,
                policy,
                sessionId,
                terminalStatus,
                messages.RootElement,
                diff.RootElement);

            envelope.Metadata["runtime"] = Name;
            envelope.Metadata["runtimeMode"] = "sse";

            await sink.PublishAsync(
                new HarnessRuntimeEvent(
                    HarnessRuntimeEventType.Completion,
                    envelope.Summary,
                    new Dictionary<string, string>
                    {
                        ["status"] = envelope.Status,
                        ["sessionId"] = sessionId,
                    }),
                runtimeCt);

            return new HarnessRuntimeResult
            {
                Structured = true,
                ExitCode = string.Equals(envelope.Status, "succeeded", StringComparison.OrdinalIgnoreCase) ? 0 : 1,
                Envelope = envelope,
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "OpenCode SSE runtime failed for run {RunId}", request.RunId);

            var error = Redact(ex.Message, request.Environment);
            await sink.PublishAsync(
                new HarnessRuntimeEvent(HarnessRuntimeEventType.Diagnostic, error),
                CancellationToken.None);

            return new HarnessRuntimeResult
            {
                Structured = true,
                ExitCode = 1,
                Envelope = new HarnessResultEnvelope
                {
                    RunId = request.RunId,
                    TaskId = request.TaskId,
                    Status = "failed",
                    Summary = "OpenCode SSE runtime failed",
                    Error = error,
                    Metadata = new Dictionary<string, string>
                    {
                        ["harness"] = "opencode",
                        ["transport"] = "sse",
                        ["runtime"] = Name,
                    }
                },
            };
        }
    }

    private async Task<string> WaitForSessionIdleAsync(
        OpenCodeApiClient client,
        string directory,
        string sessionId,
        TimeSpan timeout,
        IHarnessEventSink sink,
        Dictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var effectiveTimeout = timeout > TimeSpan.Zero ? timeout : TimeSpan.FromMinutes(10);
        if (effectiveTimeout < TimeSpan.FromSeconds(MinimumWaitSeconds))
        {
            effectiveTimeout = TimeSpan.FromSeconds(MinimumWaitSeconds);
        }

        timeoutCts.CancelAfter(effectiveTimeout);

        var sequence = 0L;
        var terminalStatus = string.Empty;

        await foreach (var @event in client.SubscribeEventsAsync(directory, timeoutCts.Token))
        {
            foreach (var normalized in NormalizeEvent(@event, sessionId))
            {
                sequence++;
                await PublishNormalizedEventAsync(
                    sink,
                    sequence,
                    sessionId,
                    normalized.Type,
                    normalized.Properties,
                    environment,
                    timeoutCts.Token);

                if (string.Equals(normalized.Type, "session.status", StringComparison.Ordinal))
                {
                    var statusType = ReadStatusType(normalized.Properties);
                    if (!string.IsNullOrWhiteSpace(statusType))
                    {
                        terminalStatus = statusType;
                    }

                    if (IsIdleStatus(normalized.Properties))
                    {
                        return "idle";
                    }
                }
                else if (string.Equals(normalized.Type, "session.idle", StringComparison.Ordinal))
                {
                    return "idle";
                }
            }
        }

        while (!timeoutCts.Token.IsCancellationRequested)
        {
            using var statusSnapshot = await client.GetStatusesAsync(directory, timeoutCts.Token);
            if (!TryReadSessionStatus(statusSnapshot.RootElement, sessionId, out var status))
            {
                return string.IsNullOrWhiteSpace(terminalStatus) ? "idle" : terminalStatus;
            }

            var projection = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
            {
                ["sessionID"] = sessionId,
                ["status"] = status,
            });

            sequence++;
            await PublishNormalizedEventAsync(
                sink,
                sequence,
                sessionId,
                "session.status",
                projection,
                environment,
                timeoutCts.Token);

            var polledStatus = ReadString(status, "type");
            if (!string.IsNullOrWhiteSpace(polledStatus))
            {
                terminalStatus = polledStatus;
            }

            if (string.Equals(polledStatus, "idle", StringComparison.OrdinalIgnoreCase))
            {
                sequence++;
                await PublishNormalizedEventAsync(
                    sink,
                    sequence,
                    sessionId,
                    "session.idle",
                    projection,
                    environment,
                    timeoutCts.Token);

                return "idle";
            }

            await Task.Delay(SessionStatusPollDelayMs, timeoutCts.Token);
        }

        throw new TimeoutException($"OpenCode session {sessionId} did not reach idle state before timeout.");
    }

    private async Task PublishNormalizedEventAsync(
        IHarnessEventSink sink,
        long sequence,
        string sessionId,
        string type,
        JsonElement properties,
        Dictionary<string, string> environment,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(new
        {
            schemaVersion = "opencode.sse.v1",
            sequence,
            type,
            sessionId,
            timestampUtc = DateTime.UtcNow,
            properties,
        }, s_jsonOptions);

        await sink.PublishAsync(
            new HarnessRuntimeEvent(HarnessRuntimeEventType.Log, Redact(payload, environment)),
            cancellationToken);

        if (string.Equals(type, "message.part.delta", StringComparison.Ordinal))
        {
            var delta = ReadString(properties, "delta");
            if (!string.IsNullOrWhiteSpace(delta))
            {
                await sink.PublishAsync(
                    new HarnessRuntimeEvent(HarnessRuntimeEventType.AssistantDelta, Redact(delta, environment)),
                    cancellationToken);
            }

            return;
        }

        if (!string.Equals(type, "session.diff", StringComparison.Ordinal))
        {
            return;
        }

        var diff = ReadString(properties, "diff");
        if (string.IsNullOrWhiteSpace(diff))
        {
            diff = ReadString(properties, "patch");
        }

        if (!string.IsNullOrWhiteSpace(diff))
        {
            await sink.PublishAsync(
                new HarnessRuntimeEvent(HarnessRuntimeEventType.DiffUpdate, Redact(diff, environment)),
                cancellationToken);
        }
    }

    private static IEnumerable<(string Type, JsonElement Properties)> NormalizeEvent(OpenCodeSseEvent @event, string sessionId)
    {
        switch (@event.Type)
        {
            case "message.part.delta":
            {
                if (IsEventForSession(@event, sessionId))
                {
                    yield return ("message.part.delta", @event.Properties);
                }

                yield break;
            }
            case "message.part.updated":
            {
                if (!IsEventForSession(@event, sessionId))
                {
                    yield break;
                }

                yield return ("message.part.updated", @event.Properties);

                if (@event.Properties.TryGetProperty("delta", out var delta) &&
                    delta.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(delta.GetString()) &&
                    @event.Properties.TryGetProperty("part", out var part))
                {
                    var deltaProjection = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
                    {
                        ["sessionID"] = ReadString(part, "sessionID"),
                        ["messageID"] = ReadString(part, "messageID"),
                        ["partID"] = ReadString(part, "id"),
                        ["field"] = "text",
                        ["delta"] = delta.GetString(),
                    });

                    yield return ("message.part.delta", deltaProjection);
                }

                yield break;
            }
            case "session.diff":
            {
                if (IsEventForSession(@event, sessionId))
                {
                    yield return ("session.diff", @event.Properties);
                }

                yield break;
            }
            case "session.status":
            {
                if (IsEventForSession(@event, sessionId))
                {
                    yield return ("session.status", @event.Properties);
                }

                yield break;
            }
            case "session.idle":
            {
                if (IsEventForSession(@event, sessionId))
                {
                    yield return ("session.idle", @event.Properties);
                }

                yield break;
            }
        }
    }

    private static bool IsEventForSession(OpenCodeSseEvent @event, string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return false;
        }

        switch (@event.Type)
        {
            case "message.part.delta":
            {
                var messageSessionId = ReadString(@event.Properties, "sessionID");
                return string.Equals(messageSessionId, sessionId, StringComparison.Ordinal);
            }
            case "message.part.updated":
            {
                if (!@event.Properties.TryGetProperty("part", out var part))
                {
                    return false;
                }

                var partSessionId = ReadString(part, "sessionID");
                return string.Equals(partSessionId, sessionId, StringComparison.Ordinal);
            }
            case "session.diff":
            case "session.status":
            case "session.idle":
            {
                var eventSessionId = ReadString(@event.Properties, "sessionID");
                return string.Equals(eventSessionId, sessionId, StringComparison.Ordinal);
            }
            default:
                return false;
        }
    }

    private static bool IsIdleStatus(JsonElement sessionStatusEvent)
    {
        return string.Equals(ReadStatusType(sessionStatusEvent), "idle", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadStatusType(JsonElement sessionStatusEvent)
    {
        if (sessionStatusEvent.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        if (sessionStatusEvent.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.Object)
        {
            return ReadString(status, "type");
        }

        return ReadString(sessionStatusEvent, "type");
    }

    private static bool TryReadSessionStatus(JsonElement snapshot, string sessionId, out JsonElement status)
    {
        if (snapshot.ValueKind == JsonValueKind.Object && snapshot.TryGetProperty(sessionId, out status))
        {
            status = status.Clone();
            return status.ValueKind == JsonValueKind.Object;
        }

        status = default;
        return false;
    }

    private static HarnessResultEnvelope BuildEnvelope(
        HarnessRunRequest request,
        OpenCodeRuntimePolicy policy,
        string sessionId,
        string terminalStatus,
        JsonElement messages,
        JsonElement diff)
    {
        var latestAssistant = FindLatestAssistantMessage(messages);
        var assistantText = ExtractAssistantText(latestAssistant);
        var assistantError = ExtractAssistantError(latestAssistant);
        var assistantMessageId = ExtractAssistantMessageId(latestAssistant);

        var changedFiles = new List<string>();
        var additions = 0;
        var deletions = 0;

        foreach (var entry in EnumerateDiffEntries(diff))
        {
            var file = ReadString(entry, "file");
            if (string.IsNullOrWhiteSpace(file))
            {
                file = ReadString(entry, "path");
            }

            if (!string.IsNullOrWhiteSpace(file))
            {
                changedFiles.Add(file);
            }

            additions += ReadInt(entry, "additions");
            deletions += ReadInt(entry, "deletions");
        }

        var distinctFiles = changedFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var failedByStatus = IsFailureStatus(terminalStatus);
        var status = !string.IsNullOrWhiteSpace(assistantError) || failedByStatus
            ? "failed"
            : "succeeded";

        var error = !string.IsNullOrWhiteSpace(assistantError)
            ? assistantError
            : failedByStatus
                ? $"OpenCode session ended with status '{terminalStatus}'."
                : string.Empty;

        var summary = BuildSummary(status, assistantText, distinctFiles.Count);

        var envelope = new HarnessResultEnvelope
        {
            RunId = request.RunId,
            TaskId = request.TaskId,
            Status = status,
            Summary = summary,
            Error = error,
            Artifacts = distinctFiles,
            Metadata = new Dictionary<string, string>
            {
                ["harness"] = "opencode",
                ["transport"] = "sse",
                ["opencodeSessionId"] = sessionId,
                ["opencodeMode"] = policy.Mode.ToString().ToLowerInvariant(),
                ["opencodeAgent"] = policy.Agent,
                ["opencodeTerminalStatus"] = string.IsNullOrWhiteSpace(terminalStatus) ? "unknown" : terminalStatus,
                ["changedFiles"] = string.Join(",", distinctFiles),
                ["diffFileCount"] = distinctFiles.Count.ToString(CultureInfo.InvariantCulture),
                ["diffAdditions"] = additions.ToString(CultureInfo.InvariantCulture),
                ["diffDeletions"] = deletions.ToString(CultureInfo.InvariantCulture),
            }
        };

        if (!string.IsNullOrWhiteSpace(assistantMessageId))
        {
            envelope.Metadata["assistantMessageId"] = assistantMessageId;
        }

        if (!string.IsNullOrWhiteSpace(assistantText))
        {
            envelope.Metadata["assistantText"] = Truncate(assistantText, MaxTextMetadataLength);
        }

        return envelope;
    }

    private static string BuildSummary(string status, string assistantText, int changedFileCount)
    {
        if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
        {
            return "OpenCode session failed";
        }

        var flattened = assistantText.ReplaceLineEndings(" ").Trim();
        if (!string.IsNullOrWhiteSpace(flattened))
        {
            return Truncate(flattened, 180);
        }

        if (changedFileCount > 0)
        {
            return $"OpenCode session completed with {changedFileCount} changed file(s)";
        }

        return "OpenCode session completed";
    }

    private static bool IsFailureStatus(string terminalStatus)
    {
        if (string.IsNullOrWhiteSpace(terminalStatus))
        {
            return false;
        }

        return terminalStatus.Trim().ToLowerInvariant() switch
        {
            "error" or "failed" or "cancelled" or "canceled" => true,
            _ => false,
        };
    }

    private static IEnumerable<JsonElement> EnumerateDiffEntries(JsonElement diff)
    {
        if (diff.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in diff.EnumerateArray())
            {
                yield return item;
            }

            yield break;
        }

        if (diff.ValueKind != JsonValueKind.Object)
        {
            yield break;
        }

        if (diff.TryGetProperty("files", out var files) && files.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in files.EnumerateArray())
            {
                yield return item;
            }

            yield break;
        }

        if (diff.TryGetProperty("changes", out var changes) && changes.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in changes.EnumerateArray())
            {
                yield return item;
            }
        }
    }

    private static JsonElement FindLatestAssistantMessage(JsonElement messages)
    {
        if (messages.ValueKind != JsonValueKind.Array)
        {
            return default;
        }

        JsonElement latest = default;
        foreach (var item in messages.EnumerateArray())
        {
            if (!item.TryGetProperty("info", out var info))
            {
                continue;
            }

            if (!string.Equals(ReadString(info, "role"), "assistant", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            latest = item;
        }

        return latest;
    }

    private static string ExtractAssistantText(JsonElement assistantMessage)
    {
        if (assistantMessage.ValueKind != JsonValueKind.Object ||
            !assistantMessage.TryGetProperty("parts", out var parts) ||
            parts.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var part in parts.EnumerateArray())
        {
            if (!string.Equals(ReadString(part, "type"), "text", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (part.TryGetProperty("ignored", out var ignored) && ignored.ValueKind == JsonValueKind.True)
            {
                continue;
            }

            var text = ReadString(part, "text");
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(text.Trim());
        }

        return builder.ToString().Trim();
    }

    private static string ExtractAssistantError(JsonElement assistantMessage)
    {
        if (assistantMessage.ValueKind != JsonValueKind.Object ||
            !assistantMessage.TryGetProperty("info", out var info) ||
            info.ValueKind != JsonValueKind.Object ||
            !info.TryGetProperty("error", out var error) ||
            error.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return string.Empty;
        }

        if (error.ValueKind == JsonValueKind.Object)
        {
            if (error.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            {
                var dataMessage = ReadString(data, "message");
                if (!string.IsNullOrWhiteSpace(dataMessage))
                {
                    return dataMessage;
                }
            }

            var errorName = ReadString(error, "name");
            if (!string.IsNullOrWhiteSpace(errorName))
            {
                return errorName;
            }
        }

        return error.ToString();
    }

    private static string ExtractAssistantMessageId(JsonElement assistantMessage)
    {
        if (assistantMessage.ValueKind != JsonValueKind.Object ||
            !assistantMessage.TryGetProperty("info", out var info))
        {
            return string.Empty;
        }

        return ReadString(info, "id");
    }

    private static string ResolveDirectory(string workingDirectory)
    {
        var candidate = string.IsNullOrWhiteSpace(workingDirectory)
            ? Directory.GetCurrentDirectory()
            : workingDirectory;

        var fullPath = Path.GetFullPath(candidate);
        if (!Directory.Exists(fullPath))
        {
            Directory.CreateDirectory(fullPath);
        }

        return fullPath;
    }

    private static Dictionary<string, object?> BuildSessionPayload(HarnessRunRequest request, OpenCodeRuntimePolicy policy)
    {
        var payload = new Dictionary<string, object?>
        {
            ["title"] = $"run-{request.RunId}",
        };

        if (policy.SessionPermissionRules is { Count: > 0 })
        {
            payload["permission"] = policy.SessionPermissionRules;
        }

        return payload;
    }

    private static Dictionary<string, object?> BuildPromptPayload(
        HarnessRunRequest request,
        OpenCodeRuntimePolicy policy,
        (string ProviderId, string ModelId)? model,
        bool useNativeInput)
    {
        var parts = BuildPromptParts(request, useNativeInput);

        var payload = new Dictionary<string, object?>
        {
            ["agent"] = policy.Agent,
            ["parts"] = parts,
        };

        if (model.HasValue)
        {
            payload["model"] = new Dictionary<string, string>
            {
                ["providerID"] = model.Value.ProviderId,
                ["modelID"] = model.Value.ModelId,
            };
        }

        if (!string.IsNullOrWhiteSpace(policy.SystemPrompt))
        {
            payload["system"] = policy.SystemPrompt;
        }

        return payload;
    }

    private static bool HasImageInputParts(IReadOnlyList<DispatchInputPart> inputParts)
    {
        for (var index = 0; index < inputParts.Count; index++)
        {
            if (string.Equals(inputParts[index].Type, "image", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static List<Dictionary<string, object?>> BuildPromptParts(HarnessRunRequest request, bool useNativeInput)
    {
        if (!useNativeInput || request.InputParts.Count == 0)
        {
            return
            [
                new Dictionary<string, object?>
                {
                    ["type"] = "text",
                    ["text"] = request.Prompt,
                }
            ];
        }

        var parts = new List<Dictionary<string, object?>>();
        var hasTextPart = false;

        for (var index = 0; index < request.InputParts.Count; index++)
        {
            var part = request.InputParts[index];
            if (string.Equals(part.Type, "text", StringComparison.OrdinalIgnoreCase))
            {
                var text = part.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    hasTextPart = true;
                    parts.Add(new Dictionary<string, object?>
                    {
                        ["type"] = "text",
                        ["text"] = text,
                    });
                }

                continue;
            }

            if (string.Equals(part.Type, "image", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(part.ImageRef))
            {
                parts.Add(new Dictionary<string, object?>
                {
                    ["type"] = "image",
                    ["imageUrl"] = part.ImageRef,
                    ["mimeType"] = part.MimeType,
                    ["alt"] = part.Alt,
                });
            }
        }

        if (!hasTextPart && !string.IsNullOrWhiteSpace(request.Prompt))
        {
            parts.Insert(0, new Dictionary<string, object?>
            {
                ["type"] = "text",
                ["text"] = request.Prompt,
            });
        }

        if (parts.Count == 0)
        {
            parts.Add(new Dictionary<string, object?>
            {
                ["type"] = "text",
                ["text"] = request.Prompt,
            });
        }

        return parts;
    }

    private static (string ProviderId, string ModelId)? ResolveModel(IReadOnlyDictionary<string, string>? env)
    {
        if (env is null || env.Count == 0)
        {
            return null;
        }

        var configuredModel = GetEnvValue(env, "OPENCODE_MODEL") ?? GetEnvValue(env, "HARNESS_MODEL");
        if (string.IsNullOrWhiteSpace(configuredModel))
        {
            return null;
        }

        var providerId = GetEnvValue(env, "OPENCODE_PROVIDER");
        var modelId = configuredModel.Trim();

        if (string.IsNullOrWhiteSpace(providerId))
        {
            var separator = modelId.IndexOf('/');
            if (separator > 0 && separator < modelId.Length - 1)
            {
                providerId = modelId[..separator];
                modelId = modelId[(separator + 1)..];
            }
        }

        if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        return (providerId.Trim(), modelId.Trim());
    }

    private async Task<OpenCodeServerHandle> StartServerAsync(
        string workingDirectory,
        Dictionary<string, string> env,
        IHarnessEventSink sink,
        CancellationToken cancellationToken)
    {
        var baseUrl = GetEnvValue(env, "OPENCODE_SERVER_BASE_URL") ?? GetEnvValue(env, "OPENCODE_SERVER_URL");
        var username = GetEnvValue(env, "OPENCODE_SERVER_USERNAME");
        var password = GetEnvValue(env, "OPENCODE_SERVER_PASSWORD");

        if (!string.IsNullOrWhiteSpace(baseUrl))
        {
            var externalClient = new OpenCodeApiClient(new Uri(baseUrl, UriKind.Absolute), username, password);
            var healthy = await externalClient.IsHealthyAsync(cancellationToken);
            if (!healthy)
            {
                externalClient.Dispose();
                throw new InvalidOperationException($"Configured OpenCode server is not healthy: {baseUrl}");
            }

            return new OpenCodeServerHandle(externalClient);
        }

        var host = GetEnvValue(env, "OPENCODE_SERVER_HOST") ?? "127.0.0.1";
        var configuredPort = ParsePort(GetEnvValue(env, "OPENCODE_SERVER_PORT"));
        var port = configuredPort > 0 ? configuredPort.Value : GetAvailablePort();
        var uri = new Uri($"http://{host}:{port}", UriKind.Absolute);

        var existingServerClient = new OpenCodeApiClient(uri, username, password);
        if (await existingServerClient.IsHealthyAsync(cancellationToken))
        {
            return new OpenCodeServerHandle(existingServerClient);
        }

        existingServerClient.Dispose();

        var process = CreateServerProcess(host, port, workingDirectory, env);

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Failed to start opencode serve process.");
            }
        }
        catch (Exception ex)
        {
            process.Dispose();
            throw new InvalidOperationException("Failed to launch opencode serve process.", ex);
        }

        var stderrBuffer = new StringBuilder();
        var stdoutPump = PumpProcessStreamAsync(process.StandardOutput, "opencode.server.stdout", sink, env, null, cancellationToken);
        var stderrPump = PumpProcessStreamAsync(process.StandardError, "opencode.server.stderr", sink, env, stderrBuffer, cancellationToken);
        var client = new OpenCodeApiClient(uri, username, password);

        var startupTimeout = ParsePositiveInt(GetEnvValue(env, "OPENCODE_SERVER_STARTUP_TIMEOUT_SECONDS"))
            ?? DefaultServerStartupTimeoutSeconds;

        var started = await WaitForServerAsync(
            client,
            process,
            TimeSpan.FromSeconds(startupTimeout),
            cancellationToken);

        if (!started)
        {
            await StopProcessAsync(process);
            client.Dispose();
            var stderr = stderrBuffer.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                throw new InvalidOperationException($"OpenCode server startup failed: {stderr}");
            }

            throw new InvalidOperationException("OpenCode server startup failed.");
        }

        logger.LogInformation("Started OpenCode server for run runtime at {BaseUrl}", uri);
        return new OpenCodeServerHandle(client, process, stdoutPump, stderrPump);
    }

    private static Process CreateServerProcess(
        string host,
        int port,
        string workingDirectory,
        IReadOnlyDictionary<string, string> env)
    {
        var info = new ProcessStartInfo("opencode")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingDirectory,
        };

        info.ArgumentList.Add("serve");
        info.ArgumentList.Add("--hostname");
        info.ArgumentList.Add(host);
        info.ArgumentList.Add("--port");
        info.ArgumentList.Add(port.ToString(CultureInfo.InvariantCulture));

        foreach (var (key, value) in env)
        {
            info.Environment[key] = value;
        }

        return new Process
        {
            StartInfo = info,
            EnableRaisingEvents = true,
        };
    }

    private static async Task<bool> WaitForServerAsync(
        OpenCodeApiClient client,
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTime.UtcNow;
        while (DateTime.UtcNow - startedAt < timeout && !cancellationToken.IsCancellationRequested)
        {
            if (process.HasExited)
            {
                return false;
            }

            if (await client.IsHealthyAsync(cancellationToken))
            {
                return true;
            }

            await Task.Delay(250, cancellationToken);
        }

        return false;
    }

    private async Task PumpProcessStreamAsync(
        StreamReader reader,
        string channel,
        IHarnessEventSink sink,
        Dictionary<string, string> environment,
        StringBuilder? capture,
        CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await reader.ReadLineAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (line is null)
            {
                break;
            }

            if (capture is not null && capture.Length < 64_000)
            {
                capture.AppendLine(line);
            }

            var payload = JsonSerializer.Serialize(new
            {
                schemaVersion = "opencode.server.v1",
                type = channel,
                timestampUtc = DateTime.UtcNow,
                line,
            }, s_jsonOptions);

            await sink.PublishAsync(
                new HarnessRuntimeEvent(HarnessRuntimeEventType.Diagnostic, Redact(payload, environment)),
                cancellationToken);
        }
    }

    private static async Task StopProcessAsync(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
        catch
        {
        }
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static string? GetEnvValue(IReadOnlyDictionary<string, string>? env, string key)
    {
        if (env is null)
        {
            return null;
        }

        return env.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    private static int? ParsePort(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port))
        {
            return null;
        }

        return port is > 0 and < 65536 ? port : null;
    }

    private static int? ParsePositiveInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
            ? parsed
            : null;
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static int ReadInt(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return 0;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : 0;
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        if (normalized.Length <= maxLength)
        {
            return normalized;
        }

        return normalized[..maxLength];
    }

    private static bool IsOpenCodeHarness(string harness)
    {
        if (string.IsNullOrWhiteSpace(harness))
        {
            return false;
        }

        return harness.Trim().ToLowerInvariant() switch
        {
            "opencode" => true,
            "open-code" => true,
            "open code" => true,
            _ => false,
        };
    }

    private string Redact(string value, IDictionary<string, string> environment)
    {
        return secretRedactor.Redact(value, environment);
    }

}
