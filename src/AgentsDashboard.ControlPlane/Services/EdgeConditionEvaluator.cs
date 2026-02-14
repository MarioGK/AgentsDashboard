using System.Text.Json;
using System.Text.RegularExpressions;
using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.ControlPlane.Services;

public static partial class EdgeConditionEvaluator
{
    private static readonly Regex ConditionPattern = ConditionRegex();

    public static bool Evaluate(
        string condition,
        WorkflowNodeResult? nodeResult,
        RunDocument? run,
        Dictionary<string, JsonElement> context)
    {
        if (string.IsNullOrWhiteSpace(condition))
            return true;

        var match = ConditionPattern.Match(condition.Trim());
        if (!match.Success)
            return false;

        var left = match.Groups[1].Value;
        var op = match.Groups[2].Value;
        var right = match.Groups[3].Value.Trim().Trim('"');

        var leftValue = ResolveOperand(left, nodeResult, run, context);
        if (leftValue is null)
            return false;

        return Compare(leftValue, op, right);
    }

    private static string? ResolveOperand(
        string path,
        WorkflowNodeResult? nodeResult,
        RunDocument? run,
        Dictionary<string, JsonElement> context)
    {
        var parts = path.Split('.');
        if (parts.Length < 2)
        {
            if (context.TryGetValue(path, out var directVal))
                return JsonElementToString(directVal);
            return null;
        }

        var root = parts[0].ToLowerInvariant();
        var field = parts[1].ToLowerInvariant();

        return root switch
        {
            "run" => field switch
            {
                "state" => run?.State.ToString(),
                "summary" => run?.Summary,
                "attempt" => run?.Attempt.ToString(),
                "failureclass" => run?.FailureClass,
                _ => null
            },
            "node" => field switch
            {
                "state" => nodeResult?.State.ToString(),
                "summary" => nodeResult?.Summary,
                "attempt" => nodeResult?.Attempt.ToString(),
                "type" => nodeResult?.NodeType.ToString(),
                _ => null
            },
            "context" => context.TryGetValue(field, out var val) ? JsonElementToString(val) : null,
            _ => context.TryGetValue(path, out var fallback) ? JsonElementToString(fallback) : null
        };
    }

    private static string JsonElementToString(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => element.GetRawText()
        };
    }

    private static bool Compare(string leftValue, string op, string rightValue)
    {
        if (double.TryParse(leftValue, out var leftNum) && double.TryParse(rightValue, out var rightNum))
        {
            return op switch
            {
                "==" => Math.Abs(leftNum - rightNum) < 0.0001,
                "!=" => Math.Abs(leftNum - rightNum) >= 0.0001,
                ">" => leftNum > rightNum,
                ">=" => leftNum >= rightNum,
                "<" => leftNum < rightNum,
                "<=" => leftNum <= rightNum,
                _ => false
            };
        }

        return op switch
        {
            "==" => string.Equals(leftValue, rightValue, StringComparison.OrdinalIgnoreCase),
            "!=" => !string.Equals(leftValue, rightValue, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    [GeneratedRegex(@"^(\w+(?:\.\w+)*)\s*(==|!=|>=?|<=?)\s*(.+)$")]
    private static partial Regex ConditionRegex();
}
