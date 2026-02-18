using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.TaskRuntime;

namespace AgentsDashboard.TaskRuntimeGateway.Services.HarnessRuntimes;

public sealed class CodexAppServerRuntime(
    SecretRedactor secretRedactor,
    ILogger<CodexAppServerRuntime> logger) : IHarnessRuntime
{
    private const string CodexCommand = "codex";
    private const string CodexListenUri = "stdio://";
    private const string RuntimeMode = "app-server";
    private const string DefaultApprovalPolicy = "on-failure";
    private const string DefaultSandbox = "danger-full-access";

    public string Name => "codex-app-server";

    public async Task<HarnessRuntimeResult> RunAsync(HarnessRunRequest request, IHarnessEventSink sink, CancellationToken ct)
    {
        if (!string.Equals(request.Harness, "codex", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Runtime '{Name}' only supports codex harness.");
        }

        var timeout = request.Timeout > TimeSpan.Zero ? request.Timeout : TimeSpan.FromMinutes(10);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        var runtimeCt = timeoutCts.Token;
        var executionMode = ResolveExecutionMode(request.Mode, request.Environment);

        var workingDirectory = string.IsNullOrWhiteSpace(request.WorkspacePath)
            ? Directory.GetCurrentDirectory()
            : request.WorkspacePath;

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = CodexCommand,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = workingDirectory,
            },
            EnableRaisingEvents = true,
        };

        process.StartInfo.ArgumentList.Add("app-server");
        process.StartInfo.ArgumentList.Add("--listen");
        process.StartInfo.ArgumentList.Add(CodexListenUri);

        foreach (var (key, value) in request.Environment)
        {
            process.StartInfo.Environment[key] = value;
        }

        process.StartInfo.Environment["NO_COLOR"] = "1";

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start codex app-server process.");
        }

        var pendingResponses = new ConcurrentDictionary<string, TaskCompletionSource<JsonElement>>(StringComparer.Ordinal);
        var turnCompletion = new TaskCompletionSource<TurnCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
        var processExit = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

        process.Exited += (_, _) =>
        {
            processExit.TrySetResult(process.ExitCode);
        };

        var stderrLog = new StringBuilder();
        var assistantBuffer = new StringBuilder();
        var reasoningBuffer = new StringBuilder();
        var commandBuffer = new StringBuilder();
        string latestDiff = string.Empty;
        string threadId = string.Empty;
        string turnId = string.Empty;
        var requestId = 0;
        var initialized = false;

        Task stdoutReaderTask = Task.Run(async () =>
        {
            while (!runtimeCt.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(runtimeCt);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (!TryParseJsonLine(line, out var root))
                {
                    var redactedLine = Redact(line, request.Environment);
                    await sink.PublishAsync(
                        new HarnessRuntimeEvent(HarnessRuntimeEventType.Diagnostic, redactedLine),
                        runtimeCt);
                    continue;
                }

                if (TryGetProperty(root, "id", out var idElement) &&
                    TryGetProperty(root, "result", out var resultElement))
                {
                    var id = ToIdKey(idElement);
                    if (pendingResponses.TryRemove(id, out var responseCompletion))
                    {
                        responseCompletion.TrySetResult(resultElement);
                    }
                    continue;
                }

                if (TryGetProperty(root, "id", out var errorIdElement) &&
                    TryGetProperty(root, "error", out var errorElement))
                {
                    var id = ToIdKey(errorIdElement);
                    if (pendingResponses.TryRemove(id, out var responseCompletion))
                    {
                        responseCompletion.TrySetException(new InvalidOperationException(errorElement.GetRawText()));
                    }

                    var errorText = Redact(errorElement.GetRawText(), request.Environment);
                    await sink.PublishAsync(
                        new HarnessRuntimeEvent(HarnessRuntimeEventType.Diagnostic, errorText),
                        runtimeCt);
                    continue;
                }

                if (!TryGetStringProperty(root, "method", out var method) ||
                    !TryGetProperty(root, "params", out var parameters))
                {
                    continue;
                }

                if (string.Equals(method, "turn/started", StringComparison.OrdinalIgnoreCase))
                {
                    if (TryGetNestedString(parameters, ["turn", "id"], out var startedTurnId) &&
                        !string.IsNullOrWhiteSpace(startedTurnId))
                    {
                        turnId = startedTurnId;
                    }
                    continue;
                }

                if (string.Equals(method, "turn/completed", StringComparison.OrdinalIgnoreCase))
                {
                    var completionThreadId = TryGetString(parameters, "threadId");
                    var completionTurnId = TryGetNestedString(parameters, ["turn", "id"], out var completedTurnId)
                        ? completedTurnId
                        : turnId;
                    var status = TryGetNestedString(parameters, ["turn", "status"], out var completedStatus)
                        ? completedStatus
                        : "failed";
                    var errorMessage = TryGetNestedString(parameters, ["turn", "error", "message"], out var completionError)
                        ? completionError
                        : string.Empty;

                    turnCompletion.TrySetResult(new TurnCompletion(
                        completionThreadId ?? threadId,
                        completionTurnId ?? turnId,
                        status,
                        errorMessage));

                    await sink.PublishAsync(
                        new HarnessRuntimeEvent(
                            HarnessRuntimeEventType.Completion,
                            status,
                            new Dictionary<string, string>
                            {
                                ["threadId"] = completionThreadId ?? threadId,
                                ["turnId"] = completionTurnId ?? turnId,
                            }),
                        runtimeCt);
                    continue;
                }

                if (string.Equals(method, "item/reasoning/textDelta", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(method, "item/reasoning/summaryTextDelta", StringComparison.OrdinalIgnoreCase))
                {
                    var delta = TryGetString(parameters, "delta");
                    if (!string.IsNullOrEmpty(delta))
                    {
                        var redacted = Redact(delta, request.Environment);
                        reasoningBuffer.Append(redacted);
                        await sink.PublishAsync(
                            new HarnessRuntimeEvent(HarnessRuntimeEventType.ReasoningDelta, redacted),
                            runtimeCt);
                    }
                    continue;
                }

                if (string.Equals(method, "item/agentMessage/delta", StringComparison.OrdinalIgnoreCase))
                {
                    var delta = TryGetString(parameters, "delta");
                    if (!string.IsNullOrEmpty(delta))
                    {
                        var redacted = Redact(delta, request.Environment);
                        assistantBuffer.Append(redacted);
                        await sink.PublishAsync(
                            new HarnessRuntimeEvent(HarnessRuntimeEventType.AssistantDelta, redacted),
                            runtimeCt);
                    }
                    continue;
                }

                if (string.Equals(method, "item/commandExecution/outputDelta", StringComparison.OrdinalIgnoreCase))
                {
                    var delta = TryGetString(parameters, "delta");
                    if (!string.IsNullOrEmpty(delta))
                    {
                        var redacted = Redact(delta, request.Environment);
                        commandBuffer.Append(redacted);
                        await sink.PublishAsync(
                            new HarnessRuntimeEvent(HarnessRuntimeEventType.CommandOutput, redacted),
                            runtimeCt);
                    }
                    continue;
                }

                if (string.Equals(method, "item/fileChange/outputDelta", StringComparison.OrdinalIgnoreCase))
                {
                    var delta = TryGetString(parameters, "delta");
                    if (!string.IsNullOrEmpty(delta))
                    {
                        var redacted = Redact(delta, request.Environment);
                        latestDiff += redacted;
                        await sink.PublishAsync(
                            new HarnessRuntimeEvent(HarnessRuntimeEventType.DiffUpdate, redacted),
                            runtimeCt);
                    }
                    continue;
                }

                if (string.Equals(method, "turn/diff/updated", StringComparison.OrdinalIgnoreCase))
                {
                    var diff = TryGetString(parameters, "diff");
                    if (!string.IsNullOrEmpty(diff))
                    {
                        var redacted = Redact(diff, request.Environment);
                        latestDiff = redacted;
                        await sink.PublishAsync(
                            new HarnessRuntimeEvent(HarnessRuntimeEventType.DiffUpdate, redacted),
                            runtimeCt);
                    }
                    continue;
                }

                if (string.Equals(method, "error", StringComparison.OrdinalIgnoreCase))
                {
                    var message = TryGetString(parameters, "message");
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        var redacted = Redact(message, request.Environment);
                        await sink.PublishAsync(
                            new HarnessRuntimeEvent(HarnessRuntimeEventType.Diagnostic, redacted),
                            runtimeCt);
                    }
                }
            }
        }, CancellationToken.None);

        Task stderrReaderTask = Task.Run(async () =>
        {
            while (!runtimeCt.IsCancellationRequested)
            {
                var line = await process.StandardError.ReadLineAsync(runtimeCt);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var redacted = Redact(line, request.Environment);
                stderrLog.AppendLine(redacted);

                await sink.PublishAsync(
                    new HarnessRuntimeEvent(HarnessRuntimeEventType.Diagnostic, redacted),
                    runtimeCt);
            }
        }, CancellationToken.None);

        using var cancellationRegistration = runtimeCt.Register(() =>
        {
            TryKill(process);
        });

        try
        {
            await SendRequestAsync(
                process,
                pendingResponses,
                Interlocked.Increment(ref requestId),
                "initialize",
                new
                {
                    clientInfo = new
                    {
                        name = "agents-dashboard-worker",
                        title = "AgentsDashboard WorkerGateway",
                        version = ThisAssemblyVersion,
                    },
                    capabilities = new
                    {
                        experimentalApi = true,
                    },
                },
                runtimeCt);

            initialized = true;

            var threadStartResponse = await SendRequestAsync(
                process,
                pendingResponses,
                Interlocked.Increment(ref requestId),
                "thread/start",
                new
                {
                    cwd = workingDirectory,
                    approvalPolicy = ResolveApprovalPolicy(request.Environment, executionMode),
                    sandbox = ResolveSandboxMode(request.Environment),
                    model = ResolveModel(request.Environment),
                    ephemeral = true,
                    experimentalRawEvents = false,
                },
                runtimeCt);

            if (!TryGetNestedString(threadStartResponse, ["thread", "id"], out threadId) ||
                string.IsNullOrWhiteSpace(threadId))
            {
                throw new InvalidOperationException("thread/start response did not include thread id.");
            }

            var prompt = ApplyModePrompt(request.Prompt, executionMode);
            var useNativeInput = request.PreferNativeMultimodal && HasImageInputParts(request.InputParts);
            var input = BuildTurnInput(request, prompt, useNativeInput);

            JsonElement turnStartResponse;
            try
            {
                turnStartResponse = await SendRequestAsync(
                    process,
                    pendingResponses,
                    Interlocked.Increment(ref requestId),
                    "turn/start",
                    new
                    {
                        threadId,
                        input,
                        cwd = workingDirectory,
                    },
                    runtimeCt);
            }
            catch (Exception ex) when (useNativeInput)
            {
                await sink.PublishAsync(
                    new HarnessRuntimeEvent(
                        HarnessRuntimeEventType.Diagnostic,
                        $"Codex native multimodal input failed; retrying with text fallback. {ex.Message}"),
                    runtimeCt);

                input = BuildTurnInput(request, prompt, useNativeInput: false);
                turnStartResponse = await SendRequestAsync(
                    process,
                    pendingResponses,
                    Interlocked.Increment(ref requestId),
                    "turn/start",
                    new
                    {
                        threadId,
                        input,
                        cwd = workingDirectory,
                    },
                    runtimeCt);
            }

            if (TryGetNestedString(turnStartResponse, ["turn", "id"], out var startedTurnId) &&
                !string.IsNullOrWhiteSpace(startedTurnId))
            {
                turnId = startedTurnId;
            }

            var completion = await WaitForCompletionAsync(turnCompletion.Task, processExit.Task, runtimeCt);
            threadId = string.IsNullOrWhiteSpace(completion.ThreadId) ? threadId : completion.ThreadId;
            turnId = string.IsNullOrWhiteSpace(completion.TurnId) ? turnId : completion.TurnId;

            var succeeded = string.Equals(completion.Status, "completed", StringComparison.OrdinalIgnoreCase);
            var summary = succeeded
                ? "Codex app-server execution completed"
                : "Codex app-server execution failed";
            var error = succeeded
                ? string.Empty
                : BuildFailureError(completion, stderrLog.ToString());

            var envelope = new HarnessResultEnvelope
            {
                RunId = request.RunId,
                TaskId = request.TaskId,
                Status = succeeded ? "succeeded" : "failed",
                Summary = summary,
                Error = error,
                Metadata = new Dictionary<string, string>
                {
                    ["runtime"] = Name,
                    ["runtimeMode"] = RuntimeMode,
                    ["threadId"] = threadId,
                    ["turnId"] = turnId,
                    ["turnStatus"] = completion.Status,
                    ["mode"] = executionMode,
                    ["assistantChars"] = assistantBuffer.Length.ToString(CultureInfo.InvariantCulture),
                    ["reasoningChars"] = reasoningBuffer.Length.ToString(CultureInfo.InvariantCulture),
                    ["commandOutputChars"] = commandBuffer.Length.ToString(CultureInfo.InvariantCulture),
                    ["diffChars"] = latestDiff.Length.ToString(CultureInfo.InvariantCulture),
                },
            };

            if (!string.IsNullOrWhiteSpace(stderrLog.ToString()))
            {
                envelope.Metadata["stderr"] = stderrLog.Length > 5000
                    ? stderrLog.ToString()[..5000]
                    : stderrLog.ToString();
            }

            if (!string.IsNullOrWhiteSpace(latestDiff))
            {
                envelope.Metadata["unifiedDiffPresent"] = "true";
            }

            return new HarnessRuntimeResult
            {
                Structured = true,
                ExitCode = process.HasExited ? process.ExitCode : 0,
                Envelope = envelope,
            };
        }
        finally
        {
            foreach (var (_, responseCompletion) in pendingResponses)
            {
                responseCompletion.TrySetCanceled(runtimeCt);
            }

            if (initialized)
            {
                try
                {
                    await process.StandardInput.FlushAsync(runtimeCt);
                }
                catch
                {
                }
            }

            try
            {
                process.StandardInput.Close();
            }
            catch
            {
            }

            if (!process.HasExited)
            {
                TryKill(process);
            }

            try
            {
                await Task.WhenAll(stdoutReaderTask, stderrReaderTask);
            }
            catch
            {
            }

            process.Dispose();
        }
    }

    private static async Task<JsonElement> SendRequestAsync(
        Process process,
        ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> pendingResponses,
        int requestId,
        string method,
        object parameters,
        CancellationToken ct)
    {
        var id = requestId.ToString(CultureInfo.InvariantCulture);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        pendingResponses[id] = completion;

        var payload = JsonSerializer.Serialize(new
        {
            id = requestId,
            method,
            @params = parameters,
        });

        await process.StandardInput.WriteLineAsync(payload.AsMemory(), ct);
        await process.StandardInput.FlushAsync(ct);

        using var registration = ct.Register(() => completion.TrySetCanceled(ct));
        try
        {
            return await completion.Task;
        }
        finally
        {
            pendingResponses.TryRemove(id, out _);
        }
    }

    private static async Task<TurnCompletion> WaitForCompletionAsync(
        Task<TurnCompletion> completionTask,
        Task<int> processExitTask,
        CancellationToken ct)
    {
        while (true)
        {
            var winner = await Task.WhenAny(completionTask, processExitTask);
            if (winner == completionTask)
            {
                return await completionTask;
            }

            if (completionTask.IsCompleted)
            {
                return await completionTask;
            }

            var exitCode = await processExitTask;
            throw new InvalidOperationException($"codex app-server exited before turn completion (exit code {exitCode}).");
        }
    }

    private static string ResolveApprovalPolicy(IReadOnlyDictionary<string, string> environment, string mode)
    {
        if (environment.TryGetValue("CODEX_APPROVAL_POLICY", out var value) &&
            !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        if (mode is "plan" or "review")
        {
            return "never";
        }

        return DefaultApprovalPolicy;
    }

    private static string ResolveSandboxMode(IReadOnlyDictionary<string, string> environment)
    {
        if (environment.TryGetValue("CODEX_SANDBOX", out var value) &&
            !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        return DefaultSandbox;
    }

    private static string? ResolveModel(IReadOnlyDictionary<string, string> environment)
    {
        if (environment.TryGetValue("CODEX_MODEL", out var codexModel) &&
            !string.IsNullOrWhiteSpace(codexModel))
        {
            return codexModel.Trim();
        }

        if (environment.TryGetValue("HARNESS_MODEL", out var harnessModel) &&
            !string.IsNullOrWhiteSpace(harnessModel))
        {
            return harnessModel.Trim();
        }

        return null;
    }

    private static string ResolveExecutionMode(string mode, IReadOnlyDictionary<string, string> environment)
    {
        if (environment.TryGetValue("HARNESS_MODE", out var harnessMode) &&
            !string.IsNullOrWhiteSpace(harnessMode))
        {
            return NormalizeMode(harnessMode);
        }

        if (environment.TryGetValue("TASK_MODE", out var taskMode) &&
            !string.IsNullOrWhiteSpace(taskMode))
        {
            return NormalizeMode(taskMode);
        }

        if (environment.TryGetValue("RUN_MODE", out var runMode) &&
            !string.IsNullOrWhiteSpace(runMode))
        {
            return NormalizeMode(runMode);
        }

        return NormalizeMode(mode);
    }

    private static string NormalizeMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return "default";
        }

        return mode.Trim().ToLowerInvariant() switch
        {
            "plan" or "planning" => "plan",
            "review" or "audit" or "read-only" or "readonly" => "review",
            _ => "default"
        };
    }

    private static string ApplyModePrompt(string prompt, string mode)
    {
        var trimmedPrompt = prompt?.Trim() ?? string.Empty;
        var header = mode switch
        {
            "plan" => "Execution mode: plan. Do not modify files, create commits, or run mutating commands. Produce a concrete implementation plan with checkpoints.",
            "review" => "Execution mode: review. Do not modify files, create commits, or run mutating commands. Focus on findings by severity with file references and required tests.",
            _ => string.Empty
        };

        if (header.Length == 0)
        {
            return trimmedPrompt;
        }

        if (trimmedPrompt.Length == 0)
        {
            return header;
        }

        return string.Concat(header, "\n\n", trimmedPrompt);
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

    private static object[] BuildTurnInput(HarnessRunRequest request, string prompt, bool useNativeInput)
    {
        if (!useNativeInput || request.InputParts.Count == 0)
        {
            return
            [
                new
                {
                    type = "text",
                    text = prompt,
                    text_elements = Array.Empty<object>(),
                }
            ];
        }

        var items = new List<object>();
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
                    items.Add(new
                    {
                        type = "text",
                        text,
                        text_elements = Array.Empty<object>(),
                    });
                }

                continue;
            }

            if (string.Equals(part.Type, "image", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(part.ImageRef))
            {
                items.Add(new
                {
                    type = "image",
                    image_url = part.ImageRef,
                    mime_type = part.MimeType,
                    alt = part.Alt,
                });
            }
        }

        if (!hasTextPart && !string.IsNullOrWhiteSpace(prompt))
        {
            items.Insert(0, new
            {
                type = "text",
                text = prompt,
                text_elements = Array.Empty<object>(),
            });
        }

        if (items.Count == 0)
        {
            items.Add(new
            {
                type = "text",
                text = prompt,
                text_elements = Array.Empty<object>(),
            });
        }

        return [.. items];
    }

    private static bool TryParseJsonLine(string line, out JsonElement root)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            root = document.RootElement.Clone();
            return true;
        }
        catch
        {
            root = default;
            return false;
        }
    }

    private static string ToIdKey(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.String => element.GetString() ?? string.Empty,
            _ => string.Empty,
        };
    }

    private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement propertyValue)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out propertyValue))
        {
            propertyValue = propertyValue.Clone();
            return true;
        }

        propertyValue = default;
        return false;
    }

    private static bool TryGetStringProperty(JsonElement element, string propertyName, out string value)
    {
        if (TryGetProperty(element, propertyName, out var propertyValue))
        {
            value = propertyValue.ValueKind == JsonValueKind.String
                ? propertyValue.GetString() ?? string.Empty
                : propertyValue.GetRawText();
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        if (!TryGetProperty(element, propertyName, out var propertyValue))
        {
            return null;
        }

        if (propertyValue.ValueKind == JsonValueKind.String)
        {
            return propertyValue.GetString();
        }

        return propertyValue.GetRawText();
    }

    private static bool TryGetNestedString(JsonElement element, IReadOnlyList<string> path, out string value)
    {
        var current = element;
        foreach (var segment in path)
        {
            if (!TryGetProperty(current, segment, out var next))
            {
                value = string.Empty;
                return false;
            }

            current = next;
        }

        if (current.ValueKind == JsonValueKind.Null)
        {
            value = string.Empty;
            return false;
        }

        value = current.ValueKind == JsonValueKind.String
            ? current.GetString() ?? string.Empty
            : current.GetRawText();

        return !string.IsNullOrWhiteSpace(value);
    }

    private static string BuildFailureError(TurnCompletion completion, string stderr)
    {
        if (!string.IsNullOrWhiteSpace(completion.Error))
        {
            return completion.Error;
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            return stderr.Length > 5000 ? stderr[..5000] : stderr;
        }

        return $"Codex turn ended with status '{completion.Status}'.";
    }

    private string Redact(string value, Dictionary<string, string> environment)
    {
        return secretRedactor.Redact(value, environment);
    }

    private void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
        {
            logger.ZLogDebug(ex, "Failed to kill codex app-server process.");
        }
    }

    private static string ThisAssemblyVersion =>
        typeof(CodexAppServerRuntime).Assembly.GetName().Version?.ToString() ?? "1.0.0";

    private sealed record TurnCompletion(string ThreadId, string TurnId, string Status, string Error);
}
