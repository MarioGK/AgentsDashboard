using System.Collections.Concurrent;
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
    IDockerContainerService dockerService,
    IArtifactExtractor artifactExtractor,
    ILogger<HarnessExecutor> logger) : IHarnessExecutor
{
    private const string WorkspacesRootPath = "/workspaces/repos";
    private const string MainBranch = "main";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> s_taskGitLocks = new(StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public async Task<HarnessResultEnvelope> ExecuteAsync(
        QueuedJob job,
        Func<string, CancellationToken, Task>? onLogChunk,
        CancellationToken cancellationToken)
    {
        var request = job.Request;
        var command = request.CustomArgs ?? string.Empty;

        if (string.IsNullOrWhiteSpace(command))
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
        var adapter = adapterFactory.Create(request.HarnessType);
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

        WorkspaceContext? workspaceContext = null;
        string? workspaceHostPath = null;
        var gitLock = GetTaskLock(request.RepositoryId, request.TaskId);
        var gitLockAcquired = false;

        if (!string.IsNullOrWhiteSpace(request.CloneUrl))
        {
            try
            {
                await gitLock.WaitAsync(cancellationToken);
                gitLockAcquired = true;

                workspaceContext = await PrepareWorkspaceAsync(request, cancellationToken);
                workspaceHostPath = workspaceContext.WorkspacePath;
            }
            catch (Exception ex)
            {
                return new HarnessResultEnvelope
                {
                    RunId = request.RunId,
                    TaskId = request.TaskId,
                    Status = "failed",
                    Summary = "Workspace preparation failed",
                    Error = ex.Message,
                };
            }
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
                context.ArtifactsHostPath,
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
                                    var redactedChunk = secretRedactor.Redact(chunk, request.EnvironmentVars);
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

                var metrics = await dockerService.GetContainerStatsAsync(containerId, CancellationToken.None);

                var finalLogs = logBuilder.Length > 0
                    ? logBuilder.ToString()
                    : await dockerService.GetLogsAsync(containerId, CancellationToken.None);

                var redactedLogs = secretRedactor.Redact(finalLogs, request.EnvironmentVars);
                var envelope = CreateEnvelope((int)exitCode, redactedLogs, string.Empty);
                envelope.RunId = request.RunId;
                envelope.TaskId = request.TaskId;

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

                if (!ValidateEnvelope(envelope))
                {
                    envelope.Status = "failed";
                    envelope.Error = string.IsNullOrEmpty(envelope.Error)
                        ? "Envelope validation failed: missing required fields (status, summary)"
                        : envelope.Error;
                }

                if (workspaceContext is not null)
                {
                    await FinalizeWorkspaceAfterRunAsync(request, workspaceContext, envelope, cancellationToken);
                }

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
                        MaxArtifacts: request.ArtifactPolicyMaxArtifacts is > 0 ? request.ArtifactPolicyMaxArtifacts.Value : 50,
                        MaxTotalSizeBytes: request.ArtifactPolicyMaxTotalSizeBytes is > 0 ? request.ArtifactPolicyMaxTotalSizeBytes.Value : 104_857_600);

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
            if (gitLockAcquired)
            {
                gitLock.Release();
            }
        }
    }

    private async Task<WorkspaceContext> PrepareWorkspaceAsync(DispatchJobRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CloneUrl))
        {
            throw new InvalidOperationException("Clone URL is required for workspace preparation.");
        }

        var repositoryPath = Path.Combine(WorkspacesRootPath, ToPathSegment(request.RepositoryId));
        var tasksPath = Path.Combine(repositoryPath, "tasks");
        var workspacePath = Path.Combine(tasksPath, ToPathSegment(request.TaskId));
        var mainBranch = ResolveMainBranch(request);

        Directory.CreateDirectory(repositoryPath);
        Directory.CreateDirectory(tasksPath);

        await EnsureWorkspaceReadyAsync(request.CloneUrl, workspacePath, mainBranch, cancellationToken);

        var headBeforeRun = await GetHeadCommitAsync(workspacePath, cancellationToken);
        return new WorkspaceContext(workspacePath, mainBranch, headBeforeRun);
    }

    private async Task EnsureWorkspaceReadyAsync(
        string cloneUrl,
        string workspacePath,
        string mainBranch,
        CancellationToken cancellationToken)
    {
        var gitDirectory = Path.Combine(workspacePath, ".git");
        if (!Directory.Exists(gitDirectory))
        {
            if (Directory.Exists(workspacePath))
            {
                Directory.Delete(workspacePath, true);
            }

            var cloneResult = await ExecuteGitAsync(["clone", cloneUrl, workspacePath], cancellationToken);
            if (cloneResult.ExitCode != 0)
            {
                throw new InvalidOperationException(BuildGitFailureMessage("clone task workspace", cloneResult));
            }
        }

        await ExecuteGitOrThrowInPathAsync(
            workspacePath,
            ["remote", "set-url", "origin", cloneUrl],
            "set workspace origin URL",
            cancellationToken);

        await ExecuteGitOrThrowInPathAsync(
            workspacePath,
            ["fetch", "--prune", "origin"],
            "fetch workspace origin",
            cancellationToken);

        await EnsureMainBranchCheckedOutAsync(workspacePath, mainBranch, cancellationToken);

        await ExecuteGitOrThrowInPathAsync(
            workspacePath,
            ["reset", "--hard", $"origin/{mainBranch}"],
            "reset workspace to origin main branch",
            cancellationToken);

        await ExecuteGitOrThrowInPathAsync(
            workspacePath,
            ["clean", "-fd"],
            "clean workspace",
            cancellationToken);
    }

    private async Task FinalizeWorkspaceAfterRunAsync(
        DispatchJobRequest request,
        WorkspaceContext workspaceContext,
        HarnessResultEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(envelope.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
        {
            envelope.Metadata["gitWorkflow"] = "skipped";
            envelope.Metadata["gitWorkflowReason"] = "non-success-run";
            return;
        }

        try
        {
            await EnsureMainBranchCheckedOutAsync(workspaceContext.WorkspacePath, workspaceContext.MainBranch, cancellationToken);

            var hasWorkspaceChanges = await HasWorkspaceChangesAsync(workspaceContext.WorkspacePath, cancellationToken);
            if (!hasWorkspaceChanges)
            {
                MarkRunAsObsolete(envelope, "no-diff");
                return;
            }

            await StageAndCommitWorkspaceChangesAsync(request, workspaceContext, cancellationToken);

            var headAfterRun = await GetHeadCommitAsync(workspaceContext.WorkspacePath, cancellationToken);
            if (string.Equals(workspaceContext.HeadBeforeRun, headAfterRun, StringComparison.Ordinal))
            {
                MarkRunAsObsolete(envelope, "no-diff");
                return;
            }

            await ExecuteGitOrThrowInPathAsync(
                workspaceContext.WorkspacePath,
                ["push", "origin", workspaceContext.MainBranch],
                "push main branch to origin",
                cancellationToken);

            envelope.Metadata["gitWorkflow"] = "main-pushed";
            envelope.Metadata["gitMainBranch"] = workspaceContext.MainBranch;
        }
        catch (Exception ex)
        {
            envelope.Status = "failed";
            envelope.Summary = "Git commit/push failed";
            envelope.Error = ex.Message;
            envelope.Metadata["gitWorkflow"] = "failed";
            envelope.Metadata["gitFailure"] = ex.Message;
        }
    }

    private async Task StageAndCommitWorkspaceChangesAsync(
        DispatchJobRequest request,
        WorkspaceContext workspaceContext,
        CancellationToken cancellationToken)
    {
        await ExecuteGitOrThrowInPathAsync(
            workspaceContext.WorkspacePath,
            ["add", "-A"],
            "stage workspace changes",
            cancellationToken);

        await EnsureCommitIdentityAsync(request, workspaceContext.WorkspacePath, cancellationToken);

        var commitResult = await ExecuteGitInPathAsync(
            workspaceContext.WorkspacePath,
            ["commit", "-m", BuildMainBranchCommitMessage(request)],
            cancellationToken);

        if (commitResult.ExitCode != 0 && !IsNothingToCommit(commitResult))
        {
            throw new InvalidOperationException(BuildGitFailureMessage("commit workspace changes", commitResult));
        }
    }

    private async Task EnsureMainBranchCheckedOutAsync(string workspacePath, string mainBranch, CancellationToken cancellationToken)
    {
        var checkoutResult = await ExecuteGitInPathAsync(workspacePath, ["checkout", mainBranch], cancellationToken);
        if (checkoutResult.ExitCode == 0)
        {
            return;
        }

        await ExecuteGitOrThrowInPathAsync(
            workspacePath,
            ["checkout", "-B", mainBranch, $"origin/{mainBranch}"],
            "checkout or create main branch",
            cancellationToken);
    }

    private async Task EnsureCommitIdentityAsync(
        DispatchJobRequest request,
        string worktreePath,
        CancellationToken cancellationToken)
    {
        var authorName = ResolveEnvValue(request.EnvironmentVars, "GIT_COMMITTER_NAME")
            ?? ResolveEnvValue(request.EnvironmentVars, "GIT_AUTHOR_NAME")
            ?? "AgentsDashboard Bot";

        var authorEmail = ResolveEnvValue(request.EnvironmentVars, "GIT_COMMITTER_EMAIL")
            ?? ResolveEnvValue(request.EnvironmentVars, "GIT_AUTHOR_EMAIL")
            ?? "agentsdashboard-bot@local";

        await ExecuteGitOrThrowInPathAsync(
            worktreePath,
            ["config", "user.name", authorName],
            "configure git user.name",
            cancellationToken);

        await ExecuteGitOrThrowInPathAsync(
            worktreePath,
            ["config", "user.email", authorEmail],
            "configure git user.email",
            cancellationToken);
    }

    private static string? ResolveEnvValue(Dictionary<string, string>? envVars, string key)
    {
        if (envVars is null)
        {
            return null;
        }

        return envVars.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    private async Task<string> GetHeadCommitAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var result = await ExecuteGitInPathAsync(workspacePath, ["rev-parse", "HEAD"], cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(BuildGitFailureMessage("resolve HEAD commit", result));
        }

        return result.StandardOutput.Trim();
    }

    private async Task<bool> HasWorkspaceChangesAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var result = await ExecuteGitInPathAsync(workspacePath, ["status", "--porcelain"], cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(BuildGitFailureMessage("check workspace changes", result));
        }

        return !string.IsNullOrWhiteSpace(result.StandardOutput);
    }

    private async Task ExecuteGitOrThrowInPathAsync(
        string workspacePath,
        IReadOnlyList<string> arguments,
        string operation,
        CancellationToken cancellationToken)
    {
        var result = await ExecuteGitInPathAsync(workspacePath, arguments, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(BuildGitFailureMessage(operation, result));
        }
    }

    private async Task<BufferedCommandResult> ExecuteGitInPathAsync(
        string workspacePath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var fullArguments = new List<string>(arguments.Count + 2)
        {
            "-C",
            workspacePath,
        };

        fullArguments.AddRange(arguments);
        return await ExecuteGitAsync(fullArguments, cancellationToken);
    }

    private static async Task<BufferedCommandResult> ExecuteGitAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        return await Cli.Wrap("git")
            .WithArguments(arguments)
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync(cancellationToken);
    }

    private static string BuildGitFailureMessage(string operation, BufferedCommandResult result)
    {
        var details = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;

        details = details?.Trim();
        if (string.IsNullOrWhiteSpace(details))
        {
            details = "unknown git error";
        }

        return $"{operation} failed (exit code {result.ExitCode}): {details}";
    }

    private static bool IsNothingToCommit(BufferedCommandResult result)
    {
        var combined = $"{result.StandardOutput}\n{result.StandardError}";
        return combined.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase) ||
               combined.Contains("no changes added to commit", StringComparison.OrdinalIgnoreCase);
    }

    private static void MarkRunAsObsolete(HarnessResultEnvelope envelope, string reason)
    {
        envelope.Status = "succeeded";
        envelope.Summary = "No changes produced";
        envelope.Error = string.Empty;
        envelope.Metadata["runDisposition"] = "obsolete";
        envelope.Metadata["obsoleteReason"] = reason;
    }

    private static SemaphoreSlim GetTaskLock(string repositoryId, string taskId)
    {
        var lockKey = $"{repositoryId}:{taskId}";
        return s_taskGitLocks.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));
    }

    private static string ToPathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        return value.Trim().Replace('/', '-').Replace('\\', '-');
    }

    private static string ResolveMainBranch(DispatchJobRequest request)
    {
        if (request.EnvironmentVars is not null &&
            request.EnvironmentVars.TryGetValue("DEFAULT_BRANCH", out var envDefaultBranch) &&
            !string.IsNullOrWhiteSpace(envDefaultBranch))
        {
            return envDefaultBranch.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Branch))
        {
            return request.Branch.Trim();
        }

        return MainBranch;
    }

    private static string BuildMainBranchCommitMessage(DispatchJobRequest request)
    {
        return $"agent task {request.TaskId}: run {request.RunId}";
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

    private async Task<HarnessResultEnvelope> ExecuteDirectAsync(
        DispatchJobRequest request,
        Func<string, CancellationToken, Task>? onLogChunk,
        CancellationToken cancellationToken)
    {
        var command = request.CustomArgs ?? string.Empty;

        var stdoutBuf = new StringBuilder();
        var stderrBuf = new StringBuilder();

        var stdoutPipe = onLogChunk is not null
            ? PipeTarget.Merge(
                PipeTarget.ToStringBuilder(stdoutBuf),
                PipeTarget.Create(async (chunk, ct) =>
                {
                    var str = chunk.ToString() ?? string.Empty;
                    stdoutBuf.Append(str);
                    var redacted = secretRedactor.Redact(str, request.EnvironmentVars);
                    await onLogChunk(redacted, ct);
                }))
            : PipeTarget.ToStringBuilder(stdoutBuf);

        var stderrPipe = onLogChunk is not null
            ? PipeTarget.Merge(
                PipeTarget.ToStringBuilder(stderrBuf),
                PipeTarget.Create(async (chunk, ct) =>
                {
                    var str = chunk.ToString() ?? string.Empty;
                    stderrBuf.Append(str);
                    var redacted = secretRedactor.Redact(str, request.EnvironmentVars);
                    await onLogChunk(redacted, ct);
                }))
            : PipeTarget.ToStringBuilder(stderrBuf);

        var cmd = Cli.Wrap("sh")
            .WithArguments(["-lc", command])
            .WithValidation(CommandResultValidation.None)
            .WithStandardOutputPipe(stdoutPipe)
            .WithStandardErrorPipe(stderrPipe);

        if (request.EnvironmentVars is not null && request.EnvironmentVars.Count > 0)
        {
            cmd = cmd.WithEnvironmentVariables(env =>
            {
                foreach (var kv in request.EnvironmentVars)
                    env.Set(kv.Key, kv.Value);
            });
        }

        var result = await cmd.ExecuteAsync(cancellationToken);

        var stdout = stdoutBuf.ToString();
        var stderr = stderrBuf.ToString();
        var redactedStdout = secretRedactor.Redact(stdout, request.EnvironmentVars);
        var redactedStderr = secretRedactor.Redact(stderr, request.EnvironmentVars);
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

    private sealed record WorkspaceContext(
        string WorkspacePath,
        string MainBranch,
        string HeadBeforeRun);
}
