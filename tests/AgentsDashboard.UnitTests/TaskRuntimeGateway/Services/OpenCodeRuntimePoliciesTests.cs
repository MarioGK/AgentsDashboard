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
    public async Task Resolve_WhenPlanMode_DeniesMutationPermissions()
    {
        var request = CreateRequest("plan");
        var policy = await Resolve(request);

        await Assert.That((await GetPropertyValue<object>(policy, "Mode")).ToString()).IsEqualTo("Plan");
        await Assert.That(await GetPropertyValue<string>(policy, "Agent")).IsEqualTo("plan");
        await Assert.That(await GetPropertyValue<string?>(policy, "SystemPrompt")).Contains("planning mode");
        await Assert.That(await GetPropertyValue<string?>(policy, "SystemPrompt")).Contains("Do not modify files");

        var rules = await GetPropertyValue<IReadOnlyList<object>?>(policy, "SessionPermissionRules");
        await Assert.That(rules).IsNotNull();
        await Assert.That(rules!.Count).IsGreaterThan(0);
        var permissions = await Task.WhenAll(rules.Select(rule => GetPropertyValue<string>(rule, "Permission")));
        await Assert.That(permissions).Contains("edit");
        await Assert.That(permissions).Contains("bash");

        var actions = await Task.WhenAll(rules.Select(rule => GetPropertyValue<string>(rule, "Action")));
        await Assert.That(actions.All(action => action == "deny")).IsTrue();
    }

    [Test]
    public async Task Resolve_WhenCommandModeFlagSetsReviewMode_UsesReviewPromptAndReviewPolicy()
    {
        var request = CreateRequest(
            string.Empty,
            command: "codex --mode readonly");

        var policy = await Resolve(request);

        await Assert.That((await GetPropertyValue<object>(policy, "Mode")).ToString()).IsEqualTo("Review");
        await Assert.That(await GetPropertyValue<string>(policy, "Agent")).IsEqualTo("plan");
        await Assert.That(await GetPropertyValue<string?>(policy, "SystemPrompt")).Contains("review mode");
        await Assert.That(await GetPropertyValue<string?>(policy, "SystemPrompt")).Contains("Do not modify files");
        await Assert.That(await GetPropertyValue<IReadOnlyList<object>?>(policy, "SessionPermissionRules")).IsNotNull();
    }

    [Test]
    public async Task Resolve_WhenCommandContainsReviewKeywords_UsesReviewModeWithoutModeArgument()
    {
        var request = CreateRequest(
            "default",
            command: "Please review this branch for defects and risks");

        var policy = await Resolve(request);

        await Assert.That((await GetPropertyValue<object>(policy, "Mode")).ToString()).IsEqualTo("Default");
    }

    [Test]
    public async Task Resolve_WhenModeArgumentAndCommandConflict_UsesModeArgument()
    {
        var request = CreateRequest(
            "plan",
            command: "run review for safety");

        var policy = await Resolve(request);

        await Assert.That((await GetPropertyValue<object>(policy, "Mode")).ToString()).IsEqualTo("Plan");
    }

    [Test]
    public async Task Resolve_WhenHarnessModeEnvOverridesInvalidCommandMode()
    {
        var request = CreateRequest(
            "plan",
            new Dictionary<string, string> { ["OPENCODE_MODE"] = "bogus-mode" },
            command: "codex --mode plan");

        var policy = await Resolve(request);

        await Assert.That((await GetPropertyValue<object>(policy, "Mode")).ToString()).IsEqualTo("Default");
    }

    [Test]
    public async Task Resolve_WhenHarnessModeEnvUsesAlias_MapsToReview()
    {
        var request = CreateRequest(
            "default",
            new Dictionary<string, string> { ["HARNESS_MODE"] = "audit" });

        var policy = await Resolve(request);

        await Assert.That((await GetPropertyValue<object>(policy, "Mode")).ToString()).IsEqualTo("Review");
    }

    [Test]
    public async Task Resolve_WhenReviewMode_UsesReviewPromptAndDenyRules()
    {
        var request = CreateRequest(
            "review",
            new Dictionary<string, string>
            {
                ["OPENCODE_REVIEW_AGENT"] = "reviewer"
            });

        var policy = await Resolve(request);

        await Assert.That((await GetPropertyValue<object>(policy, "Mode")).ToString()).IsEqualTo("Review");
        await Assert.That(await GetPropertyValue<string>(policy, "Agent")).IsEqualTo("reviewer");
        await Assert.That(await GetPropertyValue<string?>(policy, "SystemPrompt")).Contains("review mode");
        await Assert.That(await GetPropertyValue<string?>(policy, "SystemPrompt")).Contains("Do not modify files");
        await Assert.That(await GetPropertyValue<IReadOnlyList<object>?>(policy, "SessionPermissionRules")).IsNotNull();
    }

    [Test]
    public async Task Resolve_WhenDefaultMode_ReturnsBuildAgentWithoutDenyRules()
    {
        var request = CreateRequest("default");
        var policy = await Resolve(request);

        await Assert.That((await GetPropertyValue<object>(policy, "Mode")).ToString()).IsEqualTo("Default");
        await Assert.That(await GetPropertyValue<string>(policy, "Agent")).IsEqualTo("build");
        await Assert.That(await GetPropertyValue<string?>(policy, "SystemPrompt")).IsNull();
        await Assert.That(await GetPropertyValue<IReadOnlyList<object>?>(policy, "SessionPermissionRules")).IsNull();
    }

    private static async Task<object> Resolve(HarnessRunRequest request)
    {
        var resolved = ResolveMethod.Invoke(null, [request]);
        await Assert.That(resolved).IsNotNull();
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
            Command = command ?? string.Empty,
            Timeout = TimeSpan.FromMinutes(5),
        };
    }

    private static async Task<T> GetPropertyValue<T>(object source, string propertyName)
    {
        var property = source.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        await Assert.That(property).IsNotNull();
        return (T)property!.GetValue(source)!;
    }
}
