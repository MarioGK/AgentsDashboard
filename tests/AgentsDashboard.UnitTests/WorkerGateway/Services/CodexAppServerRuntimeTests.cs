using System.Reflection;
using AgentsDashboard.WorkerGateway.Services.HarnessRuntimes;

namespace AgentsDashboard.UnitTests.WorkerGateway.Services;

public sealed class CodexAppServerRuntimeTests
{
    private static readonly MethodInfo ResolveApprovalPolicyMethod = typeof(CodexAppServerRuntime)
        .GetMethod("ResolveApprovalPolicy", BindingFlags.NonPublic | BindingFlags.Static)!;

    private static readonly MethodInfo ApplyModePromptMethod = typeof(CodexAppServerRuntime)
        .GetMethod("ApplyModePrompt", BindingFlags.NonPublic | BindingFlags.Static)!;

    [Test]
    public void ResolveApprovalPolicy_WhenDefaultMode_UsesMutationCapableDefault()
    {
        var result = (string)ResolveApprovalPolicyMethod.Invoke(null, [new Dictionary<string, string>(), "default"])!;
        result.Should().Be("on-failure");
    }

    [Test]
    public void ResolveApprovalPolicy_WhenReadOnlyMode_UsesNeverApproval()
    {
        var result = (string)ResolveApprovalPolicyMethod.Invoke(null, [new Dictionary<string, string>(), "review"])!;
        result.Should().Be("never");
    }

    [Test]
    public void ResolveApprovalPolicy_WhenExplicitPolicySet_PrefersEnvironmentOverride()
    {
        var env = new Dictionary<string, string>
        {
            ["CODEX_APPROVAL_POLICY"] = "on-request"
        };

        var result = (string)ResolveApprovalPolicyMethod.Invoke(null, [env, "plan"])!;
        result.Should().Be("on-request");
    }

    [Test]
    public void ApplyModePrompt_WhenPlanMode_PrefixesReadOnlyInstructions()
    {
        var prompt = (string)ApplyModePromptMethod.Invoke(null, ["Implement feature X", "plan"])!;

        prompt.Should().Contain("Execution mode: plan");
        prompt.Should().Contain("Do not modify files");
        prompt.Should().Contain("Implement feature X");
    }
}
