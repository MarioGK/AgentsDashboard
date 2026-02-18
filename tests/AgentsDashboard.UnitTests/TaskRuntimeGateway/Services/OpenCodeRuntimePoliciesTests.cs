using System.Reflection;
using AgentsDashboard.TaskRuntimeGateway.Services.HarnessRuntimes;

namespace AgentsDashboard.UnitTests.WorkerGateway.Services;

public sealed class OpenCodeRuntimePoliciesTests
{
    private static readonly Type PoliciesType = typeof(OpenCodeSseRuntime).Assembly
        .GetType("AgentsDashboard.TaskRuntimeGateway.Services.HarnessRuntimes.OpenCodeRuntimePolicies")!;

    private static readonly MethodInfo ResolveMethod = PoliciesType
        .GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static, [typeof(HarnessRunRequest)])!;

    [Test]
    public void Resolve_WhenPlanMode_DeniesMutationPermissions()
    {
        var request = CreateRequest("plan");
        var policy = Resolve(request);

        GetPropertyValue<object>(policy, "Mode").ToString().Should().Be("Plan");
        GetPropertyValue<string>(policy, "Agent").Should().Be("plan");
        GetPropertyValue<string?>(policy, "SystemPrompt")
            .Should().Contain("planning mode")
            .And.Contain("Do not modify files");

        var rules = GetPropertyValue<IReadOnlyList<object>?>(policy, "SessionPermissionRules");
        rules.Should().NotBeNull();
        rules!.Count.Should().BeGreaterThan(0);
        rules.Select(rule => GetPropertyValue<string>(rule, "Permission")).Should().Contain(["edit", "bash"]);
        rules.Select(rule => GetPropertyValue<string>(rule, "Action")).Should().OnlyContain(action => action == "deny");
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

        GetPropertyValue<object>(policy, "Mode").ToString().Should().Be("Review");
        GetPropertyValue<string>(policy, "Agent").Should().Be("reviewer");
        GetPropertyValue<string?>(policy, "SystemPrompt")
            .Should().Contain("review mode")
            .And.Contain("Do not modify files");
        GetPropertyValue<IReadOnlyList<object>?>(policy, "SessionPermissionRules").Should().NotBeNull();
    }

    [Test]
    public void Resolve_WhenDefaultMode_ReturnsBuildAgentWithoutDenyRules()
    {
        var request = CreateRequest("default");
        var policy = Resolve(request);

        GetPropertyValue<object>(policy, "Mode").ToString().Should().Be("Default");
        GetPropertyValue<string>(policy, "Agent").Should().Be("build");
        GetPropertyValue<string?>(policy, "SystemPrompt").Should().BeNull();
        GetPropertyValue<IReadOnlyList<object>?>(policy, "SessionPermissionRules").Should().BeNull();
    }

    private static object Resolve(HarnessRunRequest request)
    {
        var resolved = ResolveMethod.Invoke(null, [request]);
        resolved.Should().NotBeNull();
        return resolved!;
    }

    private static HarnessRunRequest CreateRequest(string mode, Dictionary<string, string>? environment = null)
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
            Timeout = TimeSpan.FromMinutes(5),
        };
    }

    private static T GetPropertyValue<T>(object source, string propertyName)
    {
        var property = source.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        property.Should().NotBeNull();
        return (T)property!.GetValue(source)!;
    }
}
