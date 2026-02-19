using System.Reflection;
using AgentsDashboard.TaskRuntime.Services.HarnessRuntimes;

namespace AgentsDashboard.UnitTests.TaskRuntime.Services;

public sealed class OpenCodeRuntimePoliciesTests
{
    private static readonly Type PoliciesType = typeof(OpenCodeSseRuntime).Assembly
        .GetType("AgentsDashboard.TaskRuntime.Services.HarnessRuntimes.OpenCodeRuntimePolicies")!;

    private static readonly MethodInfo ResolveMethod = PoliciesType
        .GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static, [typeof(HarnessRunRequest)])!;

    [Test]
    public void Resolve_WhenPlanMode_DeniesMutationPermissions()
    {
        var request = CreateRequest("plan");
        var policy = Resolve(request);

        Assert.That(GetPropertyValue<object>(policy, "Mode").ToString()).IsEqualTo("Plan");
        Assert.That(GetPropertyValue<string>(policy, "Agent")).IsEqualTo("plan");
        Assert.That(GetPropertyValue<string?>(policy, "SystemPrompt")).Contains("planning mode");
        Assert.That(GetPropertyValue<string?>(policy, "SystemPrompt")).Contains("Do not modify files");

        var rules = GetPropertyValue<IReadOnlyList<object>?>(policy, "SessionPermissionRules");
        Assert.That(rules).IsNotNull();
        Assert.That(rules!.Count).IsGreaterThan(0);
        Assert.That(rules.Select(rule => GetPropertyValue<string>(rule, "Permission"))).Contains(["edit", "bash"]);
        Assert.That(rules.Select(rule => GetPropertyValue<string>(rule, "Action")).All(action => action == "deny")).IsTrue();
    }

    [Test]
    public void Resolve_WhenCommandModeFlagSetsReviewMode_UsesReviewPromptAndReviewPolicy()
    {
        var request = CreateRequest(
            "default",
            command: "codex --mode readonly");

        var policy = Resolve(request);

        Assert.That(GetPropertyValue<object>(policy, "Mode").ToString()).IsEqualTo("Review");
        Assert.That(GetPropertyValue<string>(policy, "Agent")).IsEqualTo("plan");
        Assert.That(GetPropertyValue<string?>(policy, "SystemPrompt")).Contains("review mode");
        Assert.That(GetPropertyValue<string?>(policy, "SystemPrompt")).Contains("Do not modify files");
        Assert.That(GetPropertyValue<IReadOnlyList<object>?>(policy, "SessionPermissionRules")).IsNotNull();
    }

    [Test]
    public void Resolve_WhenCommandContainsReviewKeywords_UsesReviewModeWithoutModeArgument()
    {
        var request = CreateRequest(
            "default",
            command: "Please review this branch for defects and risks");

        var policy = Resolve(request);

        Assert.That(GetPropertyValue<object>(policy, "Mode").ToString()).IsEqualTo("Review");
    }

    [Test]
    public void Resolve_WhenModeArgumentAndCommandConflict_UsesModeArgument()
    {
        var request = CreateRequest(
            "plan",
            command: "run review for safety");

        var policy = Resolve(request);

        Assert.That(GetPropertyValue<object>(policy, "Mode").ToString()).IsEqualTo("Plan");
    }

    [Test]
    public void Resolve_WhenHarnessModeEnvOverridesInvalidCommandMode()
    {
        var request = CreateRequest(
            "plan",
            new Dictionary<string, string> { ["OPENCODE_MODE"] = "bogus-mode" },
            command: "codex --mode plan");

        var policy = Resolve(request);

        Assert.That(GetPropertyValue<object>(policy, "Mode").ToString()).IsEqualTo("Default");
    }

    [Test]
    public void Resolve_WhenHarnessModeEnvUsesAlias_MapsToReview()
    {
        var request = CreateRequest(
            "default",
            new Dictionary<string, string> { ["HARNESS_MODE"] = "audit" });

        var policy = Resolve(request);

        Assert.That(GetPropertyValue<object>(policy, "Mode").ToString()).IsEqualTo("Review");
    }

    [Test]
    public void Resolve_WhenReviewMode_UsesReviewPromptAndDenyRules()
    {
        var request = CreateRequest(
            "review",
            new Dictionary<string, string>
            {
                ["OPENCODE_REVIEW_AGENT"] = "reviewer"
            });

        var policy = Resolve(request);

        Assert.That(GetPropertyValue<object>(policy, "Mode").ToString()).IsEqualTo("Review");
        Assert.That(GetPropertyValue<string>(policy, "Agent")).IsEqualTo("reviewer");
        Assert.That(GetPropertyValue<string?>(policy, "SystemPrompt")).Contains("review mode");
        Assert.That(GetPropertyValue<string?>(policy, "SystemPrompt")).Contains("Do not modify files");
        Assert.That(GetPropertyValue<IReadOnlyList<object>?>(policy, "SessionPermissionRules")).IsNotNull();
    }

    [Test]
    public void Resolve_WhenDefaultMode_ReturnsBuildAgentWithoutDenyRules()
    {
        var request = CreateRequest("default");
        var policy = Resolve(request);

        Assert.That(GetPropertyValue<object>(policy, "Mode").ToString()).IsEqualTo("Default");
        Assert.That(GetPropertyValue<string>(policy, "Agent")).IsEqualTo("build");
        Assert.That(GetPropertyValue<string?>(policy, "SystemPrompt")).IsNull();
        Assert.That(GetPropertyValue<IReadOnlyList<object>?>(policy, "SessionPermissionRules")).IsNull();
    }

    private static object Resolve(HarnessRunRequest request)
    {
        var resolved = ResolveMethod.Invoke(null, [request]);
        Assert.That(resolved).IsNotNull();
        return resolved!;
    }

    private static HarnessRunRequest CreateRequest(
        string mode,
        Dictionary<string, string>? environment = null,
        string? command = null)
    {
        return new HarnessRunRequest
        {
            RunId = "run-1",
            TaskId = "task-1",
            Harness = "opencode",
            Mode = mode,
            Prompt = "prompt",
            WorkspacePath = "/tmp",
            Environment = environment ?? new Dictionary<string, string>(),
            Command = command,
            Timeout = TimeSpan.FromMinutes(5),
        };
    }

    private static T GetPropertyValue<T>(object source, string propertyName)
    {
        var property = source.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(property).IsNotNull();
        return (T)property!.GetValue(source)!;
    }
}
