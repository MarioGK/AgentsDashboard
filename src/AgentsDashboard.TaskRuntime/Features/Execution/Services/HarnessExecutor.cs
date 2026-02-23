using System.Collections.Concurrent;
using System.Diagnostics;






using Microsoft.Extensions.Options;

namespace AgentsDashboard.TaskRuntime.Features.Execution.Services;

public sealed partial class HarnessExecutor(
    IOptions<TaskRuntimeOptions> options,
    HarnessAdapterFactory adapterFactory,
    IHarnessRuntimeFactory runtimeFactory,
    IArtifactExtractor artifactExtractor,
    McpRuntimeBootstrapService mcpRuntimeBootstrap,
    ILogger<HarnessExecutor> logger) : IHarnessExecutor
{
    private const string MainBranch = "main";
    private const string RuntimeEventWireMarker = "agentsdashboard.harness-runtime-event.v1";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> s_taskGitLocks = new(StringComparer.OrdinalIgnoreCase);

    public async Task<HarnessResultEnvelope> ExecuteAsync(
        QueuedJob job,
        Func<string, CancellationToken, Task>? onLogChunk,
        CancellationToken cancellationToken)
    {
        var request = job.Request;
        logger.LogInformation(
            "Dispatching harness execution {@Data}",
            new
            {
                request.RunId,
                request.TaskId,
                request.HarnessType,
                request.RepositoryId,
                request.TimeoutSeconds,
                request.PreferNativeMultimodal,
                InputParts = request.InputParts?.Count ?? 0,
                ImageAttachments = request.ImageAttachments?.Count ?? 0,
                HasCustomArgs = !string.IsNullOrWhiteSpace(request.CustomArgs),
                HasCloneUrl = !string.IsNullOrWhiteSpace(request.CloneUrl),
            });

        try
        {
            return await ExecuteViaRuntimeAsync(request, onLogChunk, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning(
                "Harness execution canceled {@Data}",
                new
                {
                    request.RunId,
                    request.TaskId,
                    request.HarnessType,
                    request.TimeoutSeconds,
                });

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
            logger.LogError(
                ex,
                "Harness execution failed {@Data}",
                new
                {
                    request.RunId,
                    request.TaskId,
                    request.HarnessType,
                    request.TimeoutSeconds,
                    Error = ex.Message,
                });
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

    private async Task<HarnessResultEnvelope> ExecuteViaRuntimeAsync(
        DispatchJobRequest request,
        Func<string, CancellationToken, Task>? onLogChunk,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Starting runtime execution path {@Data}",
            new
            {
                request.RunId,
                request.TaskId,
                request.HarnessType,
                request.Mode,
                request.RepositoryId,
                request.PreferNativeMultimodal,
                HasCloneUrl = !string.IsNullOrWhiteSpace(request.CloneUrl),
                request.SandboxProfileNetworkDisabled,
                request.SandboxProfileReadOnlyRootFs,
                request.ArtifactPolicyMaxArtifacts,
                request.ArtifactPolicyMaxTotalSizeBytes,
                request.TimeoutSeconds,
            });

        WorkspaceContext? workspaceContext = null;
        var workspaceHostPath = request.WorkingDirectory ?? string.Empty;
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
                logger.LogInformation(
                    "Workspace prepared for runtime execution {@Data}",
                    new
                    {
                        request.RunId,
                        WorkspacePath = workspaceContext.WorkspacePath,
                        workspaceContext.MainBranch,
                        HeadBeforeRun = workspaceContext.HeadBeforeRun,
                    });
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Workspace preparation failed {@Data}",
                    new
                    {
                        request.RunId,
                        request.TaskId,
                        request.RepositoryId,
                        HasCloneUrl = !string.IsNullOrWhiteSpace(request.CloneUrl),
                    });

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
            var runtimeRequest = BuildRuntimeRequest(request, workspaceHostPath);
            var mcpBootstrap = await mcpRuntimeBootstrap.PrepareAsync(
                request,
                runtimeRequest.WorkspacePath,
                runtimeRequest.Environment,
                cancellationToken);

            if (mcpBootstrap.HasConfig)
            {
                runtimeRequest = runtimeRequest with
                {
                    McpConfigSnapshotJson = mcpBootstrap.EffectiveJson,
                    McpConfigFilePath = mcpBootstrap.ConfigPath
                };
            }

            var runtimeSelection = runtimeFactory.Select(runtimeRequest);
            logger.LogDebug(
                "Harness runtime selected {@Data}",
                new
                {
                    request.RunId,
                    request.TaskId,
                    PrimaryRuntime = runtimeSelection.Primary.Name,
                    FallbackRuntime = runtimeSelection.Fallback?.Name,
                    SelectedRuntimeMode = runtimeSelection.RuntimeMode,
                    HarnessExecutionMode = runtimeRequest.Mode,
                    EnvironmentVarCount = runtimeRequest.Environment.Count,
                    ContainerLabelCount = runtimeRequest.ContainerLabels.Count,
                    WorkspacePath = runtimeRequest.WorkspacePath,
                    McpConfigPath = mcpBootstrap.ConfigPath,
                    McpConfigValid = mcpBootstrap.IsValid,
                    McpInstallActions = mcpBootstrap.InstallActionCount,
                });

            IHarnessEventSink sink = onLogChunk is null
                ? NullHarnessEventSink.Instance
                : new CallbackHarnessEventSink(onLogChunk);

            HarnessRuntimeResult runtimeResult;
            Exception? structuredRuntimeFailure = null;
            var runtimeName = runtimeSelection.Primary.Name;

            try
            {
                runtimeResult = await runtimeSelection.Primary.RunAsync(runtimeRequest, sink, cancellationToken);
            }
            catch (Exception ex) when (runtimeSelection.Fallback is not null && ex is not OperationCanceledException)
            {
                structuredRuntimeFailure = ex;
                runtimeName = runtimeSelection.Fallback.Name;

                logger.LogWarning(
                    ex,
                    "Structured runtime fallback triggered {@Data}",
                    new
                    {
                        request.RunId,
                        request.TaskId,
                        PrimaryRuntime = runtimeSelection.Primary.Name,
                        FallbackRuntime = runtimeSelection.Fallback.Name,
                        Error = ex.Message,
                    });

                await sink.PublishAsync(
                    new HarnessRuntimeEvent(
                        HarnessRuntimeEventType.Diagnostic,
                        $"Structured runtime '{runtimeSelection.Primary.Name}' failed: {ex.Message}",
                        new Dictionary<string, string>
                        {
                            ["fallbackRuntime"] = runtimeSelection.Fallback.Name,
                        }),
                    CancellationToken.None);

                runtimeResult = await runtimeSelection.Fallback.RunAsync(runtimeRequest, sink, cancellationToken);
            }

            var envelope = runtimeResult.Envelope;
            envelope.RunId = request.RunId;
            envelope.TaskId = request.TaskId;
            envelope.Metadata["runtimeMode"] = runtimeSelection.RuntimeMode;
            envelope.Metadata["runtimeName"] = runtimeName;
            envelope.Metadata["mcpConfigPresent"] = mcpBootstrap.HasConfig.ToString().ToLowerInvariant();
            envelope.Metadata["mcpConfigValid"] = mcpBootstrap.IsValid.ToString().ToLowerInvariant();
            if (!string.IsNullOrWhiteSpace(mcpBootstrap.ConfigPath))
            {
                envelope.Metadata["mcpConfigPath"] = mcpBootstrap.ConfigPath;
            }

            envelope.Metadata["mcpInstallActionCount"] = mcpBootstrap.InstallActionCount.ToString();
            if (mcpBootstrap.Diagnostics.Count > 0)
            {
                envelope.Metadata["mcpDiagnostics"] = string.Join(" | ", mcpBootstrap.Diagnostics.Take(4));
            }

            if (structuredRuntimeFailure is not null)
            {
                envelope.Metadata["structuredRuntimeFallback"] = "true";
                envelope.Metadata["structuredRuntimeFailure"] = structuredRuntimeFailure.Message;
            }

            if (!ValidateEnvelope(envelope))
            {
                logger.LogWarning(
                    "Runtime envelope validation failed {@Data}",
                    new
                    {
                        request.RunId,
                        request.TaskId,
                        RuntimeName = runtimeName,
                        Status = envelope.Status,
                        Summary = envelope.Summary,
                    });

                envelope.Status = "failed";
                envelope.Error = string.IsNullOrEmpty(envelope.Error)
                    ? "Envelope validation failed: missing required fields (status, summary)"
                    : envelope.Error;
            }

            if (workspaceContext is not null)
            {
                await FinalizeWorkspaceAfterRunAsync(request, workspaceContext, envelope, cancellationToken);
            }

            IHarnessAdapter? adapter = null;
            try
            {
                adapter = adapterFactory.Create(request.HarnessType);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to create harness adapter {@Data}",
                    new
                    {
                        request.RunId,
                        request.TaskId,
                        request.HarnessType,
                        request.Mode,
                    });
            }

            if (adapter is not null)
            {
                var classification = adapter.ClassifyFailure(envelope);
                if (classification.Class != FailureClass.None)
                {
                    envelope.Metadata["failureClass"] = classification.Class.ToString();
                    envelope.Metadata["isRetryable"] = classification.IsRetryable.ToString().ToLowerInvariant();

                    if (classification.SuggestedBackoffSeconds.HasValue)
                    {
                        envelope.Metadata["suggestedBackoffSeconds"] = classification.SuggestedBackoffSeconds.Value.ToString();
                    }

                    if (classification.RemediationHints.Count > 0)
                    {
                        envelope.Metadata["remediationHints"] = string.Join("; ", classification.RemediationHints);
                    }

                    logger.LogDebug(
                        "Failure classification detected {@Data}",
                        new
                        {
                            request.RunId,
                            request.TaskId,
                            Class = classification.Class,
                            classification.IsRetryable,
                            classification.SuggestedBackoffSeconds,
                            RemediationHintCount = classification.RemediationHints.Count,
                        });
                }

                var artifacts = adapter.MapArtifacts(envelope);
                if (artifacts.Count > 0)
                {
                    envelope.Metadata["artifactCount"] = artifacts.Count.ToString();
                    envelope.Metadata["artifacts"] = string.Join(",", artifacts.Select(a => a.Path));
                }
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
                    logger.LogDebug(
                        "Extracted runtime artifacts {@Data}",
                        new
                        {
                            request.RunId,
                            request.TaskId,
                            ExtractedArtifactCount = extractedArtifacts.Count,
                            ExtractedArtifactSizeBytes = extractedArtifacts.Sum(a => a.SizeBytes),
                        });
                }
            }

            logger.LogInformation(
                "Runtime execution completed {@Data}",
                new
                {
                    request.RunId,
                    request.TaskId,
                    Status = envelope.Status,
                    HasError = !string.IsNullOrWhiteSpace(envelope.Error),
                    RuntimeName = runtimeName,
                    RuntimeMode = runtimeSelection.RuntimeMode,
                    FallbackUsed = structuredRuntimeFailure is not null,
                    ArtifactCount = envelope.Artifacts?.Count ?? 0,
                    MetadataKeys = envelope.Metadata.Count,
                    FailureClass = envelope.Metadata.TryGetValue("failureClass", out var failureClass)
                        ? failureClass
                        : string.Empty,
                });

            return envelope;
        }
        finally
        {
            if (gitLockAcquired)
            {
                gitLock.Release();
            }
        }
    }

    private static bool ValidateEnvelope(HarnessResultEnvelope envelope)
        => !string.IsNullOrWhiteSpace(envelope.Status)
            && !string.IsNullOrWhiteSpace(envelope.Summary);

    private HarnessRunRequest BuildRuntimeRequest(DispatchJobRequest request, string workspaceHostPath)
    {
        var timeoutSeconds = request.TimeoutSeconds > 0
            ? request.TimeoutSeconds
            : options.Value.DefaultTimeoutSeconds;

        var environment = request.EnvironmentVars is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(request.EnvironmentVars, StringComparer.OrdinalIgnoreCase);

        var labels = request.ContainerLabels is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(request.ContainerLabels, StringComparer.OrdinalIgnoreCase);

        var workspacePath = string.IsNullOrWhiteSpace(workspaceHostPath)
            ? Directory.GetCurrentDirectory()
            : workspaceHostPath;

        return new HarnessRunRequest
        {
            RunId = request.RunId,
            TaskId = request.TaskId,
            Harness = request.HarnessType,
            Mode = ResolveRuntimeMode(request.HarnessType, request.Mode, environment),
            Prompt = request.Instruction ?? string.Empty,
            WorkspacePath = workspacePath,
            Environment = environment,
            Timeout = TimeSpan.FromSeconds(timeoutSeconds),
            Command = request.CustomArgs ?? string.Empty,
            UseDocker = false,
            ArtifactsHostPath = Path.Combine(options.Value.ArtifactStoragePath, request.RunId),
            ContainerLabels = labels,
            CpuLimit = request.SandboxProfileCpuLimit is > 0 ? request.SandboxProfileCpuLimit.Value : 1.5,
            MemoryLimit = request.SandboxProfileMemoryLimit is > 0
                ? $"{request.SandboxProfileMemoryLimit.Value / (1024 * 1024)}m"
                : "2g",
            NetworkDisabled = request.SandboxProfileNetworkDisabled,
            ReadOnlyRootFs = request.SandboxProfileReadOnlyRootFs,
            InputParts = request.InputParts is { Count: > 0 } ? [.. request.InputParts] : [],
            ImageAttachments = request.ImageAttachments is { Count: > 0 } ? [.. request.ImageAttachments] : [],
            PreferNativeMultimodal = request.PreferNativeMultimodal,
            MultimodalFallbackPolicy = request.MultimodalFallbackPolicy,
            McpConfigSnapshotJson = request.McpConfigSnapshotJson,
        };
    }

    private static string ResolveRuntimeMode(
        string harness,
        HarnessExecutionMode requestedMode,
        IReadOnlyDictionary<string, string> environment)
    {
        if (environment.TryGetValue("HARNESS_RUNTIME_MODE", out var runtimeMode) &&
            !string.IsNullOrWhiteSpace(runtimeMode))
        {
            if (string.Equals(harness, "codex", StringComparison.OrdinalIgnoreCase))
            {
                return "stdio";
            }

            if (string.Equals(harness, "opencode", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(harness, "open-code", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(harness, "open code", StringComparison.OrdinalIgnoreCase))
            {
                return "sse";
            }

            return runtimeMode.Trim();
        }

        if (string.Equals(harness, "codex", StringComparison.OrdinalIgnoreCase))
        {
            return "stdio";
        }

        if (string.Equals(harness, "opencode", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(harness, "open-code", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(harness, "open code", StringComparison.OrdinalIgnoreCase))
        {
            return "sse";
        }

        if (environment.TryGetValue("HARNESS_MODE", out var harnessMode) &&
            !string.IsNullOrWhiteSpace(harnessMode))
        {
            return harnessMode.Trim();
        }

        if (environment.TryGetValue("HARNESS_EXECUTION_MODE", out var executionMode) &&
            !string.IsNullOrWhiteSpace(executionMode))
        {
            return executionMode.Trim();
        }

        if (requestedMode != HarnessExecutionMode.Default)
        {
            return requestedMode.ToString().ToLowerInvariant();
        }

        return "unsupported";
    }

    private async Task<WorkspaceContext> PrepareWorkspaceAsync(DispatchJobRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.CloneUrl))
        {
            throw new InvalidOperationException("Clone URL is required for workspace preparation.");
        }

        var repositoryPath = Path.Combine(options.Value.WorkspacesRootPath, ToPathSegment(request.RepositoryId));
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
        string workspacePath,
        CancellationToken cancellationToken)
    {
        var authorName = ResolveEnvValue(request.EnvironmentVars, "GIT_COMMITTER_NAME")
            ?? ResolveEnvValue(request.EnvironmentVars, "GIT_AUTHOR_NAME")
            ?? "AgentsDashboard Bot";

        var authorEmail = ResolveEnvValue(request.EnvironmentVars, "GIT_COMMITTER_EMAIL")
            ?? ResolveEnvValue(request.EnvironmentVars, "GIT_AUTHOR_EMAIL")
            ?? "agentsdashboard-bot@local";

        await ExecuteGitOrThrowInPathAsync(
            workspacePath,
            ["config", "user.name", authorName],
            "configure git user.name",
            cancellationToken);

        await ExecuteGitOrThrowInPathAsync(
            workspacePath,
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

    private async Task<GitCommandResult> ExecuteGitInPathAsync(
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

    private static async Task<GitCommandResult> ExecuteGitAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = Directory.GetCurrentDirectory(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (!process.Start())
        {
            return new GitCommandResult(-1, string.Empty, "Failed to start git process.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            TryKillProcess(process);
            throw;
        }

        return new GitCommandResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask);
    }

    private static string BuildGitFailureMessage(string operation, GitCommandResult result)
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

    private static bool IsNothingToCommit(GitCommandResult result)
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

}
