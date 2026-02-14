using System.Text.Json;
using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.ControlPlane.Services;

public static class WorkflowContextMapper
{
    public static string ApplyInputMappings(
        Dictionary<string, string> mappings,
        Dictionary<string, JsonElement> context,
        string agentPrompt)
    {
        if (mappings.Count == 0)
            return agentPrompt;

        var result = agentPrompt;
        foreach (var (placeholder, contextKey) in mappings)
        {
            if (context.TryGetValue(contextKey, out var value))
            {
                var stringValue = value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? string.Empty
                    : value.GetRawText();
                result = result.Replace($"{{{{{placeholder}}}}}", stringValue);
            }
        }

        return result;
    }

    public static void ApplyOutputMappings(
        Dictionary<string, string> mappings,
        RunDocument? run,
        WorkflowNodeResult nodeResult,
        Dictionary<string, JsonElement> context)
    {
        foreach (var (contextKey, source) in mappings)
        {
            var value = ResolveOutputValue(source, run, nodeResult);
            if (value is not null)
                context[contextKey] = JsonSerializer.SerializeToElement(value);
        }
    }

    private static string? ResolveOutputValue(
        string source,
        RunDocument? run,
        WorkflowNodeResult nodeResult)
    {
        var parts = source.Split('.');
        if (parts.Length < 2)
            return null;

        var root = parts[0].ToLowerInvariant();
        var field = parts[1].ToLowerInvariant();

        return root switch
        {
            "run" => field switch
            {
                "state" => run?.State.ToString(),
                "summary" => run?.Summary,
                "prurl" => run?.PrUrl,
                "outputjson" => run?.OutputJson,
                "failureclass" => run?.FailureClass,
                _ => null
            },
            "node" => field switch
            {
                "state" => nodeResult.State.ToString(),
                "summary" => nodeResult.Summary,
                "attempt" => nodeResult.Attempt.ToString(),
                _ => null
            },
            _ => null
        };
    }
}
