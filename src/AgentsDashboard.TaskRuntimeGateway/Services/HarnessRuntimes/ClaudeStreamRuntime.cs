using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.TaskRuntimeGateway.Services;

namespace AgentsDashboard.TaskRuntimeGateway.Services.HarnessRuntimes;

public class ClaudeStreamRuntime(
    SecretRedactor secretRedactor,
    ILogger logger) : IHarnessRuntime
{
    private const string ClaudeCommand = "claude";
    private const int MaxSummaryLength = 280;
    private const int MaxMetadataValueLength = 20000;
    private const int MaxNormalizedEvents = 512;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public virtual string Name => "claude-stream-json";

    protected virtual string ProviderName => "claude-code";

    protected virtual bool SupportsHarness(string harness)
    {
        return string.Equals(harness, "claude-code", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(harness, "claude code", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<HarnessRuntimeResult> RunAsync(HarnessRunRequest request, IHarnessEventSink sink, CancellationToken ct)
    {
        if (!SupportsHarness(request.Harness))
        {
            throw new InvalidOperationException($"Runtime '{Name}' does not support harness '{request.Harness}'.");
        }

        var mode = ResolveMode(request);
        var environment = new Dictionary<string, string>(request.Environment, StringComparer.OrdinalIgnoreCase);
        ApplyEnvironment(environment, request, mode);

        var model = ResolveModel(environment);
        if (!string.IsNullOrWhiteSpace(model))
        {
            SetEnvironmentValue(environment, "HARNESS_MODEL", model);
        }

        var prompt = ApplyModePrompt(request.Prompt ?? string.Empty, mode);
        var workingDirectory = ResolveWorkingDirectory(request.WorkspacePath);
        var timeout = request.Timeout > TimeSpan.Zero ? request.Timeout : TimeSpan.FromMinutes(10);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        var runtimeCt = timeoutCts.Token;

        var process = BuildProcess(workingDirectory, prompt, model, environment);
        var state = new ParseState(mode);
        var started = false;
        Task stdoutTask = Task.CompletedTask;
        Task stderrTask = Task.CompletedTask;

        logger.ZLogInformation("Starting {RuntimeName} for run {RunId}", Name, request.RunId);

        try
        {
            started = process.Start();
            if (!started)
            {
                throw new InvalidOperationException("Failed to start Claude runtime process.");
            }

            stdoutTask = ReadStdoutAsync(process, state, sink, environment, runtimeCt);
            stderrTask = ReadStderrAsync(process, state, sink, environment, runtimeCt);

            using var cancellationRegistration = runtimeCt.Register(() => TryKill(process));
            await process.WaitForExitAsync(runtimeCt);
            await Task.WhenAll(stdoutTask, stderrTask);

            var envelope = BuildEnvelope(request, state, process.ExitCode);

            await sink.PublishAsync(
                new HarnessRuntimeEvent(
                    HarnessRuntimeEventType.Completion,
                    envelope.Summary,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["status"] = envelope.Status,
                    }),
                runtimeCt);

            logger.ZLogInformation(
                "{RuntimeName} completed for run {RunId} with status {Status}",
                Name,
                request.RunId,
                envelope.Status);

            return new HarnessRuntimeResult
            {
                Structured = true,
                ExitCode = process.ExitCode,
                Envelope = envelope,
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            TryKill(process);
            throw;
        }
        finally
        {
            if (!process.HasExited && started)
            {
                TryKill(process);
            }

            try
            {
                await Task.WhenAll(stdoutTask, stderrTask);
            }
            catch
            {
            }

            process.Dispose();
        }
    }

    protected virtual void ApplyEnvironment(
        Dictionary<string, string> environment,
        HarnessRunRequest request,
        RuntimeExecutionMode mode)
    {
        SetEnvironmentValue(environment, "CLAUDE_OUTPUT_FORMAT", "stream-json");
        SetEnvironmentValue(environment, "CLAUDE_INCLUDE_PARTIAL_MESSAGES", "true");
        SetEnvironmentValue(environment, "CLAUDE_STREAM_JSON", "true");
        SetEnvironmentValue(environment, "HARNESS_RUNTIME_PROVIDER", ProviderName);
        SetEnvironmentValue(environment, "HARNESS_RUNTIME_NAME", Name);
        SetEnvironmentValue(environment, "HARNESS_EXECUTION_MODE", ToModeValue(mode));
    }

    protected virtual string ResolveModel(IReadOnlyDictionary<string, string> environment)
    {
        return GetEnvironmentValue(environment, "CLAUDE_MODEL")
            ?? GetEnvironmentValue(environment, "ANTHROPIC_MODEL")
            ?? GetEnvironmentValue(environment, "HARNESS_MODEL")
            ?? string.Empty;
    }

    protected virtual string ApplyModePrompt(string prompt, RuntimeExecutionMode mode)
    {
        var normalizedPrompt = prompt.Trim();
        var prefix = mode switch
        {
            RuntimeExecutionMode.Plan => "Execution mode: plan.\nDo not modify files, create commits, or run mutating commands.\nProduce only a concrete implementation plan with validation checkpoints.",
            RuntimeExecutionMode.Review => "Execution mode: review.\nDo not modify files, create commits, or run mutating commands.\nFocus on review findings ordered by severity with file references, then open questions, then a concise summary.",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(prefix))
        {
            return normalizedPrompt;
        }

        if (string.IsNullOrWhiteSpace(normalizedPrompt))
        {
            return prefix;
        }

        return $"{prefix}\n\n{normalizedPrompt}";
    }

    protected virtual void ApplyEnvelopePolicy(HarnessResultEnvelope envelope)
    {
    }

    private Process BuildProcess(
        string workingDirectory,
        string prompt,
        string model,
        Dictionary<string, string> environment)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ClaudeCommand,
                WorkingDirectory = workingDirectory,
                RedirectStandardInput = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            }
        };

        process.StartInfo.ArgumentList.Add("-p");
        process.StartInfo.ArgumentList.Add("--verbose");
        process.StartInfo.ArgumentList.Add("--output-format");
        process.StartInfo.ArgumentList.Add("stream-json");
        process.StartInfo.ArgumentList.Add("--include-partial-messages");

        if (!string.IsNullOrWhiteSpace(model))
        {
            process.StartInfo.ArgumentList.Add("--model");
            process.StartInfo.ArgumentList.Add(model.Trim());
        }

        process.StartInfo.ArgumentList.Add(prompt);
        process.StartInfo.Environment["NO_COLOR"] = "1";

        foreach (var (key, value) in environment)
        {
            process.StartInfo.Environment[key] = value;
        }

        return process;
    }

    private async Task ReadStdoutAsync(
        Process process,
        ParseState state,
        IHarnessEventSink sink,
        Dictionary<string, string> environment,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var line = await process.StandardOutput.ReadLineAsync(ct);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            state.StdoutBuilder.AppendLine(Redact(line, environment));
            await ProcessStreamLineAsync(line, state, sink, environment, ct);
        }
    }

    private async Task ReadStderrAsync(
        Process process,
        ParseState state,
        IHarnessEventSink sink,
        Dictionary<string, string> environment,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var line = await process.StandardError.ReadLineAsync(ct);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var redacted = Redact(line, environment);
            state.StderrBuilder.AppendLine(redacted);
            await sink.PublishAsync(new HarnessRuntimeEvent(HarnessRuntimeEventType.Diagnostic, redacted), ct);
        }
    }

    private async Task ProcessStreamLineAsync(
        string line,
        ParseState state,
        IHarnessEventSink sink,
        Dictionary<string, string> environment,
        CancellationToken ct)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        if (!IsJsonObjectCandidate(trimmed))
        {
            await sink.PublishAsync(new HarnessRuntimeEvent(HarnessRuntimeEventType.Log, Redact(trimmed, environment)), ct);
            AddNormalizedEvent(state, "raw_line", "output", Redact(trimmed, environment), IsToolHint(trimmed));
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                AddNormalizedEvent(state, "raw_json", "output", Redact(trimmed, environment), false);
                return;
            }

            var eventType = GetString(root, "type");
            if (string.IsNullOrWhiteSpace(eventType))
            {
                await HandleUntypedObjectAsync(root, state, sink, environment, ct);
                return;
            }

            var normalizedType = eventType.Trim();
            state.EventTypes.Add(normalizedType);

            switch (normalizedType.ToLowerInvariant())
            {
                case "message_start":
                    HandleMessageStart(root, state);
                    break;
                case "content_block_start":
                    await HandleContentBlockStartAsync(root, state, sink, environment, ct);
                    break;
                case "content_block_delta":
                    await HandleContentBlockDeltaAsync(root, state, sink, environment, ct);
                    break;
                case "content_block_stop":
                    await HandleContentBlockStopAsync(root, state, sink, environment, ct);
                    break;
                case "message_delta":
                    HandleMessageDelta(root, state);
                    break;
                case "message_stop":
                    HandleMessageStop(state);
                    break;
                case "result":
                case "final_result":
                case "final":
                    HandleResultEvent(root, state);
                    break;
                case "error":
                    await HandleErrorEventAsync(root, state, sink, environment, ct);
                    break;
                default:
                    await HandleUnknownTypedEventAsync(root, normalizedType, state, sink, environment, ct);
                    break;
            }
        }
        catch
        {
            var redacted = Redact(trimmed, environment);
            await sink.PublishAsync(new HarnessRuntimeEvent(HarnessRuntimeEventType.Log, redacted), ct);
            AddNormalizedEvent(state, "raw_line", "output", redacted, IsToolHint(trimmed));
        }
    }

    private void HandleMessageStart(JsonElement root, ParseState state)
    {
        var details = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (TryGetPropertyIgnoreCase(root, "message", out var message) && message.ValueKind == JsonValueKind.Object)
        {
            var messageId = GetString(message, "id");
            var model = GetString(message, "model");
            var role = GetString(message, "role");

            if (!string.IsNullOrWhiteSpace(messageId))
            {
                details["messageId"] = messageId;
            }

            if (!string.IsNullOrWhiteSpace(model))
            {
                details["model"] = model;
                state.Model = model;
            }

            if (!string.IsNullOrWhiteSpace(role))
            {
                details["role"] = role;
            }

            if (TryGetPropertyIgnoreCase(message, "usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                ApplyUsageMetrics(usage, state.Metrics);
            }

            if (TryGetPropertyIgnoreCase(message, "content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                var text = ExtractTextFromContentArray(content);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    AppendAssistantText(state, text);
                }
            }
        }

        AddNormalizedEvent(state, "message_start", "message", "assistant message started", false, details);
    }

    private async Task HandleContentBlockStartAsync(
        JsonElement root,
        ParseState state,
        IHarnessEventSink sink,
        Dictionary<string, string> environment,
        CancellationToken ct)
    {
        var index = GetInt32(root, "index");
        var details = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var blockType = "unknown";
        var toolName = string.Empty;
        var toolCallId = string.Empty;
        var isToolRelated = false;

        if (index.HasValue)
        {
            details["index"] = index.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (TryGetPropertyIgnoreCase(root, "content_block", out var block) && block.ValueKind == JsonValueKind.Object)
        {
            blockType = GetString(block, "type") ?? "unknown";
            details["blockType"] = blockType;
            var lowerBlockType = blockType.ToLowerInvariant();
            isToolRelated = lowerBlockType.Contains("tool", StringComparison.Ordinal);

            var text = GetString(block, "text");
            if (!string.IsNullOrWhiteSpace(text))
            {
                var redactedText = Redact(text, environment);
                AppendAssistantText(state, redactedText);
                await sink.PublishAsync(new HarnessRuntimeEvent(HarnessRuntimeEventType.AssistantDelta, redactedText), ct);
            }

            if (isToolRelated)
            {
                toolName = GetString(block, "name", "tool_name", "tool") ?? "tool";
                toolCallId = GetString(block, "id", "tool_call_id", "call_id") ?? string.Empty;

                if (!string.IsNullOrWhiteSpace(toolName))
                {
                    details["toolName"] = toolName;
                }

                if (!string.IsNullOrWhiteSpace(toolCallId))
                {
                    details["toolCallId"] = toolCallId;
                }

                if (lowerBlockType.Contains("result", StringComparison.Ordinal))
                {
                    AddToolAction(state, "tool_result_start", toolName, toolCallId, "result started");
                    await sink.PublishAsync(
                        new HarnessRuntimeEvent(HarnessRuntimeEventType.Diagnostic, $"tool result started: {toolName} {toolCallId}".Trim()),
                        ct);
                }
                else
                {
                    AddToolAction(state, "tool_start", toolName, toolCallId, "started");
                    await sink.PublishAsync(
                        new HarnessRuntimeEvent(HarnessRuntimeEventType.Diagnostic, $"tool started: {toolName} {toolCallId}".Trim()),
                        ct);
                }
            }
        }

        if (index.HasValue)
        {
            state.ContentBlocks[index.Value] = new ContentBlockState
            {
                BlockType = blockType,
                ToolName = toolName,
                ToolCallId = toolCallId,
            };
        }

        AddNormalizedEvent(
            state,
            "content_block_start",
            "content",
            $"{blockType} block started",
            isToolRelated,
            details);
    }

    private async Task HandleContentBlockDeltaAsync(
        JsonElement root,
        ParseState state,
        IHarnessEventSink sink,
        Dictionary<string, string> environment,
        CancellationToken ct)
    {
        var index = GetInt32(root, "index");
        var details = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var deltaType = "unknown";
        var isToolRelated = false;
        ContentBlockState? blockState = null;

        if (index.HasValue)
        {
            details["index"] = index.Value.ToString(CultureInfo.InvariantCulture);
            state.ContentBlocks.TryGetValue(index.Value, out blockState);
        }

        if (TryGetPropertyIgnoreCase(root, "delta", out var delta) && delta.ValueKind == JsonValueKind.Object)
        {
            deltaType = GetString(delta, "type") ?? "unknown";
            details["deltaType"] = deltaType;

            var deltaText = GetString(delta, "text", "partial_json");
            if (!string.IsNullOrWhiteSpace(deltaText))
            {
                var redactedText = Redact(deltaText, environment);
                details["deltaPreview"] = Truncate(redactedText, 200);

                if (deltaType.Contains("text", StringComparison.OrdinalIgnoreCase))
                {
                    AppendAssistantText(state, redactedText);
                    await sink.PublishAsync(new HarnessRuntimeEvent(HarnessRuntimeEventType.AssistantDelta, redactedText), ct);
                }
            }

            if (deltaType.Contains("thinking", StringComparison.OrdinalIgnoreCase))
            {
                var thinkingDelta = GetString(delta, "text", "delta");
                if (!string.IsNullOrWhiteSpace(thinkingDelta))
                {
                    await sink.PublishAsync(
                        new HarnessRuntimeEvent(HarnessRuntimeEventType.ReasoningDelta, Redact(thinkingDelta, environment)),
                        ct);
                }
            }
        }

        if (blockState is not null)
        {
            var lowerBlockType = blockState.BlockType.ToLowerInvariant();
            isToolRelated = lowerBlockType.Contains("tool", StringComparison.Ordinal);

            if (isToolRelated)
            {
                if (!string.IsNullOrWhiteSpace(blockState.ToolName))
                {
                    details["toolName"] = blockState.ToolName;
                }

                if (!string.IsNullOrWhiteSpace(blockState.ToolCallId))
                {
                    details["toolCallId"] = blockState.ToolCallId;
                }
            }
        }

        if (!isToolRelated)
        {
            isToolRelated = deltaType.Contains("tool", StringComparison.OrdinalIgnoreCase) ||
                            deltaType.Contains("json", StringComparison.OrdinalIgnoreCase);
        }

        AddNormalizedEvent(
            state,
            "content_block_delta",
            "content",
            $"{deltaType} delta",
            isToolRelated,
            details);
    }

    private async Task HandleContentBlockStopAsync(
        JsonElement root,
        ParseState state,
        IHarnessEventSink sink,
        Dictionary<string, string> environment,
        CancellationToken ct)
    {
        var index = GetInt32(root, "index");
        var details = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var isToolRelated = false;

        if (index.HasValue)
        {
            details["index"] = index.Value.ToString(CultureInfo.InvariantCulture);

            if (state.ContentBlocks.TryGetValue(index.Value, out var blockState))
            {
                var lowerBlockType = blockState.BlockType.ToLowerInvariant();
                isToolRelated = lowerBlockType.Contains("tool", StringComparison.Ordinal);

                if (!string.IsNullOrWhiteSpace(blockState.BlockType))
                {
                    details["blockType"] = blockState.BlockType;
                }

                if (!string.IsNullOrWhiteSpace(blockState.ToolName))
                {
                    details["toolName"] = blockState.ToolName;
                }

                if (!string.IsNullOrWhiteSpace(blockState.ToolCallId))
                {
                    details["toolCallId"] = blockState.ToolCallId;
                }

                if (isToolRelated)
                {
                    if (lowerBlockType.Contains("result", StringComparison.Ordinal))
                    {
                        AddToolAction(state, "tool_result", blockState.ToolName, blockState.ToolCallId, "result completed");
                        await sink.PublishAsync(
                            new HarnessRuntimeEvent(HarnessRuntimeEventType.Diagnostic, Redact($"tool result completed: {blockState.ToolName} {blockState.ToolCallId}".Trim(), environment)),
                            ct);
                    }
                    else
                    {
                        AddToolAction(state, "tool_stop", blockState.ToolName, blockState.ToolCallId, "completed");
                        await sink.PublishAsync(
                            new HarnessRuntimeEvent(HarnessRuntimeEventType.Diagnostic, Redact($"tool completed: {blockState.ToolName} {blockState.ToolCallId}".Trim(), environment)),
                            ct);
                    }
                }
            }

            state.ContentBlocks.Remove(index.Value);
        }

        AddNormalizedEvent(
            state,
            "content_block_stop",
            "content",
            "content block stopped",
            isToolRelated,
            details);
    }

    private void HandleMessageDelta(JsonElement root, ParseState state)
    {
        var details = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (TryGetPropertyIgnoreCase(root, "delta", out var delta) && delta.ValueKind == JsonValueKind.Object)
        {
            var stopReason = GetString(delta, "stop_reason");
            if (!string.IsNullOrWhiteSpace(stopReason))
            {
                state.StopReason = stopReason;
                details["stopReason"] = stopReason;
            }

            var summary = GetString(delta, "summary");
            if (!string.IsNullOrWhiteSpace(summary))
            {
                state.Summary = summary;
            }
        }

        if (TryGetPropertyIgnoreCase(root, "usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            ApplyUsageMetrics(usage, state.Metrics);
        }

        AddNormalizedEvent(state, "message_delta", "message", "message delta", false, details);
    }

    private static void HandleMessageStop(ParseState state)
    {
        AddNormalizedEvent(state, "message_stop", "message", "assistant message stopped", false);
    }

    private void HandleResultEvent(JsonElement root, ParseState state)
    {
        var details = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var statusCandidate = GetString(root, "status", "result", "subtype", "outcome");
        var summary = GetString(root, "summary", "message", "result_message");
        var error = ExtractError(root);
        var successFlag = GetBoolean(root, "success", "ok");
        var errorFlag = GetBoolean(root, "is_error");

        state.FinalResultReceived = true;

        if (!string.IsNullOrWhiteSpace(statusCandidate))
        {
            details["statusCandidate"] = statusCandidate;
            var normalized = NormalizeStatus(statusCandidate);
            if (!string.Equals(normalized, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                state.Status = normalized;
            }
        }

        if (successFlag.HasValue)
        {
            details["success"] = successFlag.Value ? "true" : "false";
            state.Status = successFlag.Value ? "succeeded" : "failed";
        }

        if (errorFlag.HasValue)
        {
            details["isError"] = errorFlag.Value ? "true" : "false";
            if (errorFlag.Value)
            {
                state.Status = "failed";
            }
        }

        if (!string.IsNullOrWhiteSpace(summary))
        {
            state.Summary = summary;
            details["summary"] = Truncate(summary, 180);
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            state.Error = error;
            state.Status = "failed";
            details["error"] = Truncate(error, 180);
        }

        if (TryGetPropertyIgnoreCase(root, "usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            ApplyUsageMetrics(usage, state.Metrics);
        }

        var model = GetString(root, "model");
        if (!string.IsNullOrWhiteSpace(model))
        {
            state.Model = model;
            details["model"] = model;
        }

        AddNormalizedEvent(state, "final_result", "result", "final result received", false, details);
    }

    private async Task HandleErrorEventAsync(
        JsonElement root,
        ParseState state,
        IHarnessEventSink sink,
        Dictionary<string, string> environment,
        CancellationToken ct)
    {
        var error = ExtractError(root);
        if (string.IsNullOrWhiteSpace(error))
        {
            error = GetString(root, "message") ?? "Claude runtime error.";
        }

        var redacted = Redact(error, environment);
        state.Status = "failed";
        state.Error = redacted;

        await sink.PublishAsync(new HarnessRuntimeEvent(HarnessRuntimeEventType.Diagnostic, redacted), ct);
        AddNormalizedEvent(state, "error", "result", redacted, true);
    }

    private async Task HandleUnknownTypedEventAsync(
        JsonElement root,
        string eventType,
        ParseState state,
        IHarnessEventSink sink,
        Dictionary<string, string> environment,
        CancellationToken ct)
    {
        var details = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (TryGetPropertyIgnoreCase(root, "usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            ApplyUsageMetrics(usage, state.Metrics);
        }

        var model = GetString(root, "model");
        if (!string.IsNullOrWhiteSpace(model))
        {
            state.Model = model;
            details["model"] = model;
        }

        var isToolRelated = eventType.Contains("tool", StringComparison.OrdinalIgnoreCase);
        if (isToolRelated)
        {
            var toolName = GetString(root, "tool_name", "tool", "name") ?? "tool";
            var toolCallId = GetString(root, "tool_call_id", "call_id", "id") ?? string.Empty;
            AddToolAction(state, $"tool_{eventType}", toolName, toolCallId, eventType);
            await sink.PublishAsync(
                new HarnessRuntimeEvent(HarnessRuntimeEventType.Diagnostic, Redact($"tool event: {eventType} {toolName} {toolCallId}".Trim(), environment)),
                ct);
        }

        AddNormalizedEvent(state, eventType, "event", $"{eventType} event", isToolRelated, details);
    }

    private async Task HandleUntypedObjectAsync(
        JsonElement root,
        ParseState state,
        IHarnessEventSink sink,
        Dictionary<string, string> environment,
        CancellationToken ct)
    {
        var status = GetString(root, "status");
        var summary = GetString(root, "summary");
        var error = ExtractError(root);
        var successFlag = GetBoolean(root, "success", "ok");

        if (!string.IsNullOrWhiteSpace(status) ||
            !string.IsNullOrWhiteSpace(summary) ||
            !string.IsNullOrWhiteSpace(error) ||
            successFlag.HasValue)
        {
            var details = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(status))
            {
                details["status"] = status;
                state.Status = NormalizeStatus(status);
            }

            if (successFlag.HasValue)
            {
                details["success"] = successFlag.Value ? "true" : "false";
                state.Status = successFlag.Value ? "succeeded" : "failed";
            }

            if (!string.IsNullOrWhiteSpace(summary))
            {
                state.Summary = summary;
                details["summary"] = Truncate(summary, 180);
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                var redactedError = Redact(error, environment);
                state.Error = redactedError;
                state.Status = "failed";
                details["error"] = Truncate(redactedError, 180);
                await sink.PublishAsync(new HarnessRuntimeEvent(HarnessRuntimeEventType.Diagnostic, redactedError), ct);
            }

            state.FinalResultReceived = true;
            AddNormalizedEvent(state, "final_result", "result", "final result object parsed", false, details);
            return;
        }

        var contentText = TryExtractContentText(root);
        if (!string.IsNullOrWhiteSpace(contentText))
        {
            var redactedContent = Redact(contentText, environment);
            AppendAssistantText(state, redactedContent);
            await sink.PublishAsync(new HarnessRuntimeEvent(HarnessRuntimeEventType.AssistantDelta, redactedContent), ct);
            AddNormalizedEvent(state, "content", "content", Truncate(redactedContent, 300), false);
            return;
        }

        var redacted = Redact(root.GetRawText(), environment);
        await sink.PublishAsync(new HarnessRuntimeEvent(HarnessRuntimeEventType.Log, redacted), ct);
        AddNormalizedEvent(state, "raw_json", "output", Truncate(redacted, 400), false);
    }

    private HarnessResultEnvelope BuildEnvelope(HarnessRunRequest request, ParseState state, int exitCode)
    {
        var status = ResolveFinalStatus(state.Status, exitCode);
        var assistantText = NormalizeText(state.AssistantBuilder.ToString());
        var summary = NormalizeText(state.Summary);

        if (string.IsNullOrWhiteSpace(summary))
        {
            summary = !string.IsNullOrWhiteSpace(assistantText)
                ? Truncate(assistantText, MaxSummaryLength)
                : status == "succeeded"
                    ? "Claude stream execution completed"
                    : "Claude stream execution failed";
        }

        var stderrText = NormalizeText(state.StderrBuilder.ToString());
        var error = NormalizeText(state.Error);
        if (!string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase) &&
            string.IsNullOrWhiteSpace(error))
        {
            error = string.IsNullOrWhiteSpace(stderrText)
                ? "Claude stream execution failed."
                : Truncate(stderrText, MaxMetadataValueLength);
        }

        if (string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase))
        {
            error = string.Empty;
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["runtime"] = Name,
            ["provider"] = ProviderName,
            ["mode"] = ToModeValue(state.Mode),
            ["exitCode"] = exitCode.ToString(CultureInfo.InvariantCulture),
            ["streamParsed"] = "true",
            ["streamEventCount"] = state.NormalizedEvents.Count.ToString(CultureInfo.InvariantCulture),
            ["toolLifecycleCount"] = state.ToolLifecycleCount.ToString(CultureInfo.InvariantCulture),
            ["finalResultReceived"] = state.FinalResultReceived ? "true" : "false",
            ["stdout"] = Truncate(state.StdoutBuilder.ToString(), 5000),
            ["stderr"] = Truncate(state.StderrBuilder.ToString(), 5000),
        };

        if (state.EventTypes.Count > 0)
        {
            metadata["streamEventTypes"] = string.Join(",", state.EventTypes.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(assistantText))
        {
            metadata["assistantPreview"] = Truncate(assistantText, MaxMetadataValueLength);
            metadata["assistantChars"] = assistantText.Length.ToString(CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(state.StopReason))
        {
            metadata["stopReason"] = state.StopReason;
        }

        if (!string.IsNullOrWhiteSpace(state.Model))
        {
            metadata["model"] = state.Model;
        }

        if (state.ToolCallIds.Count > 0)
        {
            metadata["toolCalls"] = string.Join(",", state.ToolCallIds.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        }

        if (state.ToolNames.Count > 0)
        {
            metadata["toolNames"] = string.Join(",", state.ToolNames.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        }

        metadata["normalizedEvents"] = Truncate(
            JsonSerializer.Serialize(state.NormalizedEvents, s_jsonOptions),
            MaxMetadataValueLength);

        var envelope = new HarnessResultEnvelope
        {
            RunId = request.RunId,
            TaskId = request.TaskId,
            Status = status,
            Summary = summary,
            Error = error,
            Actions = state.Actions,
            Metrics = state.Metrics,
            Metadata = metadata,
        };

        ApplyEnvelopePolicy(envelope);
        return envelope;
    }

    private string Redact(string value, Dictionary<string, string> environment)
    {
        return secretRedactor.Redact(value, environment);
    }

    private static void AddToolAction(
        ParseState state,
        string type,
        string toolName,
        string toolCallId,
        string stage)
    {
        var safeToolName = string.IsNullOrWhiteSpace(toolName) ? "tool" : toolName.Trim();
        var safeToolCallId = toolCallId?.Trim() ?? string.Empty;
        var description = string.IsNullOrWhiteSpace(safeToolCallId)
            ? $"{safeToolName} {stage}"
            : $"{safeToolName} ({safeToolCallId}) {stage}";
        var target = string.IsNullOrWhiteSpace(safeToolCallId) ? safeToolName : safeToolCallId;

        state.Actions.Add(new HarnessAction
        {
            Type = type,
            Description = description,
            Target = target,
        });

        state.ToolLifecycleCount++;

        if (!string.IsNullOrWhiteSpace(safeToolName))
        {
            state.ToolNames.Add(safeToolName);
        }

        if (!string.IsNullOrWhiteSpace(safeToolCallId))
        {
            state.ToolCallIds.Add(safeToolCallId);
        }
    }

    private static void AddNormalizedEvent(
        ParseState state,
        string type,
        string category,
        string message,
        bool isToolRelated,
        IDictionary<string, string>? metadata = null)
    {
        var sequence = ++state.Sequence;
        if (state.NormalizedEvents.Count >= MaxNormalizedEvents)
        {
            state.NormalizedEvents.RemoveAt(0);
        }

        state.NormalizedEvents.Add(new NormalizedStreamEvent(
            sequence,
            type,
            category,
            Truncate(NormalizeText(message), 600),
            isToolRelated,
            metadata is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase)));
    }

    private static void AppendAssistantText(ParseState state, string text)
    {
        var normalized = NormalizeText(text);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return;
        }

        if (state.AssistantBuilder.Length > 0)
        {
            state.AssistantBuilder.Append('\n');
        }

        state.AssistantBuilder.Append(normalized);
    }

    private static string ResolveWorkingDirectory(string workspacePath)
    {
        if (string.IsNullOrWhiteSpace(workspacePath))
        {
            return Directory.GetCurrentDirectory();
        }

        return workspacePath;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }
    }

    protected static void SetEnvironmentValue(IDictionary<string, string> environment, string key, string value)
    {
        var existingKey = environment.Keys.FirstOrDefault(x => string.Equals(x, key, StringComparison.OrdinalIgnoreCase));
        if (existingKey is null)
        {
            environment[key] = value;
        }
        else
        {
            environment[existingKey] = value;
        }
    }

    protected static string? GetEnvironmentValue(IReadOnlyDictionary<string, string> environment, string key)
    {
        if (environment.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        foreach (var (existingKey, existingValue) in environment)
        {
            if (string.Equals(existingKey, key, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(existingValue))
            {
                return existingValue;
            }
        }

        return null;
    }

    private static RuntimeExecutionMode ResolveMode(HarnessRunRequest request)
    {
        var envMode =
            GetEnvironmentValue(request.Environment, "HARNESS_MODE") ??
            GetEnvironmentValue(request.Environment, "TASK_MODE") ??
            GetEnvironmentValue(request.Environment, "RUN_MODE") ??
            GetEnvironmentValue(request.Environment, "EXECUTION_MODE") ??
            GetEnvironmentValue(request.Environment, "MODE");

        if (!string.IsNullOrWhiteSpace(envMode))
        {
            return ParseMode(envMode);
        }

        if (!string.IsNullOrWhiteSpace(request.Mode))
        {
            return ParseMode(request.Mode);
        }

        return RuntimeExecutionMode.Default;
    }

    private static RuntimeExecutionMode ParseMode(string mode)
    {
        return mode.Trim().ToLowerInvariant() switch
        {
            "plan" or "planning" => RuntimeExecutionMode.Plan,
            "review" or "audit" or "readonly" or "read-only" => RuntimeExecutionMode.Review,
            _ => RuntimeExecutionMode.Default
        };
    }

    private static string ToModeValue(RuntimeExecutionMode mode)
    {
        return mode switch
        {
            RuntimeExecutionMode.Plan => "plan",
            RuntimeExecutionMode.Review => "review",
            _ => "default"
        };
    }

    private static string ResolveFinalStatus(string statusCandidate, int exitCode)
    {
        if (!string.IsNullOrWhiteSpace(statusCandidate))
        {
            var normalized = NormalizeStatus(statusCandidate);
            if (!string.Equals(normalized, "unknown", StringComparison.OrdinalIgnoreCase))
            {
                if (exitCode != 0 && string.Equals(normalized, "succeeded", StringComparison.OrdinalIgnoreCase))
                {
                    return "failed";
                }

                return normalized;
            }
        }

        return exitCode == 0 ? "succeeded" : "failed";
    }

    private static string NormalizeStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return "unknown";
        }

        var normalized = status.Trim().ToLowerInvariant();

        if (normalized.Contains("success", StringComparison.Ordinal) ||
            normalized.Contains("succeeded", StringComparison.Ordinal) ||
            normalized.Contains("complete", StringComparison.Ordinal))
        {
            return "succeeded";
        }

        if (normalized.Contains("fail", StringComparison.Ordinal) ||
            normalized.Contains("error", StringComparison.Ordinal))
        {
            return "failed";
        }

        if (normalized.Contains("cancel", StringComparison.Ordinal))
        {
            return "cancelled";
        }

        if (normalized.Contains("pending", StringComparison.Ordinal) ||
            normalized.Contains("running", StringComparison.Ordinal) ||
            normalized.Contains("progress", StringComparison.Ordinal))
        {
            return "pending";
        }

        return "unknown";
    }

    private static void ApplyUsageMetrics(JsonElement usage, IDictionary<string, double> metrics)
    {
        foreach (var property in usage.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetDouble(out var number))
            {
                metrics[property.Name] = number;
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.String &&
                double.TryParse(property.Value.GetString(), CultureInfo.InvariantCulture, out var parsed))
            {
                metrics[property.Name] = parsed;
            }
        }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            value = default;
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string? GetString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetPropertyIgnoreCase(element, propertyName, out var value))
            {
                continue;
            }

            var text = GetString(value);
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return null;
    }

    private static string? GetString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => element.GetRawText()
        };
    }

    private static int? GetInt32(JsonElement element, string propertyName)
    {
        if (!TryGetPropertyIgnoreCase(element, propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String &&
            int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool? GetBoolean(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!TryGetPropertyIgnoreCase(element, propertyName, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.True)
            {
                return true;
            }

            if (value.ValueKind == JsonValueKind.False)
            {
                return false;
            }

            if (value.ValueKind == JsonValueKind.String &&
                bool.TryParse(value.GetString(), out var parsed))
            {
                return parsed;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number != 0;
            }
        }

        return null;
    }

    private static string ExtractError(JsonElement root)
    {
        if (!TryGetPropertyIgnoreCase(root, "error", out var error))
        {
            return string.Empty;
        }

        if (error.ValueKind == JsonValueKind.Object)
        {
            return GetString(error, "message", "error", "detail", "description") ?? error.GetRawText();
        }

        return GetString(error) ?? string.Empty;
    }

    private static string TryExtractContentText(JsonElement root)
    {
        if (!TryGetPropertyIgnoreCase(root, "content", out var content))
        {
            return string.Empty;
        }

        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() ?? string.Empty;
        }

        if (content.ValueKind == JsonValueKind.Array)
        {
            return ExtractTextFromContentArray(content);
        }

        return string.Empty;
    }

    private static string ExtractTextFromContentArray(JsonElement contentArray)
    {
        if (contentArray.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var item in contentArray.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var value = item.GetString();
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(value);
                continue;
            }

            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var text = GetString(item, "text");
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(text);
        }

        return builder.ToString();
    }

    private static bool IsJsonObjectCandidate(string value)
    {
        return value.Length >= 2 && value[0] == '{' && value[^1] == '}';
    }

    private static bool IsToolHint(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("tool", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("function_call", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("tool_use", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeText(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }

    protected enum RuntimeExecutionMode
    {
        Default = 0,
        Plan = 1,
        Review = 2
    }

    private sealed class ParseState(RuntimeExecutionMode mode)
    {
        public RuntimeExecutionMode Mode { get; } = mode;
        public int Sequence { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
        public string StopReason { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public bool FinalResultReceived { get; set; }
        public int ToolLifecycleCount { get; set; }
        public StringBuilder AssistantBuilder { get; } = new();
        public StringBuilder StdoutBuilder { get; } = new();
        public StringBuilder StderrBuilder { get; } = new();
        public List<HarnessAction> Actions { get; } = [];
        public Dictionary<string, double> Metrics { get; } = [];
        public List<NormalizedStreamEvent> NormalizedEvents { get; } = [];
        public HashSet<string> EventTypes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ToolCallIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> ToolNames { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<int, ContentBlockState> ContentBlocks { get; } = [];
    }

    private sealed class ContentBlockState
    {
        public string BlockType { get; init; } = string.Empty;
        public string ToolName { get; init; } = string.Empty;
        public string ToolCallId { get; init; } = string.Empty;
    }

    private sealed record NormalizedStreamEvent(
        int Sequence,
        string Type,
        string Category,
        string Message,
        bool IsToolRelated,
        IReadOnlyDictionary<string, string> Metadata);
}
