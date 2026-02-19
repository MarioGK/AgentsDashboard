using System.Reflection;
using AgentsDashboard.TaskRuntime.Services.HarnessRuntimes;

namespace AgentsDashboard.UnitTests.TaskRuntime.Services;

public sealed class CodexAppServerRuntimeTests
{
    private static readonly MethodInfo ResolveApprovalPolicyMethod = typeof(CodexAppServerRuntime)
        .GetMethod("ResolveApprovalPolicy", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ApplyModePromptMethod = typeof(CodexAppServerRuntime)
        .GetMethod("ApplyModePrompt", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ResolveExecutionModeMethod = typeof(CodexAppServerRuntime)
        .GetMethod("ResolveExecutionMode", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ResolveModelMethod = typeof(CodexAppServerRuntime)
        .GetMethod("ResolveModel", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Test]
    public void ResolveApprovalPolicy_WhenDefaultMode_UsesMutationCapableDefault()
    {
        var result = (string)ResolveApprovalPolicyMethod.Invoke(null, [new Dictionary<string, string>(), "default"])!;
        Assert.That(result).IsEqualTo("on-failure");
    }

    [Test]
    public void ResolveApprovalPolicy_WhenReadOnlyMode_UsesNeverApproval()
    {
        var result = (string)ResolveApprovalPolicyMethod.Invoke(null, [new Dictionary<string, string>(), "review"])!;
        Assert.That(result).IsEqualTo("never");
    }

    [Test]
    public void ResolveApprovalPolicy_WhenExplicitPolicySet_PrefersEnvironmentOverride()
    {
        var env = new Dictionary<string, string>
        {
            ["CODEX_APPROVAL_POLICY"] = "on-request"
        };

        var result = (string)ResolveApprovalPolicyMethod.Invoke(null, [env, "plan"])!;
        Assert.That(result).IsEqualTo("on-request");
    }

    [Test]
    public void ApplyModePrompt_WhenPlanMode_PrefixesReadOnlyInstructions()
    {
        var prompt = (string)ApplyModePromptMethod.Invoke(null, ["Implement feature X", "plan"])!;

        Assert.That(prompt).Contains("Execution mode: plan");
        Assert.That(prompt).Contains("Do not modify files");
        Assert.That(prompt).Contains("Implement feature X");
    }

    [Test]
    public void ResolveExecutionMode_PrioritizesHarnessAndTaskMode()
    {
        var mode = (string)ResolveExecutionModeMethod.Invoke(null, ["plan", new Dictionary<string, string>
        {
            ["HARNESS_MODE"] = "review",
            ["TASK_MODE"] = "plan",
        }])!;

        Assert.That(mode).IsEqualTo("review");
    }

    [Test]
    public void ResolveExecutionMode_RetainsNormalizedAliases()
    {
        var mode = (string)ResolveExecutionModeMethod.Invoke(null, ["planning", new Dictionary<string, string>
        {
            ["RUN_MODE"] = "audit",
        }])!;

        Assert.That(mode).IsEqualTo("review");
    }

    [Test]
    public void ResolveModel_UsesCodexModelOverHarnessModel()
    {
        var model = (string?)ResolveModelMethod.Invoke(null, [new Dictionary<string, string>
        {
            ["CODEX_MODEL"] = "codex-model",
            ["HARNESS_MODEL"] = "harness-model",
        }])!;

        Assert.That(model).IsEqualTo("codex-model");
    }

    [Test]
    public void ResolveModel_FallsBackToHarnessModel()
    {
        var model = (string?)ResolveModelMethod.Invoke(null, [new Dictionary<string, string>
        {
            ["HARNESS_MODEL"] = "harness-model",
        }]);

        Assert.That(model).IsEqualTo("harness-model");
    }
}
