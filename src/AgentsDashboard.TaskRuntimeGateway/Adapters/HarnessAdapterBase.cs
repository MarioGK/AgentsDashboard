using System.Diagnostics;
using System.Text.Json;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.TaskRuntime;
using AgentsDashboard.TaskRuntimeGateway.Configuration;
using AgentsDashboard.TaskRuntimeGateway.Services;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.TaskRuntimeGateway.Adapters;

public abstract class HarnessAdapterBase(
    IOptions<TaskRuntimeOptions> options,
    SecretRedactor secretRedactor,
    ILogger logger) : IHarnessAdapter
{
    public abstract string HarnessName { get; }

    public virtual HarnessExecutionContext PrepareContext(DispatchJobRequest request)
    {
        var image = ResolveImage(request.HarnessType);

        return new HarnessExecutionContext
        {
            RunId = request.RunId,
            Harness = request.HarnessType,
            Prompt = request.Instruction,
            Command = request.CustomArgs ?? string.Empty,
            Image = image,
            WorkspacePath = request.WorkingDirectory ?? string.Empty,
            GitUrl = request.CloneUrl,
            ArtifactsHostPath = System.IO.Path.Combine(options.Value.ArtifactStoragePath, request.RunId),
            Env = request.EnvironmentVars ?? new Dictionary<string, string>(),
            ContainerLabels = request.ContainerLabels ?? new Dictionary<string, string>(),
            TimeoutSeconds = request.TimeoutSeconds > 0 ? request.TimeoutSeconds : options.Value.DefaultTimeoutSeconds,
            Attempt = request.Attempt,
            CpuLimit = request.SandboxProfileCpuLimit is > 0 ? request.SandboxProfileCpuLimit.Value : 1.5,
            MemoryLimit = request.SandboxProfileMemoryLimit is > 0 ? $"{request.SandboxProfileMemoryLimit.Value / (1024 * 1024)}m" : "2g",
            NetworkDisabled = request.SandboxProfileNetworkDisabled,
            ReadOnlyRootFs = request.SandboxProfileReadOnlyRootFs
        };
    }

    public virtual HarnessCommand BuildCommand(HarnessExecutionContext context)
    {
        var dockerArgs = new List<string>
        {
            "run", "--rm",
            "--cpus", context.CpuLimit.ToString("F1"),
            "--memory", context.MemoryLimit,
        };

        if (context.NetworkDisabled)
        {
            dockerArgs.Add("--network");
            dockerArgs.Add("none");
        }

        if (context.ReadOnlyRootFs)
        {
            dockerArgs.Add("--read-only");
        }

        dockerArgs.AddRange(new[]
        {
            "-e", $"PROMPT={EscapeEnv(context.Prompt)}",
            "-e", $"HARNESS={EscapeEnv(context.Harness)}",
        });

        foreach (var (key, value) in context.ContainerLabels)
        {
            dockerArgs.Add("--label");
            dockerArgs.Add($"{key}={value}");
        }

        foreach (var (key, value) in context.Env)
        {
            dockerArgs.Add("-e");
            dockerArgs.Add($"{key}={EscapeEnv(value)}");
        }

        AddHarnessSpecificArguments(context, dockerArgs);

        dockerArgs.Add(context.Image);
        dockerArgs.Add("sh");
        dockerArgs.Add("-lc");
        dockerArgs.Add(context.Command);

        return new HarnessCommand
        {
            FileName = "docker",
            Arguments = dockerArgs,
            Environment = new Dictionary<string, string>(context.Env),
            UseShellExecute = false
        };
    }

    protected virtual void AddHarnessSpecificArguments(HarnessExecutionContext context, List<string> args)
    {
    }

    public virtual async Task<HarnessResultEnvelope> ExecuteAsync(
        HarnessExecutionContext context,
        HarnessCommand command,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(context.TimeoutSeconds));

        try
        {
            var result = await RunProcessAsync(command, timeoutCts.Token);

            var redactedStdout = secretRedactor.Redact(result.stdout, context.Env);
            var redactedStderr = secretRedactor.Redact(result.stderr, context.Env);

            var envelope = ParseEnvelope(redactedStdout, redactedStderr, result.exitCode);

            await PostExecuteAsync(context, envelope, cancellationToken);

            return envelope;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new HarnessResultEnvelope
            {
                Status = "failed",
                Summary = "Run timed out",
                Error = $"Execution exceeded {context.TimeoutSeconds} seconds"
            };
        }
    }

    protected virtual Task PostExecuteAsync(
        HarnessExecutionContext context,
        HarnessResultEnvelope envelope,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public virtual HarnessResultEnvelope ParseEnvelope(string stdout, string stderr, int exitCode)
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
                ["stdout"] = Truncate(stdout, 5000),
                ["stderr"] = Truncate(stderr, 5000),
                ["exitCode"] = exitCode.ToString(),
            }
        };
    }

    public virtual IReadOnlyList<HarnessArtifact> MapArtifacts(HarnessResultEnvelope envelope)
    {
        var artifacts = new List<HarnessArtifact>();

        if (envelope.Artifacts is null || envelope.Artifacts.Count == 0)
            return artifacts;

        foreach (var artifactPath in envelope.Artifacts)
        {
            if (string.IsNullOrWhiteSpace(artifactPath))
                continue;

            artifacts.Add(new HarnessArtifact
            {
                Name = Path.GetFileName(artifactPath),
                Path = artifactPath,
                Type = DetermineArtifactType(artifactPath)
            });
        }

        return artifacts;
    }

    public virtual FailureClassification ClassifyFailure(HarnessResultEnvelope envelope)
    {
        if (string.Equals(envelope.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
            return FailureClassification.Success();

        var error = string.IsNullOrEmpty(envelope.Error) ? (envelope.Summary ?? string.Empty) : envelope.Error;
        var lowerError = error.ToLowerInvariant();

        return ClassifyByErrorPatterns(lowerError);
    }

    protected virtual FailureClassification ClassifyByErrorPatterns(string lowerError)
    {
        if (ContainsAny(lowerError, "unauthorized", "invalid api key", "authentication", "auth failed", "401"))
            return FailureClassification.FromClass(FailureClass.AuthenticationError, "Authentication failed", false, 0, "Check API credentials");

        if (ContainsAny(lowerError, "rate limit", "too many requests", "429", "quota exceeded"))
            return FailureClassification.FromClass(FailureClass.RateLimitExceeded, "Rate limit exceeded", true, 60, "Wait before retrying", "Consider reducing request frequency");

        if (ContainsAny(lowerError, "timeout", "timed out", "deadline exceeded"))
            return FailureClassification.FromClass(FailureClass.Timeout, "Operation timed out", true, 30, "Increase timeout", "Check for slow operations");

        if (ContainsAny(lowerError, "out of memory", "oom", "memory exhausted", "resource exhausted"))
            return FailureClassification.FromClass(FailureClass.ResourceExhausted, "Resources exhausted", true, 60, "Reduce memory usage", "Split into smaller tasks");

        if (ContainsAny(lowerError, "invalid", "malformed", "bad request", "400", "validation"))
            return FailureClassification.FromClass(FailureClass.InvalidInput, "Invalid input", false, 0, "Check input format", "Validate parameters");

        if (ContainsAny(lowerError, "not found", "404", "does not exist"))
            return FailureClassification.FromClass(FailureClass.NotFound, "Resource not found", false, 0, "Verify resource exists");

        if (ContainsAny(lowerError, "permission denied", "forbidden", "403", "access denied"))
            return FailureClassification.FromClass(FailureClass.PermissionDenied, "Permission denied", false, 0, "Check permissions", "Verify access rights");

        if (ContainsAny(lowerError, "network", "connection", "dns", "socket", "unreachable"))
            return FailureClassification.FromClass(FailureClass.NetworkError, "Network error", true, 30, "Check network connectivity");

        if (ContainsAny(lowerError, "config", "configuration", "missing", "not configured"))
            return FailureClassification.FromClass(FailureClass.ConfigurationError, "Configuration error", false, 0, "Check configuration");

        return FailureClassification.FromClass(FailureClass.Unknown, "Unknown error", true, 10, "Review error details");
    }

    protected static bool ContainsAny(string haystack, params string[] needles)
    {
        foreach (var needle in needles)
        {
            if (haystack.Contains(needle))
                return true;
        }
        return false;
    }

    protected string ResolveImage(string harness)
    {
        if (options.Value.HarnessImages.TryGetValue(harness, out var image))
            return image;

        return options.Value.DefaultImage;
    }

    protected static string EscapeEnv(string input) => input.Replace("\n", " ");

    protected static string Truncate(string input, int maxLength) =>
        input.Length > maxLength ? input[..maxLength] : input;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    protected static bool TryParseEnvelope(string output, out HarnessResultEnvelope envelope)
    {
        try
        {
            envelope = JsonSerializer.Deserialize<HarnessResultEnvelope>(output, s_jsonOptions) ?? new HarnessResultEnvelope();
            return !string.IsNullOrWhiteSpace(envelope.Status);
        }
        catch
        {
            envelope = new HarnessResultEnvelope();
            return false;
        }
    }

    protected static string DetermineArtifactType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".md" => "markdown",
            ".json" => "json",
            ".yaml" or ".yml" => "yaml",
            ".txt" => "text",
            ".log" => "log",
            ".diff" or ".patch" => "diff",
            ".cs" => "csharp",
            ".js" or ".ts" => "javascript",
            ".py" => "python",
            ".go" => "go",
            ".rs" => "rust",
            _ => "file"
        };
    }

    private async Task<(int exitCode, string stdout, string stderr)> RunProcessAsync(
        HarnessCommand command,
        CancellationToken cancellationToken)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = command.FileName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = command.UseShellExecute,
                WorkingDirectory = command.WorkingDirectory ?? string.Empty
            }
        };

        foreach (var arg in command.Arguments)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

        foreach (var (key, value) in command.Environment)
        {
            process.StartInfo.Environment[key] = value;
        }

        logger.ZLogInformation("Executing harness {Harness} for run {RunId}", HarnessName, process.StartInfo.Environment.TryGetValue("RUN_ID", out var runId) ? runId ?? "unknown" : "unknown");

        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }
}
