using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AgentsDashboard.Contracts.TaskRuntime;
using AgentsDashboard.TaskRuntime.Configuration;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.TaskRuntime.Services;

public sealed class TaskRuntimeCommandService(
    TaskRuntimeEventBus eventBus,
    IOptions<TaskRuntimeOptions> options,
    ILogger<TaskRuntimeCommandService> logger)
{
    private readonly ConcurrentDictionary<string, RuntimeCommandExecutionState> _commands = new(StringComparer.Ordinal);

    public ValueTask<StartRuntimeCommandResult> StartCommandAsync(StartRuntimeCommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.RunId) ||
            string.IsNullOrWhiteSpace(request.TaskId) ||
            string.IsNullOrWhiteSpace(request.ExecutionToken) ||
            string.IsNullOrWhiteSpace(request.Command))
        {
            return ValueTask.FromResult(new StartRuntimeCommandResult
            {
                Success = false,
                ErrorMessage = "run_id, task_id, execution_token and command are required",
                CommandId = string.Empty,
                AcceptedAt = DateTimeOffset.UtcNow,
            });
        }

        var commandId = Guid.NewGuid().ToString("N");
        var timeoutSeconds = ResolveTimeoutSeconds(request.TimeoutSeconds);
        var maxOutputBytes = ResolveMaxOutputBytes(request.MaxOutputBytes);
        var startedAt = DateTimeOffset.UtcNow;

        var commandState = new RuntimeCommandExecutionState(
            commandId,
            request.RunId.Trim(),
            request.TaskId.Trim(),
            request.ExecutionToken.Trim(),
            startedAt,
            timeoutSeconds,
            maxOutputBytes);

        if (!_commands.TryAdd(commandId, commandState))
        {
            return ValueTask.FromResult(new StartRuntimeCommandResult
            {
                Success = false,
                ErrorMessage = "failed to allocate command id",
                CommandId = string.Empty,
                AcceptedAt = DateTimeOffset.UtcNow,
            });
        }

        commandState.ExecutionTask = Task.Run(
            () => ExecuteCommandAsync(commandState, request),
            CancellationToken.None);

        return ValueTask.FromResult(new StartRuntimeCommandResult
        {
            Success = true,
            ErrorMessage = null,
            CommandId = commandId,
            AcceptedAt = startedAt,
        });
    }

    public ValueTask<CancelRuntimeCommandResult> CancelCommandAsync(CancelRuntimeCommandRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.CommandId))
        {
            return ValueTask.FromResult(new CancelRuntimeCommandResult
            {
                Success = false,
                ErrorMessage = "command_id is required",
                CanceledAt = DateTimeOffset.UtcNow,
            });
        }

        if (!_commands.TryGetValue(request.CommandId.Trim(), out var state))
        {
            return ValueTask.FromResult(new CancelRuntimeCommandResult
            {
                Success = false,
                ErrorMessage = $"Command {request.CommandId} was not found",
                CanceledAt = DateTimeOffset.UtcNow,
            });
        }

        if (!state.TryRequestCancel())
        {
            return ValueTask.FromResult(new CancelRuntimeCommandResult
            {
                Success = false,
                ErrorMessage = $"Command {request.CommandId} is already completed",
                CanceledAt = DateTimeOffset.UtcNow,
            });
        }

        return ValueTask.FromResult(new CancelRuntimeCommandResult
        {
            Success = true,
            ErrorMessage = null,
            CanceledAt = DateTimeOffset.UtcNow,
        });
    }

    public ValueTask<RuntimeCommandStatusResult> GetCommandStatusAsync(GetRuntimeCommandStatusRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.CommandId) ||
            !_commands.TryGetValue(request.CommandId.Trim(), out var state))
        {
            return ValueTask.FromResult(new RuntimeCommandStatusResult
            {
                Found = false,
                CommandId = request.CommandId?.Trim() ?? string.Empty,
            });
        }

        return ValueTask.FromResult(state.CreateSnapshot(found: true));
    }

    private async Task ExecuteCommandAsync(RuntimeCommandExecutionState state, StartRuntimeCommandRequest request)
    {
        var workDir = ResolveWorkingDirectory(request.WorkingDirectory);

        var process = new Process
        {
            StartInfo = BuildProcessStartInfo(request, workDir),
        };

        state.AttachProcess(process);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(state.TimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, state.CancelSource.Token);
        var runtimeToken = linkedCts.Token;

        try
        {
            if (!process.Start())
            {
                state.MarkTerminal(
                    RuntimeCommandStatusValue.Failed,
                    null,
                    DateTimeOffset.UtcNow,
                    "failed to start process");

                await PublishCommandSystemEventAsync(
                    state,
                    "command_failed",
                    "command.failed",
                    "Failed to start process",
                    null,
                    runtimeToken);
                return;
            }

            await PublishCommandSystemEventAsync(
                state,
                "command_started",
                "command.started",
                $"Command started: {request.Command}",
                JsonSerializer.Serialize(new
                {
                    state.CommandId,
                    Command = request.Command,
                    Arguments = request.Arguments ?? [],
                    WorkingDirectory = workDir,
                    state.TimeoutSeconds,
                    state.MaxOutputBytes,
                    EnvironmentKeys = request.EnvironmentVars?.Keys.OrderBy(static x => x, StringComparer.OrdinalIgnoreCase).ToArray() ?? [],
                }),
                runtimeToken);

            var stdoutTask = PumpOutputAsync(state, process.StandardOutput, "stdout", runtimeToken);
            var stderrTask = PumpOutputAsync(state, process.StandardError, "stderr", runtimeToken);

            try
            {
                await process.WaitForExitAsync(runtimeToken);
            }
            catch (OperationCanceledException)
            {
                TryKillProcess(process);
            }

            await Task.WhenAll(stdoutTask, stderrTask);

            var completedAt = DateTimeOffset.UtcNow;
            if (state.CancelRequested)
            {
                state.MarkTerminal(
                    RuntimeCommandStatusValue.Canceled,
                    null,
                    completedAt,
                    null,
                    canceled: true);
            }
            else if (timeoutCts.IsCancellationRequested)
            {
                state.MarkTerminal(
                    RuntimeCommandStatusValue.TimedOut,
                    null,
                    completedAt,
                    "Command timed out",
                    timedOut: true);
            }
            else if (process.ExitCode == 0)
            {
                state.MarkTerminal(
                    RuntimeCommandStatusValue.Completed,
                    process.ExitCode,
                    completedAt,
                    null);
            }
            else
            {
                state.MarkTerminal(
                    RuntimeCommandStatusValue.Failed,
                    process.ExitCode,
                    completedAt,
                    $"Command exited with code {process.ExitCode}");
            }

            var completionSummary = state.Status switch
            {
                RuntimeCommandStatusValue.Completed => "Command completed successfully",
                RuntimeCommandStatusValue.Canceled => "Command canceled",
                RuntimeCommandStatusValue.TimedOut => "Command timed out",
                _ => state.ErrorMessage ?? "Command failed",
            };

            var completionPayload = JsonSerializer.Serialize(new
            {
                state.CommandId,
                state.Status,
                state.ExitCode,
                state.TimedOut,
                state.Canceled,
                state.OutputTruncated,
                state.StandardOutputBytes,
                state.StandardErrorBytes,
                CompletedAt = state.CompletedAt,
                state.ErrorMessage,
            });

            await eventBus.PublishAsync(
                CreateEvent(
                    state.RunId,
                    state.TaskId,
                    state.ExecutionToken,
                    "command_completed",
                    completionSummary,
                    sequence: state.NextSequence(),
                    category: "command.completed",
                    structuredPayloadJson: completionPayload,
                    schemaVersion: "runtime-command-v1",
                    commandId: state.CommandId),
                CancellationToken.None);
        }
        catch (Exception ex)
        {
            state.MarkTerminal(
                RuntimeCommandStatusValue.Failed,
                process.HasExited ? process.ExitCode : null,
                DateTimeOffset.UtcNow,
                ex.Message);

            logger.LogWarning(ex, "Runtime command failed for {CommandId}", state.CommandId);

            await PublishCommandSystemEventAsync(
                state,
                "command_failed",
                "command.failed",
                ex.Message,
                JsonSerializer.Serialize(new
                {
                    state.CommandId,
                    Error = ex.Message,
                }),
                CancellationToken.None);
        }
        finally
        {
            process.Dispose();
        }
    }

    private async Task PumpOutputAsync(
        RuntimeCommandExecutionState state,
        StreamReader reader,
        string streamName,
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

            var byteCount = Encoding.UTF8.GetByteCount(line) + 1;
            var shouldEmit = state.TryRecordOutput(streamName, byteCount);
            if (!shouldEmit)
            {
                continue;
            }

            var payloadJson = JsonSerializer.Serialize(new
            {
                stream = streamName,
                text = line,
                truncated = false,
            });

            await eventBus.PublishAsync(
                CreateEvent(
                    state.RunId,
                    state.TaskId,
                    state.ExecutionToken,
                    "command_output",
                    line,
                    sequence: state.NextSequence(),
                    category: "command.delta",
                    structuredPayloadJson: payloadJson,
                    schemaVersion: "runtime-command-v1",
                    commandId: state.CommandId),
                cancellationToken);
        }
    }

    private Task PublishCommandSystemEventAsync(
        RuntimeCommandExecutionState state,
        string eventType,
        string category,
        string summary,
        string? payloadJson,
        CancellationToken cancellationToken)
    {
        return eventBus.PublishAsync(
            CreateEvent(
                state.RunId,
                state.TaskId,
                state.ExecutionToken,
                eventType,
                summary,
                sequence: state.NextSequence(),
                category: category,
                structuredPayloadJson: payloadJson,
                schemaVersion: "runtime-command-v1",
                commandId: state.CommandId),
            cancellationToken).AsTask();
    }

    private static JobEventMessage CreateEvent(
        string runId,
        string taskId,
        string executionToken,
        string eventType,
        string summary,
        long sequence,
        string category,
        string? structuredPayloadJson,
        string schemaVersion,
        string commandId)
    {
        return new JobEventMessage
        {
            RunId = runId,
            TaskId = taskId,
            ExecutionToken = executionToken,
            EventType = eventType,
            Summary = summary,
            Error = null,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Metadata = null,
            Sequence = sequence,
            Category = category,
            PayloadJson = structuredPayloadJson,
            SchemaVersion = schemaVersion,
            CommandId = commandId,
        };
    }

    private ProcessStartInfo BuildProcessStartInfo(StartRuntimeCommandRequest request, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.Command.Trim(),
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        if (request.Arguments is { Count: > 0 })
        {
            foreach (var argument in request.Arguments.Where(static x => !string.IsNullOrWhiteSpace(x)))
            {
                startInfo.ArgumentList.Add(argument.Trim());
            }
        }

        if (request.EnvironmentVars is { Count: > 0 })
        {
            foreach (var (key, value) in request.EnvironmentVars)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                startInfo.Environment[key.Trim()] = value ?? string.Empty;
            }
        }

        return startInfo;
    }

    private int ResolveTimeoutSeconds(int requestedTimeoutSeconds)
    {
        var configuredDefault = options.Value.CommandDefaultTimeoutSeconds > 0
            ? options.Value.CommandDefaultTimeoutSeconds
            : 600;
        var configuredMax = options.Value.CommandMaxTimeoutSeconds > 0
            ? options.Value.CommandMaxTimeoutSeconds
            : 3600;
        var requested = requestedTimeoutSeconds > 0 ? requestedTimeoutSeconds : configuredDefault;
        return Math.Clamp(requested, 1, configuredMax);
    }

    private int ResolveMaxOutputBytes(int requestedMaxOutputBytes)
    {
        var configuredMax = options.Value.CommandMaxOutputBytes > 0
            ? options.Value.CommandMaxOutputBytes
            : 4_194_304;
        var requested = requestedMaxOutputBytes > 0 ? requestedMaxOutputBytes : configuredMax;
        return Math.Clamp(requested, 4096, configuredMax);
    }

    private static string ResolveWorkingDirectory(string? requestedWorkingDirectory)
    {
        if (string.IsNullOrWhiteSpace(requestedWorkingDirectory))
        {
            return Directory.GetCurrentDirectory();
        }

        var normalized = Path.GetFullPath(requestedWorkingDirectory.Trim());
        if (!Directory.Exists(normalized))
        {
            throw new DirectoryNotFoundException($"Working directory '{normalized}' was not found.");
        }

        return normalized;
    }

    private static void TryKillProcess(Process process)
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

    private sealed class RuntimeCommandExecutionState(
        string commandId,
        string runId,
        string taskId,
        string executionToken,
        DateTimeOffset startedAt,
        int timeoutSeconds,
        int maxOutputBytes)
    {
        private readonly object _syncRoot = new();
        private long _sequence;
        private long _capturedBytes;
        private bool _cancelRequested;
        private Process? _process;

        public string CommandId { get; } = commandId;
        public string RunId { get; } = runId;
        public string TaskId { get; } = taskId;
        public string ExecutionToken { get; } = executionToken;
        public DateTimeOffset StartedAt { get; } = startedAt;
        public int TimeoutSeconds { get; } = timeoutSeconds;
        public int MaxOutputBytes { get; } = maxOutputBytes;
        public CancellationTokenSource CancelSource { get; } = new();
        public Task? ExecutionTask { get; set; }

        public RuntimeCommandStatusValue Status { get; private set; } = RuntimeCommandStatusValue.Running;
        public int? ExitCode { get; private set; }
        public DateTimeOffset? CompletedAt { get; private set; }
        public string? ErrorMessage { get; private set; }
        public bool TimedOut { get; private set; }
        public bool Canceled { get; private set; }
        public bool OutputTruncated { get; private set; }
        public long StandardOutputBytes { get; private set; }
        public long StandardErrorBytes { get; private set; }

        public bool CancelRequested
        {
            get
            {
                lock (_syncRoot)
                {
                    return _cancelRequested;
                }
            }
        }

        public void AttachProcess(Process process)
        {
            lock (_syncRoot)
            {
                _process = process;
            }
        }

        public bool TryRequestCancel()
        {
            lock (_syncRoot)
            {
                if (Status != RuntimeCommandStatusValue.Running)
                {
                    return false;
                }

                _cancelRequested = true;
                CancelSource.Cancel();
                if (_process is { HasExited: false })
                {
                    TryKillProcess(_process);
                }

                return true;
            }
        }

        public bool TryRecordOutput(string streamName, int byteCount)
        {
            lock (_syncRoot)
            {
                if (Status != RuntimeCommandStatusValue.Running)
                {
                    return false;
                }

                if (string.Equals(streamName, "stderr", StringComparison.Ordinal))
                {
                    StandardErrorBytes += byteCount;
                }
                else
                {
                    StandardOutputBytes += byteCount;
                }

                if (OutputTruncated)
                {
                    return false;
                }

                var nextTotal = _capturedBytes + byteCount;
                if (nextTotal > MaxOutputBytes)
                {
                    OutputTruncated = true;
                    return false;
                }

                _capturedBytes = nextTotal;
                return true;
            }
        }

        public long NextSequence()
        {
            return Interlocked.Increment(ref _sequence);
        }

        public void MarkTerminal(
            RuntimeCommandStatusValue status,
            int? exitCode,
            DateTimeOffset completedAt,
            string? errorMessage,
            bool timedOut = false,
            bool canceled = false)
        {
            lock (_syncRoot)
            {
                Status = status;
                ExitCode = exitCode;
                CompletedAt = completedAt;
                ErrorMessage = errorMessage;
                TimedOut = timedOut;
                Canceled = canceled;
            }
        }

        public RuntimeCommandStatusResult CreateSnapshot(bool found)
        {
            lock (_syncRoot)
            {
                return new RuntimeCommandStatusResult
                {
                    Found = found,
                    CommandId = CommandId,
                    RunId = RunId,
                    TaskId = TaskId,
                    ExecutionToken = ExecutionToken,
                    Status = Status,
                    ExitCode = ExitCode,
                    StartedAt = StartedAt,
                    CompletedAt = CompletedAt,
                    ErrorMessage = ErrorMessage,
                    TimedOut = TimedOut,
                    Canceled = Canceled,
                    OutputTruncated = OutputTruncated,
                    StandardOutputBytes = StandardOutputBytes,
                    StandardErrorBytes = StandardErrorBytes,
                };
            }
        }
    }
}
