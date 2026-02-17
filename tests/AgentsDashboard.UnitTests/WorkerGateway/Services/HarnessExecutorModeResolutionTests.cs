using System.Reflection;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.WorkerGateway.Services;

namespace AgentsDashboard.UnitTests.WorkerGateway.Services;

public sealed class HarnessExecutorModeResolutionTests
{
    private static readonly MethodInfo ResolveRuntimeModeMethod = typeof(HarnessExecutor)
        .GetMethod("ResolveRuntimeMode", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Test]
    public void ResolveRuntimeMode_WhenRuntimeModeEnvIsSet_ReturnsIt()
    {
        var mode = InvokeResolveRuntimeMode(
            "codex",
            HarnessExecutionMode.Default,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["HARNESS_RUNTIME_MODE"] = "app-server",
                ["CODEX_TRANSPORT"] = "app-server",
            });

        mode.Should().Be("app-server");
    }

    [Test]
    public void ResolveRuntimeMode_WhenCodexTransportSetBeforeHarnessMode_PrioritizesTransport()
    {
        var mode = InvokeResolveRuntimeMode(
            "codex",
            HarnessExecutionMode.Default,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["CODEX_TRANSPORT"] = "command",
                ["HARNESS_MODE"] = "command",
            });

        mode.Should().Be("command");
    }

    [Test]
    public void ResolveRuntimeMode_WhenCodexTransportIsSet_UsesCodexTransportBeforeHarnessMode()
    {
        var mode = InvokeResolveRuntimeMode(
            "codex",
            HarnessExecutionMode.Default,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["CODEX_TRANSPORT"] = "app-server",
                ["HARNESS_MODE"] = "plan",
            });

        mode.Should().Be("app-server");
    }

    [Test]
    public void ResolveRuntimeMode_WhenExecutionModeRequested_UsesRequestedModeWhenNoOverrides()
    {
        var mode = InvokeResolveRuntimeMode(
            "claude-code",
            HarnessExecutionMode.Review,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        mode.Should().Be("review");
    }

    [Test]
    public void ResolveRuntimeMode_WhenNothingSpecified_DefaultsToCommand()
    {
        var mode = InvokeResolveRuntimeMode(
            "opencode",
            HarnessExecutionMode.Default,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        mode.Should().Be("command");
    }

    private static string InvokeResolveRuntimeMode(
        string harness,
        HarnessExecutionMode requestedMode,
        IReadOnlyDictionary<string, string> environment)
    {
        return (string)ResolveRuntimeModeMethod.Invoke(null, [harness, requestedMode, environment])!;
    }
}
