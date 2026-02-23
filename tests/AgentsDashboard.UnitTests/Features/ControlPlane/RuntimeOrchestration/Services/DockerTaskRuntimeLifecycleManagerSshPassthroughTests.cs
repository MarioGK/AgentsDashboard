using System.Reflection;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public sealed class DockerTaskRuntimeLifecycleManagerSshPassthroughTests
{
    private static readonly MethodInfo ResolveWorkerSshPassthroughMethod = typeof(DockerTaskRuntimeLifecycleManager)
        .GetMethod("ResolveWorkerSshPassthrough", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Could not find ResolveWorkerSshPassthrough.");

    private static readonly MethodInfo NormalizeGitSshCommandModeMethod = typeof(DockerTaskRuntimeLifecycleManager)
        .GetMethod("NormalizeGitSshCommandMode", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Could not find NormalizeGitSshCommandMode.");

    [Test]
    public async Task ResolveWorkerSshPassthrough_WhenEnabledAndPathsExist_IncludesSshBindsAndEnvironment()
    {
        var tempRoot = CreateTempDirectory();
        var sshDirectory = Path.Combine(tempRoot, ".ssh");
        var agentSocketPath = Path.Combine(tempRoot, "agent.sock");

        Directory.CreateDirectory(sshDirectory);
        await File.WriteAllTextAsync(agentSocketPath, "socket");

        var runtime = CreateRuntimeSettings(
            enableHostSshPassthrough: true,
            hostSshDirectory: sshDirectory,
            hostSshAgentSocketPath: agentSocketPath,
            gitSshCommandMode: "no");

        var result = InvokeResolveWorkerSshPassthrough(runtime, "no");

        var additionalBinds = GetTupleItem<IReadOnlyList<string>>(result, 1);
        var additionalEnvironment = GetTupleItem<IReadOnlyList<string>>(result, 2);
        var sshDirectoryMounted = GetTupleItem<bool>(result, 3);
        var sshAgentMounted = GetTupleItem<bool>(result, 4);

        await Assert.That(sshDirectoryMounted).IsTrue();
        await Assert.That(sshAgentMounted).IsTrue();
        await Assert.That(additionalBinds.Contains($"{sshDirectory}:/home/agent/.ssh:ro", StringComparer.Ordinal)).IsTrue();
        await Assert.That(additionalBinds.Contains($"{agentSocketPath}:/ssh-agent.sock", StringComparer.Ordinal)).IsTrue();
        await Assert.That(additionalEnvironment.Contains("SSH_AUTH_SOCK=/ssh-agent.sock", StringComparer.Ordinal)).IsTrue();
        await Assert.That(additionalEnvironment.Contains("AGENTSDASHBOARD_WORKER_SSH_AVAILABLE=true", StringComparer.Ordinal)).IsTrue();

        TryDeleteDirectory(tempRoot);
    }

    [Test]
    public async Task ResolveWorkerSshPassthrough_WhenDisabled_ReportsSshUnavailable()
    {
        var runtime = CreateRuntimeSettings(
            enableHostSshPassthrough: false,
            hostSshDirectory: string.Empty,
            hostSshAgentSocketPath: string.Empty,
            gitSshCommandMode: "no");

        var result = InvokeResolveWorkerSshPassthrough(runtime, "no");

        var additionalBinds = GetTupleItem<IReadOnlyList<string>>(result, 1);
        var additionalEnvironment = GetTupleItem<IReadOnlyList<string>>(result, 2);
        var sshDirectoryMounted = GetTupleItem<bool>(result, 3);
        var sshAgentMounted = GetTupleItem<bool>(result, 4);

        await Assert.That(sshDirectoryMounted).IsFalse();
        await Assert.That(sshAgentMounted).IsFalse();
        await Assert.That(additionalBinds.Count).IsEqualTo(0);
        await Assert.That(additionalEnvironment.Contains("AGENTSDASHBOARD_WORKER_SSH_AVAILABLE=false", StringComparer.Ordinal)).IsTrue();
    }

    [Test]
    public async Task NormalizeGitSshCommandMode_WhenUnsupported_FallsBackToNo()
    {
        var value = NormalizeGitSshCommandModeMethod.Invoke(null, ["unsupported"]) as string;
        await Assert.That(value).IsEqualTo("no");
    }

    private static object InvokeResolveWorkerSshPassthrough(OrchestratorRuntimeSettings runtime, string gitSshMode)
    {
        var result = ResolveWorkerSshPassthroughMethod.Invoke(null, [runtime, gitSshMode]);
        if (result is null)
        {
            throw new InvalidOperationException("ResolveWorkerSshPassthrough returned null.");
        }

        return result;
    }

    private static T GetTupleItem<T>(object tuple, int index)
    {
        var field = tuple.GetType().GetField($"Item{index}", BindingFlags.Public | BindingFlags.Instance);
        if (field is null)
        {
            throw new InvalidOperationException($"Tuple field Item{index} was not found.");
        }

        var value = field.GetValue(tuple);
        if (value is T typedValue)
        {
            return typedValue;
        }

        throw new InvalidOperationException($"Tuple field Item{index} did not return expected type '{typeof(T).FullName}'.");
    }

    private static OrchestratorRuntimeSettings CreateRuntimeSettings(
        bool enableHostSshPassthrough,
        string hostSshDirectory,
        string hostSshAgentSocketPath,
        string gitSshCommandMode)
    {
        return new OrchestratorRuntimeSettings(
            MaxActiveTaskRuntimes: 1,
            DefaultTaskParallelRuns: 1,
            TaskRuntimeInactiveTimeoutMinutes: 5,
            MinWorkers: 1,
            MaxWorkers: 1,
            MaxProcessesPerWorker: 1,
            ReserveWorkers: 0,
            MaxQueueDepth: 200,
            QueueWaitTimeoutSeconds: 300,
            TaskRuntimeImagePolicy: TaskRuntimeImagePolicy.PreferLocal,
            ContainerImage: "ghcr.io/mariogk/ai-harness:latest",
            ContainerNamePrefix: "task-runtime",
            DockerNetwork: "agentsdashboard",
            ConnectivityMode: TaskRuntimeConnectivityMode.AutoDetect,
            TaskRuntimeImageRegistry: string.Empty,
            TaskRuntimeCanaryImage: string.Empty,
            WorkerDockerBuildContextPath: string.Empty,
            WorkerDockerfilePath: string.Empty,
            MaxConcurrentPulls: 2,
            MaxConcurrentBuilds: 1,
            ImagePullTimeoutSeconds: 120,
            ImageBuildTimeoutSeconds: 600,
            TaskRuntimeImageCacheTtlMinutes: 240,
            ImageFailureCooldownMinutes: 15,
            CanaryPercent: 10,
            MaxWorkerStartAttemptsPer10Min: 30,
            MaxFailedStartsPer10Min: 10,
            CooldownMinutes: 15,
            ContainerStartTimeoutSeconds: 60,
            ContainerStopTimeoutSeconds: 30,
            HealthProbeIntervalSeconds: 10,
            RuntimeHeartbeatStaleSeconds: 60,
            RuntimeProbeFailureThreshold: 2,
            RuntimeRemediationCooldownSeconds: 30,
            RuntimeReadinessDegradeSeconds: 45,
            RuntimeReadinessFailureRatioPercent: 30,
            ContainerRestartLimit: 3,
            ContainerUnhealthyAction: ContainerUnhealthyAction.Recreate,
            OrchestratorErrorBurstThreshold: 20,
            OrchestratorErrorCoolDownMinutes: 10,
            EnableDraining: true,
            DrainTimeoutSeconds: 120,
            EnableAutoRecycle: true,
            RecycleAfterRuns: 200,
            RecycleAfterUptimeMinutes: 720,
            EnableContainerAutoCleanup: true,
            WorkerCpuLimit: string.Empty,
            WorkerMemoryLimitMb: 0,
            WorkerPidsLimit: 0,
            WorkerFileDescriptorLimit: 0,
            RunHardTimeoutSeconds: 3600,
            MaxRunLogMb: 50,
            EnablePressureScaling: true,
            CpuScaleOutThresholdPercent: 85,
            MemoryScaleOutThresholdPercent: 85,
            PressureSampleWindowSeconds: 30,
            EnableHostSshPassthrough: enableHostSshPassthrough,
            HostSshDirectory: hostSshDirectory,
            HostSshAgentSocketPath: hostSshAgentSocketPath,
            GitSshCommandMode: gitSshCommandMode);
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"agentsdashboard-ssh-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
        catch
        {
        }
    }
}
