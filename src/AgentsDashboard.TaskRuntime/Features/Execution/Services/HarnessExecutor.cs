using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;






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
        var runtimeGitEnvironment = CaptureRuntimeGitEnvironment();
        var gitCommandOptions = ResolveGitCommandOptions(request.CloneUrl, request.EnvironmentVars, runtimeGitEnvironment);

        if (!string.IsNullOrWhiteSpace(request.CloneUrl))
        {
            try
            {
                await gitLock.WaitAsync(cancellationToken);
                gitLockAcquired = true;

                workspaceContext = await PrepareWorkspaceAsync(request, gitCommandOptions, cancellationToken);
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
                await FinalizeWorkspaceAfterRunAsync(request, workspaceContext, envelope, workspaceContext.GitCommandOptions, cancellationToken);
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

        if (requestedMode != HarnessExecutionMode.Default)
        {
            return requestedMode.ToString().ToLowerInvariant();
        }

        return "unsupported";
    }

    private async Task<WorkspaceContext> PrepareWorkspaceAsync(
        DispatchJobRequest request,
        GitCommandOptions gitCommandOptions,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeCloneUrl(gitCommandOptions.CloneUrl, out var normalizedCloneUrl, out var cloneUrlError))
        {
            throw new InvalidOperationException(cloneUrlError);
        }
        var normalizedGitCommandOptions = gitCommandOptions with { CloneUrl = normalizedCloneUrl };

        var repositoryPath = Path.Combine(options.Value.WorkspacesRootPath, ToPathSegment(request.RepositoryId));
        var tasksPath = Path.Combine(repositoryPath, "tasks");
        var workspacePath = Path.Combine(tasksPath, ToPathSegment(request.TaskId));
        var mainBranch = ResolveMainBranch(request);

        Directory.CreateDirectory(repositoryPath);
        Directory.CreateDirectory(tasksPath);

        var effectiveGitCommandOptions = await EnsureWorkspaceReadyAsync(
            normalizedGitCommandOptions.CloneUrl,
            workspacePath,
            mainBranch,
            normalizedGitCommandOptions,
            cancellationToken);

        var headBeforeRun = await GetHeadCommitAsync(workspacePath, effectiveGitCommandOptions, cancellationToken);
        return new WorkspaceContext(workspacePath, mainBranch, headBeforeRun, effectiveGitCommandOptions);
    }

    private async Task<GitCommandOptions> EnsureWorkspaceReadyAsync(
        string cloneUrl,
        string workspacePath,
        string mainBranch,
        GitCommandOptions gitCommandOptions,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeCloneUrl(cloneUrl, out var normalizedCloneUrl, out var cloneUrlError))
        {
            throw new InvalidOperationException(cloneUrlError);
        }

        var normalizedGitCommandOptions = gitCommandOptions with { CloneUrl = normalizedCloneUrl };

        var gitDirectory = Path.Combine(workspacePath, ".git");
        var effectiveGitCommandOptions = normalizedGitCommandOptions;
        if (!Directory.Exists(gitDirectory))
        {
            if (Directory.Exists(workspacePath))
            {
                Directory.Delete(workspacePath, true);
            }

            effectiveGitCommandOptions = await ExecuteCloneWithFallbackAsync(
                normalizedCloneUrl,
                workspacePath,
                mainBranch,
                normalizedGitCommandOptions,
                cancellationToken);
        }

        await ExecuteGitOrThrowInPathAsync(
            workspacePath,
            ["remote", "set-url", "origin", effectiveGitCommandOptions.CloneUrl],
            "set workspace origin URL",
            effectiveGitCommandOptions,
            cancellationToken);

        var fetchResult = await ExecuteGitInPathAsync(
            workspacePath,
            ["fetch", "--prune", "origin"],
            effectiveGitCommandOptions,
            cancellationToken);
        if (fetchResult.ExitCode != 0)
        {
            if (!TryParseGitHubRepoSlug(normalizedCloneUrl, out _))
            {
                throw new InvalidOperationException(BuildGitFailureMessage("fetch workspace origin", fetchResult));
            }

            var fetchFailureMessage = BuildGitCloneFailureMessage(
                "fetch workspace origin",
                fetchResult,
                effectiveGitCommandOptions.CloneUrl,
                effectiveGitCommandOptions.EnvironmentVariables);
            logger.LogWarning(
                "Workspace fetch failed, rebuilding workspace with clone fallback {@Data}",
                new
                {
                    CloneUrl = effectiveGitCommandOptions.CloneUrl,
                    RepositoryPath = workspacePath,
                    Failure = fetchFailureMessage,
                });

            if (Directory.Exists(workspacePath))
            {
                Directory.Delete(workspacePath, true);
            }

            effectiveGitCommandOptions = await ExecuteCloneWithFallbackAsync(
                normalizedCloneUrl,
                workspacePath,
                mainBranch,
                normalizedGitCommandOptions,
                cancellationToken);

            await ExecuteGitOrThrowInPathAsync(
                workspacePath,
                ["remote", "set-url", "origin", effectiveGitCommandOptions.CloneUrl],
                "set workspace origin URL",
                effectiveGitCommandOptions,
                cancellationToken);

            await ExecuteGitOrThrowInPathAsync(
                workspacePath,
                ["fetch", "--prune", "origin"],
                "fetch workspace origin",
                effectiveGitCommandOptions,
                cancellationToken);
        }

        await EnsureMainBranchCheckedOutAsync(workspacePath, mainBranch, effectiveGitCommandOptions, cancellationToken);

        await ExecuteGitOrThrowInPathAsync(
            workspacePath,
            ["reset", "--hard", $"origin/{mainBranch}"],
            "reset workspace to origin main branch",
            effectiveGitCommandOptions,
            cancellationToken);

        await ExecuteGitOrThrowInPathAsync(
            workspacePath,
            ["clean", "-fd"],
            "clean workspace",
            effectiveGitCommandOptions,
            cancellationToken);

        return effectiveGitCommandOptions;
    }

    private async Task<GitCommandOptions> ExecuteCloneWithFallbackAsync(
        string cloneUrl,
        string workspacePath,
        string mainBranch,
        GitCommandOptions gitCommandOptions,
        CancellationToken cancellationToken)
    {
        if (!TryParseGitHubRepoSlug(cloneUrl, out var repositorySlug))
        {
            var directCloneResult = await ExecuteGitAsync(["clone", gitCommandOptions.CloneUrl, workspacePath], gitCommandOptions, cancellationToken);
            if (directCloneResult.ExitCode == 0)
            {
                return gitCommandOptions;
            }

            var directFailureMessage = BuildGitCloneFailureMessage(
                "clone task workspace",
                directCloneResult,
                gitCommandOptions.CloneUrl,
                gitCommandOptions.EnvironmentVariables);
            throw new InvalidOperationException($"clone task workspace failed. Last attempt: {directFailureMessage}");
        }

        var requestEnvironment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(gitCommandOptions.GitHubToken))
        {
            requestEnvironment["GITHUB_TOKEN"] = gitCommandOptions.GitHubToken;
        }

        var sshCloneOptions = ResolveGitCommandOptions(
            BuildGitHubSshCloneUrl(repositorySlug),
            requestEnvironment,
            gitCommandOptions.EnvironmentVariables,
            forceOriginalCloneUrl: true);
        var sshCloneResult = await ExecuteGitAsync(["clone", sshCloneOptions.CloneUrl, workspacePath], sshCloneOptions, cancellationToken);
        if (sshCloneResult.ExitCode == 0)
        {
            return sshCloneOptions;
        }

        var sshFailureMessage = BuildGitCloneFailureMessage(
            "clone task workspace",
            sshCloneResult,
            sshCloneOptions.CloneUrl,
            sshCloneOptions.EnvironmentVariables);
        logger.LogWarning(
            "Workspace clone over SSH failed, retrying with gh CLI {@Data}",
            new
            {
                CloneUrl = sshCloneOptions.CloneUrl,
                RepositoryPath = workspacePath,
                Failure = sshFailureMessage,
            });

        if (Directory.Exists(workspacePath))
        {
            Directory.Delete(workspacePath, true);
        }

        var ghCloneResult = await ExecuteGhCloneAsync(repositorySlug, workspacePath, mainBranch, gitCommandOptions, cancellationToken);
        if (ghCloneResult.ExitCode == 0)
        {
            var ghOriginResult = await ExecuteGitInPathAsync(workspacePath, ["remote", "get-url", "origin"], gitCommandOptions, cancellationToken);
            var ghCloneUrl = ghOriginResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(ghOriginResult.StandardOutput)
                ? ghOriginResult.StandardOutput.Trim()
                : BuildGitHubSshCloneUrl(repositorySlug);

            return ResolveGitCommandOptions(
                ghCloneUrl,
                requestEnvironment,
                gitCommandOptions.EnvironmentVariables);
        }

        var ghFailureMessage = BuildGitCloneFailureMessage(
            "clone task workspace with gh",
            ghCloneResult,
            BuildGitHubSshCloneUrl(repositorySlug),
            gitCommandOptions.EnvironmentVariables);
        logger.LogWarning(
            "Workspace clone with gh failed, retrying with HTTPS fallback {@Data}",
            new
            {
                CloneUrl = BuildGitHubSshCloneUrl(repositorySlug),
                RepositoryPath = workspacePath,
                Failure = ghFailureMessage,
            });

        if (Directory.Exists(workspacePath))
        {
            Directory.Delete(workspacePath, true);
        }

        var httpsCloneOptions = GetHttpsFallbackOptions(cloneUrl, gitCommandOptions);
        var httpsCloneResult = await ExecuteGitAsync(["clone", httpsCloneOptions.CloneUrl, workspacePath], httpsCloneOptions, cancellationToken);
        if (httpsCloneResult.ExitCode == 0)
        {
            return httpsCloneOptions;
        }

        var httpsFailureMessage = BuildGitCloneFailureMessage(
            "clone task workspace",
            httpsCloneResult,
            httpsCloneOptions.CloneUrl,
            httpsCloneOptions.EnvironmentVariables);

        throw new InvalidOperationException(
            $"clone task workspace failed after SSH, gh CLI, and HTTPS attempts. ssh={sshFailureMessage}; gh={ghFailureMessage}; https={httpsFailureMessage}");
    }

    private async Task<GitCommandResult> ExecuteGhCloneAsync(
        string repositorySlug,
        string workspacePath,
        string branch,
        GitCommandOptions gitCommandOptions,
        CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "repo",
            "clone",
            repositorySlug,
            workspacePath,
        };

        if (!string.IsNullOrWhiteSpace(branch))
        {
            arguments.Add("--");
            arguments.Add("--branch");
            arguments.Add(branch);
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "gh",
                WorkingDirectory = Directory.GetCurrentDirectory(),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            }
        };

        process.StartInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";

        foreach (var (key, value) in gitCommandOptions.EnvironmentVariables)
        {
            process.StartInfo.Environment[key] = value;
        }

        if (!string.IsNullOrWhiteSpace(gitCommandOptions.GitHubToken))
        {
            process.StartInfo.Environment["GH_TOKEN"] = gitCommandOptions.GitHubToken;
            process.StartInfo.Environment["GITHUB_TOKEN"] = gitCommandOptions.GitHubToken;
        }

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (!process.Start())
        {
            return new GitCommandResult(-1, string.Empty, "Failed to start gh process.");
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

    private async Task FinalizeWorkspaceAfterRunAsync(
        DispatchJobRequest request,
        WorkspaceContext workspaceContext,
        HarnessResultEnvelope envelope,
        GitCommandOptions gitCommandOptions,
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
            await EnsureMainBranchCheckedOutAsync(workspaceContext.WorkspacePath, workspaceContext.MainBranch, gitCommandOptions, cancellationToken);

            var hasWorkspaceChanges = await HasWorkspaceChangesAsync(workspaceContext.WorkspacePath, gitCommandOptions, cancellationToken);
            if (!hasWorkspaceChanges)
            {
                MarkRunAsObsolete(envelope, "no-diff");
                return;
            }

            await StageAndCommitWorkspaceChangesAsync(request, workspaceContext, gitCommandOptions, cancellationToken);

            var headAfterRun = await GetHeadCommitAsync(workspaceContext.WorkspacePath, gitCommandOptions, cancellationToken);
            if (string.Equals(workspaceContext.HeadBeforeRun, headAfterRun, StringComparison.Ordinal))
            {
                MarkRunAsObsolete(envelope, "no-diff");
                return;
            }

            await ExecuteGitOrThrowInPathAsync(
                workspaceContext.WorkspacePath,
                ["push", "origin", workspaceContext.MainBranch],
                "push main branch to origin",
                gitCommandOptions,
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
        GitCommandOptions gitCommandOptions,
        CancellationToken cancellationToken)
    {
        await ExecuteGitOrThrowInPathAsync(
            workspaceContext.WorkspacePath,
            ["add", "-A"],
            "stage workspace changes",
            gitCommandOptions,
            cancellationToken);

        await EnsureCommitIdentityAsync(request, workspaceContext.WorkspacePath, gitCommandOptions, cancellationToken);

        var commitResult = await ExecuteGitInPathAsync(
            workspaceContext.WorkspacePath,
            ["commit", "-m", BuildMainBranchCommitMessage(request)],
            gitCommandOptions,
            cancellationToken);

        if (commitResult.ExitCode != 0 && !IsNothingToCommit(commitResult))
        {
            throw new InvalidOperationException(BuildGitFailureMessage("commit workspace changes", commitResult));
        }
    }

    private async Task EnsureMainBranchCheckedOutAsync(
        string workspacePath,
        string mainBranch,
        GitCommandOptions gitCommandOptions,
        CancellationToken cancellationToken)
    {
        var checkoutResult = await ExecuteGitInPathAsync(workspacePath, ["checkout", mainBranch], gitCommandOptions, cancellationToken);
        if (checkoutResult.ExitCode == 0)
        {
            return;
        }

        await ExecuteGitOrThrowInPathAsync(
            workspacePath,
            ["checkout", "-B", mainBranch, $"origin/{mainBranch}"],
            "checkout or create main branch",
            gitCommandOptions,
            cancellationToken);
    }

    private async Task EnsureCommitIdentityAsync(
        DispatchJobRequest request,
        string workspacePath,
        GitCommandOptions gitCommandOptions,
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
            gitCommandOptions,
            cancellationToken);

        await ExecuteGitOrThrowInPathAsync(
            workspacePath,
            ["config", "user.email", authorEmail],
            "configure git user.email",
            gitCommandOptions,
            cancellationToken);
    }

    private static string? ResolveEnvValue(IReadOnlyDictionary<string, string>? envVars, string key)
    {
        if (envVars is null)
        {
            return null;
        }

        foreach (var (currentKey, value) in envVars)
        {
            if (!string.Equals(currentKey, key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static GitCommandOptions ResolveGitCommandOptions(
        string cloneUrl,
        IReadOnlyDictionary<string, string>? environmentVars,
        IReadOnlyDictionary<string, string> runtimeEnvironment,
        bool forceHttpsFallback = false,
        bool forceOriginalCloneUrl = false)
    {
        var githubToken = ResolveEnvValue(environmentVars, "GITHUB_TOKEN")
            ?? ResolveEnvValue(environmentVars, "GH_TOKEN");
        var hasSshCredentials = !forceHttpsFallback && HasSshCredentialsAvailable(environmentVars, runtimeEnvironment);
        var resolvedCloneUrl = forceOriginalCloneUrl
            ? cloneUrl
            : ResolveCloneUrlForGitHubToken(cloneUrl, githubToken, hasSshCredentials);

        var argumentPrefix = new List<string>();
        if (!string.IsNullOrWhiteSpace(githubToken) && IsGitHubHttpsUrl(resolvedCloneUrl))
        {
            argumentPrefix.Add("-c");
            argumentPrefix.Add($"http.https://github.com/.extraheader=Authorization: Basic {ToBasicAuthToken(githubToken)}");
        }

        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["GIT_TERMINAL_PROMPT"] = "0",
        };

        AddEnvironmentValueIfPresent(environment, runtimeEnvironment, "SSH_AUTH_SOCK");
        AddEnvironmentValueIfPresent(environment, runtimeEnvironment, "GIT_SSH_COMMAND");

        return new GitCommandOptions(
            string.IsNullOrWhiteSpace(resolvedCloneUrl) ? cloneUrl : resolvedCloneUrl,
            argumentPrefix,
            environment,
            githubToken,
            hasSshCredentials);
    }

    private static GitCommandOptions GetHttpsFallbackOptions(
        string cloneUrl,
        GitCommandOptions gitCommandOptions)
    {
        if (!TryParseGitHubRepoSlug(cloneUrl, out var repositorySlug))
        {
            return gitCommandOptions;
        }

        var fallbackRequestEnvironment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(gitCommandOptions.GitHubToken))
        {
            fallbackRequestEnvironment["GITHUB_TOKEN"] = gitCommandOptions.GitHubToken;
        }

        return ResolveGitCommandOptions(
            BuildGitHubHttpsCloneUrl(repositorySlug),
            fallbackRequestEnvironment,
            gitCommandOptions.EnvironmentVariables,
            forceHttpsFallback: true);
    }

    private static bool TryParseGitHubRepoSlug(string cloneUrl, out string repositorySlug)
    {
        repositorySlug = string.Empty;
        if (string.IsNullOrWhiteSpace(cloneUrl))
        {
            return false;
        }

        var normalizedCloneUrl = cloneUrl.Trim();
        if (normalizedCloneUrl.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
        {
            normalizedCloneUrl = normalizedCloneUrl["git@github.com:".Length..];
        }
        else if (normalizedCloneUrl.StartsWith("ssh://git@github.com/", StringComparison.OrdinalIgnoreCase))
        {
            normalizedCloneUrl = normalizedCloneUrl["ssh://git@github.com/".Length..];
        }
        else if (normalizedCloneUrl.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
        {
            normalizedCloneUrl = normalizedCloneUrl["https://github.com/".Length..];
        }
        else if (normalizedCloneUrl.StartsWith("http://github.com/", StringComparison.OrdinalIgnoreCase))
        {
            normalizedCloneUrl = normalizedCloneUrl["http://github.com/".Length..];
        }
        else if (Uri.TryCreate(normalizedCloneUrl, UriKind.Absolute, out var absoluteUri) &&
                 string.Equals(absoluteUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            normalizedCloneUrl = absoluteUri.AbsolutePath.Trim('/');
        }
        else
        {
            return false;
        }

        if (normalizedCloneUrl.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            normalizedCloneUrl = normalizedCloneUrl[..^4];
        }

        var parts = normalizedCloneUrl
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        repositorySlug = $"{parts[0]}/{parts[1]}";
        return true;
    }

    private static string BuildGitHubSshCloneUrl(string repositorySlug)
    {
        return $"git@github.com:{repositorySlug}.git";
    }

    private static string BuildGitHubHttpsCloneUrl(string repositorySlug)
    {
        return $"https://github.com/{repositorySlug}.git";
    }

    private static string BuildGitCloneFailureMessage(
        string operation,
        GitCommandResult result,
        string cloneUrl,
        IReadOnlyDictionary<string, string> runtimeEnvironment)
    {
        return $"{BuildGitFailureMessage(operation, result)} | {BuildGitAuthContext(cloneUrl, runtimeEnvironment)}";
    }

    private static string BuildGitAuthContext(string cloneUrl, IReadOnlyDictionary<string, string> runtimeEnvironment)
    {
        var hasSshAvailabilityFlag = string.Equals(
            ResolveEnvValue(runtimeEnvironment, "AGENTSDASHBOARD_WORKER_SSH_AVAILABLE"),
            "true",
            StringComparison.OrdinalIgnoreCase);

        var sshAuthSock = ResolveEnvValue(runtimeEnvironment, "SSH_AUTH_SOCK") ?? string.Empty;
        var hasSshAuthSock = !string.IsNullOrWhiteSpace(sshAuthSock) && Path.Exists(sshAuthSock);

        var home = ResolveEnvValue(runtimeEnvironment, "HOME") ?? "<unset>";
        var homeResolved = !string.IsNullOrWhiteSpace(home);
        var sshDirectory = homeResolved ? Path.Combine(home, ".ssh") : "<unset>";
        var hasSshDirectory = homeResolved && Directory.Exists(sshDirectory);
        var hasKnownPrivateKey = homeResolved && HasPotentialSshCredentials(sshDirectory);
        var hasSshCredentials = HasSshCredentialsAvailable(null, runtimeEnvironment);

        var selectedScheme = Uri.TryCreate(cloneUrl, UriKind.Absolute, out var uri)
            ? uri.Scheme
            : IsScpStyleCloneUrl(cloneUrl)
                ? "ssh"
                : "invalid";

        return
            $"gitAuthMode={(hasSshCredentials ? "ssh" : "https")}" +
            $", cloneScheme={selectedScheme}" +
            $", sshAvailabilityFlag={hasSshAvailabilityFlag.ToString().ToLowerInvariant()}" +
            $", sshAuthSockPresent={hasSshAuthSock.ToString().ToLowerInvariant()}" +
            $", sshDirectoryExists={hasSshDirectory.ToString().ToLowerInvariant()}" +
            $", sshPrivateKeyCandidateFound={hasKnownPrivateKey.ToString().ToLowerInvariant()}" +
            $", home={home}";
    }

    private static IReadOnlyDictionary<string, string> CaptureRuntimeGitEnvironment()
    {
        var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var sshAuthSock = Environment.GetEnvironmentVariable("SSH_AUTH_SOCK");
        if (!string.IsNullOrWhiteSpace(sshAuthSock))
        {
            environment["SSH_AUTH_SOCK"] = sshAuthSock.Trim();
        }

        var gitSshCommand = Environment.GetEnvironmentVariable("GIT_SSH_COMMAND");
        if (!string.IsNullOrWhiteSpace(gitSshCommand))
        {
            environment["GIT_SSH_COMMAND"] = gitSshCommand.Trim();
        }

        var home = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrWhiteSpace(home))
        {
            environment["HOME"] = home.Trim();
        }

        var workerSshAvailable = Environment.GetEnvironmentVariable("AGENTSDASHBOARD_WORKER_SSH_AVAILABLE");
        if (!string.IsNullOrWhiteSpace(workerSshAvailable))
        {
            environment["AGENTSDASHBOARD_WORKER_SSH_AVAILABLE"] = workerSshAvailable.Trim();
        }

        return environment;
    }

    private static bool HasSshCredentialsAvailable(
        IReadOnlyDictionary<string, string>? requestEnvironment,
        IReadOnlyDictionary<string, string> runtimeEnvironment)
    {
        var sshAvailabilityFlag = ResolveEnvValue(runtimeEnvironment, "AGENTSDASHBOARD_WORKER_SSH_AVAILABLE");
        if (string.Equals(sshAvailabilityFlag, "false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var sshAuthSock = ResolveEnvValue(runtimeEnvironment, "SSH_AUTH_SOCK")
            ?? ResolveEnvValue(requestEnvironment, "SSH_AUTH_SOCK");
        if (!string.IsNullOrWhiteSpace(sshAuthSock) && Path.Exists(sshAuthSock))
        {
            return true;
        }

        var home = ResolveEnvValue(runtimeEnvironment, "HOME")
            ?? ResolveEnvValue(requestEnvironment, "HOME");
        if (string.IsNullOrWhiteSpace(home))
        {
            return false;
        }

        try
        {
            var sshDirectory = Path.Combine(home, ".ssh");
            return HasPotentialSshCredentials(sshDirectory);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasPotentialSshCredentials(string sshDirectory)
    {
        if (!Directory.Exists(sshDirectory))
        {
            return false;
        }

        try
        {
            return Directory
                .EnumerateFiles(sshDirectory, "*", SearchOption.TopDirectoryOnly)
                .Any(IsPotentialPrivateKeyFile);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPotentialPrivateKeyFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        var normalizedName = fileName.ToLowerInvariant();
        if (normalizedName.EndsWith(".pub", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (normalizedName is "known_hosts" or "known_hosts.old" or "config" or "authorized_keys" or "authorized_keys2" or "ssh_config")
        {
            return false;
        }

        if (normalizedName.StartsWith("id_", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var extension = Path.GetExtension(normalizedName);
        if (extension is ".pem" or ".key" or ".ppk")
        {
            return true;
        }

        return HasPrivateKeyMarker(path);
    }

    private static bool HasPrivateKeyMarker(string path)
    {
        const int maxChars = 4096;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            var buffer = new char[maxChars];
            var charsRead = reader.Read(buffer, 0, buffer.Length);
            if (charsRead <= 0)
            {
                return false;
            }

            var sample = new string(buffer, 0, charsRead);
            return sample.Contains("BEGIN OPENSSH PRIVATE KEY", StringComparison.OrdinalIgnoreCase) ||
                   sample.Contains("BEGIN RSA PRIVATE KEY", StringComparison.OrdinalIgnoreCase) ||
                   sample.Contains("BEGIN DSA PRIVATE KEY", StringComparison.OrdinalIgnoreCase) ||
                   sample.Contains("BEGIN ECDSA PRIVATE KEY", StringComparison.OrdinalIgnoreCase) ||
                   sample.Contains("BEGIN EC PRIVATE KEY", StringComparison.OrdinalIgnoreCase) ||
                   sample.Contains("BEGIN PRIVATE KEY", StringComparison.OrdinalIgnoreCase) ||
                   sample.Contains("BEGIN ENCRYPTED PRIVATE KEY", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void AddEnvironmentValueIfPresent(
        Dictionary<string, string> destination,
        IReadOnlyDictionary<string, string>? source,
        string key)
    {
        var value = ResolveEnvValue(source, key);
        if (!string.IsNullOrWhiteSpace(value))
        {
            destination[key] = value;
        }
    }

    private static bool TryNormalizeCloneUrl(string? cloneUrl, out string normalizedCloneUrl, out string error)
    {
        normalizedCloneUrl = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(cloneUrl))
        {
            error = "Clone URL is required for workspace preparation.";
            return false;
        }

        var trimmed = cloneUrl.Trim();
        if (IsScpStyleCloneUrl(trimmed) || IsSupportedCloneUrl(trimmed))
        {
            normalizedCloneUrl = trimmed;
            return true;
        }

        error = $"Unsupported clone URL format: {cloneUrl}";
        return false;
    }

    private static bool IsSupportedCloneUrl(string cloneUrl)
    {
        if (string.IsNullOrWhiteSpace(cloneUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(cloneUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!uri.IsWellFormedOriginalString() || string.IsNullOrWhiteSpace(uri.Host))
        {
            return false;
        }

        return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri.Scheme, Uri.UriSchemeSsh, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri.Scheme, "git", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri.Scheme, "git+ssh", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsScpStyleCloneUrl(string cloneUrl)
    {
        if (string.IsNullOrWhiteSpace(cloneUrl))
        {
            return false;
        }

        if (Uri.TryCreate(cloneUrl, UriKind.Absolute, out _))
        {
            return false;
        }

        if (cloneUrl.Contains(' '))
        {
            return false;
        }

        var atIndex = cloneUrl.IndexOf('@', StringComparison.Ordinal);
        if (atIndex <= 0)
        {
            return false;
        }

        var colonIndex = cloneUrl.IndexOf(':', StringComparison.Ordinal);
        if (colonIndex <= atIndex)
        {
            return false;
        }

        var host = cloneUrl[(atIndex + 1)..colonIndex];
        return !string.IsNullOrWhiteSpace(host) && !host.Contains('/');
    }

    private static string ResolveCloneUrlForGitHubToken(string cloneUrl, string? githubToken, bool hasSshCredentials)
    {
        if (string.IsNullOrWhiteSpace(cloneUrl))
        {
            return cloneUrl;
        }

        var normalizedCloneUrl = cloneUrl.Trim();
        if (hasSshCredentials)
        {
            return normalizedCloneUrl;
        }

        const string gitSshPrefix = "git@github.com:";
        if (normalizedCloneUrl.StartsWith(gitSshPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return $"https://github.com/{normalizedCloneUrl[gitSshPrefix.Length..]}";
        }

        const string sshUrlPrefix = "ssh://git@github.com/";
        if (normalizedCloneUrl.StartsWith(sshUrlPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return $"https://github.com/{normalizedCloneUrl[sshUrlPrefix.Length..]}";
        }

        return normalizedCloneUrl;
    }

    private static bool IsGitHubHttpsUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
    }

    private static string ToBasicAuthToken(string token)
    {
        var bytes = Encoding.UTF8.GetBytes($"x-access-token:{token}");
        return Convert.ToBase64String(bytes);
    }

    private async Task<string> GetHeadCommitAsync(
        string workspacePath,
        GitCommandOptions gitCommandOptions,
        CancellationToken cancellationToken)
    {
        var result = await ExecuteGitInPathAsync(workspacePath, ["rev-parse", "HEAD"], gitCommandOptions, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(BuildGitFailureMessage("resolve HEAD commit", result));
        }

        return result.StandardOutput.Trim();
    }

    private async Task<bool> HasWorkspaceChangesAsync(
        string workspacePath,
        GitCommandOptions gitCommandOptions,
        CancellationToken cancellationToken)
    {
        var result = await ExecuteGitInPathAsync(workspacePath, ["status", "--porcelain"], gitCommandOptions, cancellationToken);
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
        GitCommandOptions gitCommandOptions,
        CancellationToken cancellationToken)
    {
        var result = await ExecuteGitInPathAsync(workspacePath, arguments, gitCommandOptions, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(BuildGitFailureMessage(operation, result));
        }
    }

    private async Task<GitCommandResult> ExecuteGitInPathAsync(
        string workspacePath,
        IReadOnlyList<string> arguments,
        GitCommandOptions gitCommandOptions,
        CancellationToken cancellationToken)
    {
        var fullArguments = new List<string>(arguments.Count + 2)
        {
            "-C",
            workspacePath,
        };

        fullArguments.AddRange(arguments);
        return await ExecuteGitAsync(fullArguments, gitCommandOptions, cancellationToken);
    }

    private static async Task<GitCommandResult> ExecuteGitAsync(
        IReadOnlyList<string> arguments,
        GitCommandOptions gitCommandOptions,
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

        foreach (var (key, value) in gitCommandOptions.EnvironmentVariables)
        {
            process.StartInfo.Environment[key] = value;
        }

        foreach (var argument in gitCommandOptions.ArgumentPrefix)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

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
        else
        {
            var lines = details.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length > 0)
            {
                var fatalLine = lines.FirstOrDefault(line => line.Contains("fatal:", StringComparison.OrdinalIgnoreCase));
                details = !string.IsNullOrWhiteSpace(fatalLine)
                    ? fatalLine
                    : string.Join(" | ", lines);
            }
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
