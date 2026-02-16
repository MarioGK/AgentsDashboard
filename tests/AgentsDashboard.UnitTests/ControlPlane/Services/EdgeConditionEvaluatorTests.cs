using System.Text.Json;
using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Services;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class EdgeConditionEvaluatorTests
{
    private static Dictionary<string, JsonElement> Context(params (string Key, object Value)[] entries)
    {
        var dict = new Dictionary<string, JsonElement>();
        foreach (var (key, value) in entries)
            dict[key] = JsonSerializer.SerializeToElement(value);
        return dict;
    }

    [Test]
    public void NullCondition_ReturnsTrue()
    {
        var result = EdgeConditionEvaluator.Evaluate(null!, null, null, []);

        result.Should().BeTrue();
    }

    [Test]
    public void EmptyCondition_ReturnsTrue()
    {
        var result = EdgeConditionEvaluator.Evaluate("", null, null, []);

        result.Should().BeTrue();
    }

    [Test]
    public void WhitespaceCondition_ReturnsTrue()
    {
        var result = EdgeConditionEvaluator.Evaluate("   ", null, null, []);

        result.Should().BeTrue();
    }

    [Test]
    public void RunStateEquals_Succeeded_ReturnsTrue()
    {
        var run = new RunDocument { State = RunState.Succeeded };

        var result = EdgeConditionEvaluator.Evaluate("run.state == Succeeded", null, run, []);

        result.Should().BeTrue();
    }

    [Test]
    public void RunStateEquals_Failed_ReturnsFalse()
    {
        var run = new RunDocument { State = RunState.Succeeded };

        var result = EdgeConditionEvaluator.Evaluate("run.state == Failed", null, run, []);

        result.Should().BeFalse();
    }

    [Test]
    public void RunStateNotEquals_Succeeded_ReturnsTrue()
    {
        var run = new RunDocument { State = RunState.Failed };

        var result = EdgeConditionEvaluator.Evaluate("run.state != Succeeded", null, run, []);

        result.Should().BeTrue();
    }

    [Test]
    public void ContextKeyEquals_ReturnsTrue()
    {
        var ctx = Context(("status", "ready"));

        var result = EdgeConditionEvaluator.Evaluate("context.status == ready", null, null, ctx);

        result.Should().BeTrue();
    }

    [Test]
    public void ContextKeyNumericGreaterThan_ReturnsTrue()
    {
        var ctx = Context(("score", 85));

        var result = EdgeConditionEvaluator.Evaluate("context.score > 50", null, null, ctx);

        result.Should().BeTrue();
    }

    [Test]
    public void ContextKeyNumericLessThan_ReturnsTrue()
    {
        var ctx = Context(("score", 30));

        var result = EdgeConditionEvaluator.Evaluate("context.score < 50", null, null, ctx);

        result.Should().BeTrue();
    }

    [Test]
    public void ContextKeyNumericGreaterEqual_ReturnsTrue()
    {
        var ctx = Context(("score", 50));

        var result = EdgeConditionEvaluator.Evaluate("context.score >= 50", null, null, ctx);

        result.Should().BeTrue();
    }

    [Test]
    public void ContextKeyNumericLessEqual_ReturnsTrue()
    {
        var ctx = Context(("score", 50));

        var result = EdgeConditionEvaluator.Evaluate("context.score <= 50", null, null, ctx);

        result.Should().BeTrue();
    }

    [Test]
    public void ContextKeyNotEquals_ReturnsTrue()
    {
        var ctx = Context(("env", "staging"));

        var result = EdgeConditionEvaluator.Evaluate("context.env != production", null, null, ctx);

        result.Should().BeTrue();
    }

    [Test]
    public void NodeStateEquals_ReturnsTrue()
    {
        var nodeResult = new WorkflowNodeResult { State = WorkflowNodeState.Succeeded };

        var result = EdgeConditionEvaluator.Evaluate("node.state == Succeeded", nodeResult, null, []);

        result.Should().BeTrue();
    }

    [Test]
    public void NodeAttemptEquals_ReturnsTrue()
    {
        var nodeResult = new WorkflowNodeResult { Attempt = 3 };

        var result = EdgeConditionEvaluator.Evaluate("node.attempt == 3", nodeResult, null, []);

        result.Should().BeTrue();
    }

    [Test]
    public void InvalidExpression_ReturnsFalse()
    {
        var result = EdgeConditionEvaluator.Evaluate("this is not a valid expression", null, null, []);

        result.Should().BeFalse();
    }

    [Test]
    public void MissingContextKey_ReturnsFalse()
    {
        var result = EdgeConditionEvaluator.Evaluate("context.missing == value", null, null, []);

        result.Should().BeFalse();
    }

    [Test]
    public void NullRun_WithRunCondition_ReturnsFalse()
    {
        var result = EdgeConditionEvaluator.Evaluate("run.state == Succeeded", null, null, []);

        result.Should().BeFalse();
    }

    [Test]
    public void CaseInsensitiveComparison_ReturnsTrue()
    {
        var run = new RunDocument { State = RunState.Succeeded };

        var result = EdgeConditionEvaluator.Evaluate("run.state == succeeded", null, run, []);

        result.Should().BeTrue();
    }
}
