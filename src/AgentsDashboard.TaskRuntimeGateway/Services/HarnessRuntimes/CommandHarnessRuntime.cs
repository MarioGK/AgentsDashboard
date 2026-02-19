using System.Text;
using System.Text.Json;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.TaskRuntimeGateway.Configuration;
using CliWrap;
using CliWrap.Buffered;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.TaskRuntimeGateway.Services.HarnessRuntimes;

public sealed class CommandHarnessRuntime(
    IOptions<TaskRuntimeOptions> options,
    IDockerContainerService dockerService,
    SecretRedactor secretRedactor,
    ILogger<CommandHarnessRuntime> logger) : IHarnessRuntime
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public string Name => "command";

    public async Task<HarnessRuntimeResult> RunAsync(HarnessRunRequest request, IHarnessEventSink sink, CancellationToken ct)
    {
        if (request.UseDocker)
        {
            return await ExecuteViaDockerAsync(request, sink, ct);
        }

        return await ExecuteDirectAsync(request, sink, ct);
    }

    private async Task<HarnessRuntimeResult> ExecuteViaDockerAsync(
        HarnessRunRequest request,
        IHarnessEventSink sink,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Command))
        {
            return new HarnessRuntimeResult
            {
                Structured = false,
                ExitCode = 1,
                Envelope = new HarnessResultEnvelope
                {
                    RunId = request.RunId,
                    TaskId = request.TaskId,
                    Status = "failed",
                    Summary = "Task command is required",
                    Error = "Command runtime requires a non-empty command",
                },
            };
        }

        var image = ResolveImage(request.Harness);
        if (!IsImageAllowed(image))
        {
            return new HarnessRuntimeResult
            {
                Structured = false,
                ExitCode = 1,
                Envelope = new HarnessResultEnvelope
                {
                    RunId = request.RunId,
                    TaskId = request.TaskId,
                    Status = "failed",
                    Summary = "Image not allowed",
                    Error = $"Image '{image}' is not in the configured allowlist.",
                },
            };
        }

        var env = new Dictionary<string, string>(request.Environment, StringComparer.OrdinalIgnoreCase)
        {
            ["PROMPT"] = request.Prompt.Replace("\n", " "),
            ["HARNESS"] = request.Harness,
        };

        var containerId = await dockerService.CreateContainerAsync(
            image,
            ["sh", "-lc", request.Command],
            env,
            request.ContainerLabels,
            string.IsNullOrWhiteSpace(request.WorkspacePath) ? null : request.WorkspacePath,
            string.IsNullOrWhiteSpace(request.ArtifactsHostPath) ? null : request.ArtifactsHostPath,
            request.CpuLimit,
            request.MemoryLimit,
            request.NetworkDisabled,
            request.ReadOnlyRootFs,
            ct);

        try
        {
            await dockerService.StartAsync(containerId, ct);

            var logBuilder = new StringBuilder();
            var logStreamingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var logStreamingTask = Task.Run(async () =>
            {
                try
                {
                    await dockerService.StreamLogsAsync(
                        containerId,
                        async (chunk, streamCt) =>
                        {
                            logBuilder.Append(chunk);
                            var redactedChunk = secretRedactor.Redact(chunk, request.Environment);
                            await sink.PublishAsync(
                                new HarnessRuntimeEvent(HarnessRuntimeEventType.Log, redactedChunk),
                                streamCt);
                        },
                        logStreamingCts.Token);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Log streaming failed for command runtime container {ContainerId}", containerId[..12]);
                }
            }, CancellationToken.None);

            var exitCode = await dockerService.WaitForExitAsync(containerId, ct);

            logStreamingCts.Cancel();
            try
            {
                await logStreamingTask;
            }
            catch (OperationCanceledException)
            {
            }

            var metrics = await dockerService.GetContainerStatsAsync(containerId, CancellationToken.None);

            var finalLogs = logBuilder.Length > 0
                ? logBuilder.ToString()
                : await dockerService.GetLogsAsync(containerId, CancellationToken.None);

            var redactedLogs = secretRedactor.Redact(finalLogs, request.Environment);
            var envelope = CreateEnvelope((int)exitCode, redactedLogs, string.Empty);
            envelope.RunId = request.RunId;
            envelope.TaskId = request.TaskId;
            envelope.Metadata["runtime"] = Name;

            if (metrics is not null)
            {
                envelope.Metrics["cpuPercent"] = metrics.CpuPercent;
                envelope.Metrics["memoryUsageBytes"] = metrics.MemoryUsageBytes;
                envelope.Metrics["memoryLimitBytes"] = metrics.MemoryLimitBytes;
                envelope.Metrics["memoryPercent"] = metrics.MemoryPercent;
                envelope.Metrics["networkRxBytes"] = metrics.NetworkRxBytes;
                envelope.Metrics["networkTxBytes"] = metrics.NetworkTxBytes;
                envelope.Metrics["blockReadBytes"] = metrics.BlockReadBytes;
                envelope.Metrics["blockWriteBytes"] = metrics.BlockWriteBytes;
            }

            await sink.PublishAsync(
                new HarnessRuntimeEvent(
                    HarnessRuntimeEventType.Completion,
                    envelope.Summary,
                    new Dictionary<string, string> { ["status"] = envelope.Status }),
                ct);

            return new HarnessRuntimeResult
            {
                Structured = false,
                ExitCode = (int)exitCode,
                Envelope = envelope,
            };
        }
        catch
        {
            await dockerService.RemoveAsync(containerId, CancellationToken.None);
            throw;
        }
    }

    private async Task<HarnessRuntimeResult> ExecuteDirectAsync(
        HarnessRunRequest request,
        IHarnessEventSink sink,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Command))
        {
            return new HarnessRuntimeResult
            {
                Structured = false,
                ExitCode = 1,
                Envelope = new HarnessResultEnvelope
                {
                    RunId = request.RunId,
                    TaskId = request.TaskId,
                    Status = "failed",
                    Summary = "Task command is required",
                    Error = "Command runtime requires a non-empty command",
                },
            };
        }

        var stdoutBuf = new StringBuilder();
        var stderrBuf = new StringBuilder();

        var stdoutPipe = PipeTarget.Merge(
            PipeTarget.ToStringBuilder(stdoutBuf),
            PipeTarget.Create(async (chunk, streamCt) =>
            {
                var str = chunk.ToString() ?? string.Empty;
                var redacted = secretRedactor.Redact(str, request.Environment);
                await sink.PublishAsync(
                    new HarnessRuntimeEvent(HarnessRuntimeEventType.Log, redacted),
                    streamCt);
            }));

        var stderrPipe = PipeTarget.Merge(
            PipeTarget.ToStringBuilder(stderrBuf),
            PipeTarget.Create(async (chunk, streamCt) =>
            {
                var str = chunk.ToString() ?? string.Empty;
                var redacted = secretRedactor.Redact(str, request.Environment);
                await sink.PublishAsync(
                    new HarnessRuntimeEvent(HarnessRuntimeEventType.Diagnostic, redacted),
                    streamCt);
            }));

        var cmd = Cli.Wrap("sh")
            .WithArguments(["-lc", request.Command])
            .WithValidation(CommandResultValidation.None)
            .WithStandardOutputPipe(stdoutPipe)
            .WithStandardErrorPipe(stderrPipe);

        if (request.Environment.Count > 0)
        {
            cmd = cmd.WithEnvironmentVariables(env =>
            {
                foreach (var (key, value) in request.Environment)
                {
                    env.Set(key, value);
                }
            });
        }

        var result = await cmd.ExecuteAsync(ct);

        var stdout = stdoutBuf.ToString();
        var stderr = stderrBuf.ToString();
        var redactedStdout = secretRedactor.Redact(stdout, request.Environment);
        var redactedStderr = secretRedactor.Redact(stderr, request.Environment);
        var envelope = CreateEnvelope(result.ExitCode, redactedStdout, redactedStderr);
        envelope.RunId = request.RunId;
        envelope.TaskId = request.TaskId;
        envelope.Metadata["runtime"] = Name;

        await sink.PublishAsync(
            new HarnessRuntimeEvent(
                HarnessRuntimeEventType.Completion,
                envelope.Summary,
                new Dictionary<string, string> { ["status"] = envelope.Status }),
            ct);

        return new HarnessRuntimeResult
        {
            Structured = false,
            ExitCode = result.ExitCode,
            Envelope = envelope,
        };
    }

    private string ResolveImage(string harness)
    {
        if (options.Value.HarnessImages.TryGetValue(harness, out var image))
        {
            return image;
        }

        return options.Value.DefaultImage;
    }

    private bool IsImageAllowed(string image)
    {
        var allowedImages = options.Value.AllowedImages;
        if (allowedImages.Count == 0)
        {
            return true;
        }

        foreach (var pattern in allowedImages)
        {
            if (pattern.EndsWith("*", StringComparison.Ordinal))
            {
                if (image.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (string.Equals(image, pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static HarnessResultEnvelope CreateEnvelope(int exitCode, string stdout, string stderr)
    {
        if (TryParseEnvelope(stdout, out var parsed))
        {
            return parsed;
        }

        return new HarnessResultEnvelope
        {
            Status = exitCode == 0 ? "succeeded" : "failed",
            Summary = exitCode == 0 ? "Task completed" : "Task failed",
            Error = stderr,
            Metadata = new Dictionary<string, string>
            {
                ["stdout"] = stdout.Length > 5000 ? stdout[..5000] : stdout,
                ["stderr"] = stderr.Length > 5000 ? stderr[..5000] : stderr,
                ["exitCode"] = exitCode.ToString(),
            }
        };
    }

    private static bool TryParseEnvelope(string output, out HarnessResultEnvelope envelope)
    {
        try
        {
            envelope = JsonSerializer.Deserialize<HarnessResultEnvelope>(output, s_jsonOptions) ?? new HarnessResultEnvelope();
            return !string.IsNullOrWhiteSpace(envelope.Status) && envelope.Status != "unknown";
        }
        catch
        {
            envelope = new HarnessResultEnvelope();
            return false;
        }
    }
}
