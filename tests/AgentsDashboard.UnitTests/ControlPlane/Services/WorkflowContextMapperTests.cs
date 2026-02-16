using System.Text.Json;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Services;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class WorkflowContextMapperTests
{
    private static Dictionary<string, JsonElement> Context(params (string Key, object Value)[] entries)
    {
        var dict = new Dictionary<string, JsonElement>();
        foreach (var (key, value) in entries)
            dict[key] = JsonSerializer.SerializeToElement(value);
        return dict;
    }

    [Test]
    public void InputMappings_EmptyMappings_ReturnsOriginalPrompt()
    {
        var prompt = "Run the tests";

        var result = WorkflowContextMapper.ApplyInputMappings([], [], prompt);

        result.Should().Be("Run the tests");
    }

    [Test]
    public void InputMappings_SingleMapping_ReplacesPlaceholder()
    {
        var mappings = new Dictionary<string, string> { ["branch"] = "branchName" };
        var ctx = Context(("branchName", "feature/test"));
        var prompt = "Run tests on {{branch}}";

        var result = WorkflowContextMapper.ApplyInputMappings(mappings, ctx, prompt);

        result.Should().Be("Run tests on feature/test");
    }

    [Test]
    public void InputMappings_MultipleMapping_ReplacesAll()
    {
        var mappings = new Dictionary<string, string>
        {
            ["branch"] = "branchName",
            ["env"] = "environment"
        };
        var ctx = Context(("branchName", "main"), ("environment", "production"));
        var prompt = "Deploy {{branch}} to {{env}}";

        var result = WorkflowContextMapper.ApplyInputMappings(mappings, ctx, prompt);

        result.Should().Be("Deploy main to production");
    }

    [Test]
    public void InputMappings_MissingContextKey_LeavesPlaceholder()
    {
        var mappings = new Dictionary<string, string> { ["branch"] = "nonexistent" };
        var prompt = "Run tests on {{branch}}";

        var result = WorkflowContextMapper.ApplyInputMappings(mappings, [], prompt);

        result.Should().Be("Run tests on {{branch}}");
    }

    [Test]
    public void InputMappings_NumericValue_ReplacesCorrectly()
    {
        var mappings = new Dictionary<string, string> { ["count"] = "retryCount" };
        var ctx = Context(("retryCount", 42));
        var prompt = "Retry up to {{count}} times";

        var result = WorkflowContextMapper.ApplyInputMappings(mappings, ctx, prompt);

        result.Should().Be("Retry up to 42 times");
    }

    [Test]
    public void OutputMappings_RunSummary_WritesToContext()
    {
        var mappings = new Dictionary<string, string> { ["lastSummary"] = "run.summary" };
        var run = new RunDocument { Summary = "All tests passed" };
        var nodeResult = new WorkflowNodeResult { State = WorkflowNodeState.Succeeded };
        var ctx = new Dictionary<string, JsonElement>();

        WorkflowContextMapper.ApplyOutputMappings(mappings, run, nodeResult, ctx);

        ctx.Should().ContainKey("lastSummary");
        ctx["lastSummary"].GetString().Should().Be("All tests passed");
    }

    [Test]
    public void OutputMappings_RunState_WritesToContext()
    {
        var mappings = new Dictionary<string, string> { ["runState"] = "run.state" };
        var run = new RunDocument { State = RunState.Succeeded };
        var nodeResult = new WorkflowNodeResult();
        var ctx = new Dictionary<string, JsonElement>();

        WorkflowContextMapper.ApplyOutputMappings(mappings, run, nodeResult, ctx);

        ctx.Should().ContainKey("runState");
        ctx["runState"].GetString().Should().Be("Succeeded");
    }

    [Test]
    public void OutputMappings_RunPrUrl_WritesToContext()
    {
        var mappings = new Dictionary<string, string> { ["prLink"] = "run.prurl" };
        var run = new RunDocument { PrUrl = "https://github.com/org/repo/pull/42" };
        var nodeResult = new WorkflowNodeResult();
        var ctx = new Dictionary<string, JsonElement>();

        WorkflowContextMapper.ApplyOutputMappings(mappings, run, nodeResult, ctx);

        ctx.Should().ContainKey("prLink");
        ctx["prLink"].GetString().Should().Be("https://github.com/org/repo/pull/42");
    }

    [Test]
    public void OutputMappings_NodeState_WritesToContext()
    {
        var mappings = new Dictionary<string, string> { ["nodeState"] = "node.state" };
        var nodeResult = new WorkflowNodeResult { State = WorkflowNodeState.Succeeded };
        var ctx = new Dictionary<string, JsonElement>();

        WorkflowContextMapper.ApplyOutputMappings(mappings, null, nodeResult, ctx);

        ctx.Should().ContainKey("nodeState");
        ctx["nodeState"].GetString().Should().Be("Succeeded");
    }

    [Test]
    public void OutputMappings_NodeSummary_WritesToContext()
    {
        var mappings = new Dictionary<string, string> { ["nodeSummary"] = "node.summary" };
        var nodeResult = new WorkflowNodeResult { Summary = "Completed in 5s" };
        var ctx = new Dictionary<string, JsonElement>();

        WorkflowContextMapper.ApplyOutputMappings(mappings, null, nodeResult, ctx);

        ctx.Should().ContainKey("nodeSummary");
        ctx["nodeSummary"].GetString().Should().Be("Completed in 5s");
    }

    [Test]
    public void OutputMappings_NullRun_SkipsRunFields()
    {
        var mappings = new Dictionary<string, string> { ["summary"] = "run.summary" };
        var nodeResult = new WorkflowNodeResult();
        var ctx = new Dictionary<string, JsonElement>();

        WorkflowContextMapper.ApplyOutputMappings(mappings, null, nodeResult, ctx);

        ctx.Should().NotContainKey("summary");
    }

    [Test]
    public void OutputMappings_EmptyMappings_DoesNothing()
    {
        var run = new RunDocument { Summary = "Test" };
        var nodeResult = new WorkflowNodeResult();
        var ctx = new Dictionary<string, JsonElement>();

        WorkflowContextMapper.ApplyOutputMappings([], run, nodeResult, ctx);

        ctx.Should().BeEmpty();
    }
}
