using System.Reflection;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.TaskRuntimeGateway.Services;

namespace AgentsDashboard.UnitTests.TaskRuntimeGateway.Services;

public sealed class HarnessExecutorModeResolutionTests
{
    private static readonly MethodInfo ResolveRuntimeModeMethod = typeof(HarnessExecutor)
        .GetMethod("ResolveRuntimeMode", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Test]
    public void ResolveRuntimeMode_WhenRuntimeModeEnvIsSet_ReturnsStdioForCodex()
    {
        var mode = InvokeResolveRuntimeMode(
            "codex",
            HarnessExecutionMode.Default,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["HARNESS_RUNTIME_MODE"] = "stdio",
                ["CODEX_TRANSPORT"] = "stdio",
            });

        Assert.That(mode).IsEqualTo("stdio");
    }

    [Test]
    public void ResolveRuntimeMode_WhenLegacyCodexTransportIsSet_IgnoresTransport()
    {
        var mode = InvokeResolveRuntimeMode(
            "codex",
            HarnessExecutionMode.Default,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["CODEX_TRANSPORT"] = "command",
                ["HARNESS_MODE"] = "command",
            });

        Assert.That(mode).IsEqualTo("stdio");
    }

    [Test]
    public void ResolveRuntimeMode_WhenCodexHasTransportAndHarnessMode_SetToStdio()
    {
        var mode = InvokeResolveRuntimeMode(
            "codex",
            HarnessExecutionMode.Default,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["CODEX_TRANSPORT"] = "stdio",
                ["HARNESS_MODE"] = "plan",
            });

        Assert.That(mode).IsEqualTo("stdio");
    }

    [Test]
    public void ResolveRuntimeMode_WhenOpenCodeExecutionModeRequested_UsesSseMode()
    {
        var mode = InvokeResolveRuntimeMode(
            "opencode",
            HarnessExecutionMode.Review,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        Assert.That(mode).IsEqualTo("sse");
    }

    [Test]
    public void ResolveRuntimeMode_WhenOpencodeRuntimeModeConfiguredAsWs_StillUsesSse()
    {
        var mode = InvokeResolveRuntimeMode(
            "opencode",
            HarnessExecutionMode.Default,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["HARNESS_RUNTIME_MODE"] = "ws",
            });

        Assert.That(mode).IsEqualTo("sse");
    }

    [Test]
    public void ResolveRuntimeMode_WhenCodexHasNoOverrides_DefaultsToStdio()
    {
        var mode = InvokeResolveRuntimeMode(
            "codex",
            HarnessExecutionMode.Default,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        Assert.That(mode).IsEqualTo("stdio");
    }

    [Test]
    public void ResolveRuntimeMode_WhenCustomHarnessNoRuntimeMode_UsesRequestedMode()
    {
        var mode = InvokeResolveRuntimeMode(
            "thirdparty",
            HarnessExecutionMode.Review,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        Assert.That(mode).IsEqualTo("review");
    }

    [Test]
    public void ResolveRuntimeMode_WhenNothingSpecified_DefaultsToServerTransport()
    {
        var mode = InvokeResolveRuntimeMode(
            "opencode",
            HarnessExecutionMode.Default,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        Assert.That(mode).IsEqualTo("sse");
    }

    [Test]
    public void ResolveRuntimeMode_WhenHarnessRuntimeModeExists_UsesHarnessRuntimeTransport()
    {
        var mode = InvokeResolveRuntimeMode(
            "opencode",
            HarnessExecutionMode.Review,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["HARNESS_RUNTIME_MODE"] = "stdio",
                ["HARNESS_MODE"] = "review",
            });

        Assert.That(mode).IsEqualTo("sse");
    }

    [Test]
    public void ResolveRuntimeMode_WhenNoRuntimeMode_UsesHarnessModeOverRequestedMode()
    {
        var mode = InvokeResolveRuntimeMode(
            "codex",
            HarnessExecutionMode.Review,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["HARNESS_MODE"] = "plan",
            });

        Assert.That(mode).IsEqualTo("stdio");
    }

    [Test]
    public void ResolveRuntimeMode_WhenUnsupportedHarnessHonorsProvidedRuntimeMode()
    {
        var mode = InvokeResolveRuntimeMode(
            "custom",
            HarnessExecutionMode.Default,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["HARNESS_RUNTIME_MODE"] = "ws",
                ["HARNESS_MODE"] = "plan",
            });

        Assert.That(mode).IsEqualTo("ws");
    }

    private static string InvokeResolveRuntimeMode(
        string harness,
        HarnessExecutionMode requestedMode,
        IReadOnlyDictionary<string, string> environment)
    {
        return (string)ResolveRuntimeModeMethod.Invoke(null, [harness, requestedMode, environment])!;
    }
}
