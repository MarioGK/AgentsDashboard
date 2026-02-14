using System.Text;
using System.Text.Json;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.Worker;
using AgentsDashboard.WorkerGateway.Adapters;
using AgentsDashboard.WorkerGateway.Configuration;
using AgentsDashboard.WorkerGateway.Models;
using CliWrap;
using CliWrap.Buffered;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.WorkerGateway.Services;

public sealed class HarnessExecutor(
    IOptions<WorkerOptions> options,
    HarnessAdapterFactory adapterFactory,
    SecretRedactor secretRedactor,
    DockerContainerService dockerService,
    IArtifactExtractor artifactExtractor,
    ILogger<HarnessExecutor> logger)
{
    public async Task<HarnessResultEnvelope> ExecuteAsync(
        QueuedJob job,
        Func<string, CancellationToken, Task>? onLogChunk,
        CancellationToken cancellationToken)
    {
        var request = job.Request;

        if (string.IsNullOrWhiteSpace(request.Command))
        {
            return new HarnessResultEnvelope
            {
                RunId = request.RunId,
                TaskId = request.TaskId,
                Status = "failed",
                Summary = "Task command is required",
                Error = "Dispatch command is empty",
            };
        }

        try
        {
            if (!options.Value.UseDocker)
            {
                return await ExecuteDirectAsync(request, onLogChunk, cancellationToken);
            }

            return await ExecuteViaAdapterAsync(request, onLogChunk, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return new HarnessResultEnvelope
            {
                RunId = request.RunId,
                TaskId = request.TaskId,
                Status = "failed",
                Summary = "Run cancelled or timed out",
                Error = "Execution cancelled or exceeded timeout",
            };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Harness execution failed for run {RunId}", request.RunId);
            return new HarnessResultEnvelope
            {
                RunId = request.RunId,
                TaskId = request.TaskId,
                Status = "failed",
                Summary = "Harness execution crashed",
                Error = ex.Message,
            };
        }
    }

    private async Task<HarnessResultEnvelope> ExecuteViaAdapterAsync(
        DispatchJobRequest request,
        Func<string, CancellationToken, Task>? onLogChunk,
        CancellationToken cancellationToken)
    {
        var adapter = adapterFactory.Create(request.Harness);

        var context = adapter.PrepareContext(request);

        if (!IsImageAllowed(context.Image))
        {
            logger.LogWarning("Image {Image} is not in the allowlist for run {RunId}", context.Image, request.RunId);
            return new HarnessResultEnvelope
            {
                RunId = request.RunId,
                TaskId = request.TaskId,
                Status = "failed",
                Summary = "Image not allowed",
                Error = $"Image '{context.Image}' is not in the configured allowlist.",
            };
        }

        string? workspaceHostPath = null;
        bool cleanupWorkspace = false;

        if (!string.IsNullOrWhiteSpace(request.GitUrl))
        {
            var workspaceResult = await PrepareWorkspaceAsync(request, cancellationToken);
            workspaceHostPath = workspaceResult.Path;
            cleanupWorkspace = workspaceResult.ShouldCleanup;
        }

        try
        {
            var env = new Dictionary<string, string>(context.Env)
            {
                ["PROMPT"] = context.Prompt.Replace("\n", " "),
                ["HARNESS"] = context.Harness,
            };

            var containerId = await dockerService.CreateContainerAsync(
                context.Image,
                ["sh", "-lc", context.Command],
                env,
                context.ContainerLabels,
                workspaceHostPath,
                context.CpuLimit,
                context.MemoryLimit,
                context.NetworkDisabled,
                context.ReadOnlyRootFs,
                cancellationToken);

            try
            {
                await dockerService.StartAsync(containerId, cancellationToken);

                var logBuilder = new StringBuilder();
                var logStreamingCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var logStreamingTask = Task.CompletedTask;

                if (onLogChunk is not null)
                {
                    logStreamingTask = Task.Run(async () =>
                    {
                        try
                        {
                            await dockerService.StreamLogsAsync(
                                containerId,
                                async (chunk, ct) =>
                                {
                                    logBuilder.Append(chunk);
                                    var redactedChunk = secretRedactor.Redact(chunk, request.Env);
                                    await onLogChunk(redactedChunk, ct);
                                },
                                logStreamingCts.Token);
                        }
                        catch (OperationCanceledException)
                        {
                        }
                        catch (Exception ex)
                        {
                            logger.LogWarning(ex, "Log streaming failed for container {ContainerId}", containerId[..12]);
                        }
                    }, CancellationToken.None);
                }

                var exitCode = await dockerService.WaitForExitAsync(containerId, cancellationToken);

                logStreamingCts.Cancel();
                try
                {
                    await logStreamingTask;
                }
                catch (OperationCanceledException)
                {
                }

                var finalLogs = logBuilder.Length > 0
                    ? logBuilder.ToString()
                    : await dockerService.GetLogsAsync(containerId, CancellationToken.None);

                var redactedLogs = secretRedactor.Redact(finalLogs, request.Env);
                var envelope = CreateEnvelope((int)exitCode, redactedLogs, string.Empty);
                envelope.RunId = request.RunId;
                envelope.TaskId = request.TaskId;

                if (!ValidateEnvelope(envelope))
                {
                    envelope.Status = "failed";
                    envelope.Error = string.IsNullOrEmpty(envelope.Error)
                        ? "Envelope validation failed: missing required fields (status, summary)"
                        : envelope.Error;
                }

                await TryCreatePullRequestAsync(request, envelope, cancellationToken);

                var classification = adapter.ClassifyFailure(envelope);
                if (classification.Class != FailureClass.None)
                {
                    envelope.Metadata["failureClass"] = classification.Class.ToString();
                    envelope.Metadata["isRetryable"] = classification.IsRetryable.ToString().ToLowerInvariant();

                    if (classification.SuggestedBackoffSeconds.HasValue)
                        envelope.Metadata["suggestedBackoffSeconds"] = classification.SuggestedBackoffSeconds.Value.ToString();

                    if (classification.RemediationHints.Count > 0)
                        envelope.Metadata["remediationHints"] = string.Join("; ", classification.RemediationHints);
                }

                var artifacts = adapter.MapArtifacts(envelope);
                if (artifacts.Count > 0)
                {
                    envelope.Metadata["artifactCount"] = artifacts.Count.ToString();
                    envelope.Metadata["artifacts"] = string.Join(",", artifacts.Select(a => a.Path));
                }

                if (!string.IsNullOrWhiteSpace(workspaceHostPath) && Directory.Exists(workspaceHostPath))
                {
                    var policy = new ArtifactPolicyConfig(
                        MaxArtifacts: request.ArtifactPolicyMaxArtifacts > 0 ? request.ArtifactPolicyMaxArtifacts : 50,
                        MaxTotalSizeBytes: request.ArtifactPolicyMaxTotalSizeBytes > 0 ? request.ArtifactPolicyMaxTotalSizeBytes : 104_857_600);

                    var extractedArtifacts = await artifactExtractor.ExtractArtifactsAsync(
                        workspaceHostPath,
                        request.RunId,
                        policy,
                        cancellationToken);

                    if (extractedArtifacts.Count > 0)
                    {
                        envelope.Artifacts = extractedArtifacts.Select(a => a.DestinationPath).ToList();
                        envelope.Metadata["extractedArtifactCount"] = extractedArtifacts.Count.ToString();
                        envelope.Metadata["extractedArtifactSize"] = extractedArtifacts.Sum(a => a.SizeBytes).ToString();
                    }
                }

                return envelope;
            }
            catch
            {
                await dockerService.RemoveAsync(containerId, CancellationToken.None);
                throw;
            }
        }
        finally
        {
            if (cleanupWorkspace && !string.IsNullOrWhiteSpace(workspaceHostPath) && Directory.Exists(workspaceHostPath))
            {
                try
                {
                    Directory.Delete(workspaceHostPath, recursive: true);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to cleanup workspace directory {Path}", workspaceHostPath);
                }
            }
        }
    }

    private async Task<(string Path, bool ShouldCleanup)> PrepareWorkspaceAsync(DispatchJobRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.WorkspacePath) && Directory.Exists(request.WorkspacePath))
        {
            return (request.WorkspacePath, false);
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"workspace-{request.RunId}");
        Directory.CreateDirectory(tempPath);

        try
        {
            var result = await Cli.Wrap("git")
                .WithArguments(["clone", "--depth", "1", request.GitUrl, tempPath])
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(cancellationToken);

            if (result.ExitCode != 0)
            {
                logger.LogWarning("Git clone failed for {GitUrl}: {Error}", request.GitUrl, result.StandardError);
                try { Directory.Delete(tempPath, recursive: true); } catch { }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Git clone threw for {GitUrl}", request.GitUrl);
        }

        return (tempPath, true);
    }

    private bool IsImageAllowed(string image)
    {
        var allowedImages = options.Value.AllowedImages;
        if (allowedImages.Count == 0)
            return true;

        foreach (var pattern in allowedImages)
        {
            if (pattern.EndsWith('*'))
            {
                if (image.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (string.Equals(image, pattern, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ValidateEnvelope(HarnessResultEnvelope envelope)
    {
        return !string.IsNullOrWhiteSpace(envelope.Status) && !string.IsNullOrWhiteSpace(envelope.Summary);
    }

    private async Task TryCreatePullRequestAsync(
        DispatchJobRequest request,
        HarnessResultEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(envelope.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
            return;

        if (!request.Env.TryGetValue("AUTO_CREATE_PR", out var autoPr) ||
            !string.Equals(autoPr, "true", StringComparison.OrdinalIgnoreCase))
            return;

        if (!request.Env.TryGetValue("GH_REPO", out var repository) || string.IsNullOrWhiteSpace(repository) ||
            !request.Env.TryGetValue("PR_BRANCH", out var branch) || string.IsNullOrWhiteSpace(branch))
        {
            envelope.Status = "failed";
            envelope.Summary = "GitHub PR automation failed";
            envelope.Error = "AUTO_CREATE_PR is enabled, but GH_REPO/PR_BRANCH are missing.";
            return;
        }

        var title = request.Env.TryGetValue("PR_TITLE", out var prTitle) && !string.IsNullOrWhiteSpace(prTitle)
            ? prTitle
            : $"Agent run {request.RunId[..Math.Min(8, request.RunId.Length)]}";
        var body = request.Env.TryGetValue("PR_BODY", out var prBody) && !string.IsNullOrWhiteSpace(prBody)
            ? prBody
            : $"Automated pull request generated by harness {request.Harness}.";
        var baseBranch = request.Env.TryGetValue("DEFAULT_BRANCH", out var defaultBranch) && !string.IsNullOrWhiteSpace(defaultBranch)
            ? defaultBranch
            : "main";

        try
        {
            var envVars = new Dictionary<string, string?>();
            foreach (var kv in request.Env)
                envVars[kv.Key] = kv.Value;

            var result = await Cli.Wrap("gh")
                .WithArguments(["pr", "create", "--repo", repository, "--head", branch, "--base", baseBranch, "--title", title, "--body", body])
                .WithEnvironmentVariables(env => { foreach (var kv in envVars) env.Set(kv.Key, kv.Value); })
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync(cancellationToken);

            if (result.ExitCode != 0)
            {
                envelope.Status = "failed";
                envelope.Summary = "GitHub PR automation failed";
                envelope.Error = string.IsNullOrWhiteSpace(result.StandardError) ? "gh pr create failed" : result.StandardError;
                return;
            }

            envelope.Metadata["prUrl"] = result.StandardOutput.Trim();
        }
        catch (Exception ex)
        {
            envelope.Status = "failed";
            envelope.Summary = "GitHub PR automation failed";
            envelope.Error = ex.Message;
        }
    }

    private async Task<HarnessResultEnvelope> ExecuteDirectAsync(
        DispatchJobRequest request,
        Func<string, CancellationToken, Task>? onLogChunk,
        CancellationToken cancellationToken)
    {
        var stdoutBuf = new StringBuilder();
        var stderrBuf = new StringBuilder();

        var stdoutPipe = onLogChunk is not null
            ? PipeTarget.Merge(
                PipeTarget.ToStringBuilder(stdoutBuf),
                PipeTarget.Create(async (chunk, ct) =>
                {
                    var str = chunk.ToString();
                    stdoutBuf.Append(str);
                    var redacted = secretRedactor.Redact(str, request.Env);
                    await onLogChunk(redacted, ct);
                }))
            : PipeTarget.ToStringBuilder(stdoutBuf);

        var stderrPipe = onLogChunk is not null
            ? PipeTarget.Merge(
                PipeTarget.ToStringBuilder(stderrBuf),
                PipeTarget.Create(async (chunk, ct) =>
                {
                    var str = chunk.ToString();
                    stderrBuf.Append(str);
                    var redacted = secretRedactor.Redact(str, request.Env);
                    await onLogChunk(redacted, ct);
                }))
            : PipeTarget.ToStringBuilder(stderrBuf);

        var cmd = Cli.Wrap("sh")
            .WithArguments(["-lc", request.Command])
            .WithValidation(CommandResultValidation.None)
            .WithStandardOutputPipe(stdoutPipe)
            .WithStandardErrorPipe(stderrPipe);

        if (request.Env.Count > 0)
        {
            cmd = cmd.WithEnvironmentVariables(env =>
            {
                foreach (var kv in request.Env)
                    env.Set(kv.Key, kv.Value);
            });
        }

        var result = await cmd.ExecuteAsync(cancellationToken);

        var stdout = stdoutBuf.ToString();
        var stderr = stderrBuf.ToString();
        var redactedStdout = secretRedactor.Redact(stdout, request.Env);
        var redactedStderr = secretRedactor.Redact(stderr, request.Env);
        var envelope = CreateEnvelope(result.ExitCode, redactedStdout, redactedStderr);
        envelope.RunId = request.RunId;
        envelope.TaskId = request.TaskId;
        return envelope;
    }

    private static HarnessResultEnvelope CreateEnvelope(int exitCode, string stdout, string stderr)
    {
        if (TryParseEnvelope(stdout, out var parsed))
            return parsed;

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

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

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
