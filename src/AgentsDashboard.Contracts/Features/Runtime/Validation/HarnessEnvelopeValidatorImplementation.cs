using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgentsDashboard.Contracts.Features.Runtime.Validation;


public sealed class HarnessEnvelopeValidator
{
    private static readonly HashSet<string> s_validStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "succeeded", "failed", "unknown", "cancelled", "pending"
    };

    private static readonly HashSet<string> s_knownProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "runId", "taskId", "status", "summary", "error", "actions", "artifacts", "metrics", "metadata", "rawOutputRef"
    };

    private static readonly HashSet<string> s_actionProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "type", "description", "target"
    };

    public HarnessEnvelopeValidationResult Validate(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return HarnessEnvelopeValidationResult.Failure("JSON content is empty");
        }

        JsonNode? rootNode;
        try
        {
            rootNode = JsonNode.Parse(json);
        }
        catch (JsonException ex)
        {
            return HarnessEnvelopeValidationResult.Failure($"Invalid JSON: {ex.Message}");
        }

        if (rootNode is not JsonObject root)
        {
            return HarnessEnvelopeValidationResult.Failure("Root element must be an object");
        }

        var errors = new List<string>();
        var warnings = new List<string>();

        ValidateStatus(root, errors);
        ValidateRunId(root, warnings);
        ValidateTaskId(root, warnings);
        ValidateActions(root, errors);
        ValidateArtifacts(root, errors);
        ValidateMetrics(root, errors);
        ValidateMetadata(root, errors);
        ValidateRawOutputRef(root, warnings);
        ValidateUnknownProperties(root, warnings);

        return errors.Count > 0
            ? new HarnessEnvelopeValidationResult { IsValid = false, Errors = errors, Warnings = warnings }
            : new HarnessEnvelopeValidationResult { IsValid = true, Warnings = warnings };
    }

    private static void ValidateStatus(JsonObject root, List<string> errors)
    {
        if (!root.TryGetPropertyValue("status", out var statusNode))
        {
            errors.Add("Required property 'status' is missing");
            return;
        }

        if (statusNode is not JsonValue statusValue || !statusValue.TryGetValue(out string? status))
        {
            errors.Add("Property 'status' must be a string");
            return;
        }

        if (string.IsNullOrWhiteSpace(status))
        {
            errors.Add("Property 'status' cannot be empty");
            return;
        }

        if (!s_validStatuses.Contains(status))
        {
            errors.Add($"Property 'status' must be one of: {string.Join(", ", s_validStatuses)}. Got: '{status}'");
        }
    }

    private static void ValidateRunId(JsonObject root, List<string> warnings)
    {
        if (!root.TryGetPropertyValue("runId", out var runIdNode))
        {
            warnings.Add("Optional property 'runId' is missing");
            return;
        }

        if (runIdNode is not JsonValue runIdValue || !runIdValue.TryGetValue(out string? _))
        {
            warnings.Add("Property 'runId' should be a string");
        }
    }

    private static void ValidateTaskId(JsonObject root, List<string> warnings)
    {
        if (!root.TryGetPropertyValue("taskId", out var taskIdNode))
        {
            warnings.Add("Optional property 'taskId' is missing");
            return;
        }

        if (taskIdNode is not JsonValue taskIdValue || !taskIdValue.TryGetValue(out string? _))
        {
            warnings.Add("Property 'taskId' should be a string");
        }
    }

    private static void ValidateActions(JsonObject root, List<string> errors)
    {
        if (!root.TryGetPropertyValue("actions", out var actionsNode))
        {
            return;
        }

        if (actionsNode is not JsonArray actionsArray)
        {
            errors.Add("Property 'actions' must be an array");
            return;
        }

        for (var i = 0; i < actionsArray.Count; i++)
        {
            var actionNode = actionsArray[i];
            if (actionNode is not JsonObject actionObj)
            {
                errors.Add($"actions[{i}] must be an object");
                continue;
            }

            ValidateAction(actionObj, i, errors);
        }
    }

    private static void ValidateAction(JsonObject action, int index, List<string> errors)
    {
        if (!action.TryGetPropertyValue("type", out var typeNode))
        {
            errors.Add($"actions[{index}].type is required");
        }
        else if (typeNode is not JsonValue typeValue || !typeValue.TryGetValue(out string? type) || string.IsNullOrWhiteSpace(type))
        {
            errors.Add($"actions[{index}].type must be a non-empty string");
        }

        if (action.TryGetPropertyValue("description", out var descNode) && descNode is JsonValue descValue && !descValue.TryGetValue(out string? _))
        {
            errors.Add($"actions[{index}].description must be a string");
        }

        if (action.TryGetPropertyValue("target", out var targetNode) && targetNode is JsonValue targetValue && !targetValue.TryGetValue(out string? _))
        {
            errors.Add($"actions[{index}].target must be a string");
        }

        foreach (var prop in action)
        {
            if (!s_actionProperties.Contains(prop.Key))
            {
                errors.Add($"actions[{index}] contains unknown property '{prop.Key}'");
            }
        }
    }

    private static void ValidateArtifacts(JsonObject root, List<string> errors)
    {
        if (!root.TryGetPropertyValue("artifacts", out var artifactsNode))
        {
            return;
        }

        if (artifactsNode is not JsonArray artifactsArray)
        {
            errors.Add("Property 'artifacts' must be an array");
            return;
        }

        for (var i = 0; i < artifactsArray.Count; i++)
        {
            var item = artifactsArray[i];
            if (item is not JsonValue value || !value.TryGetValue(out string? artifact))
            {
                errors.Add($"artifacts[{i}] must be a string");
            }
            else if (string.IsNullOrWhiteSpace(artifact))
            {
                errors.Add($"artifacts[{i}] cannot be empty");
            }
        }
    }

    private static void ValidateMetrics(JsonObject root, List<string> errors)
    {
        if (!root.TryGetPropertyValue("metrics", out var metricsNode))
        {
            return;
        }

        if (metricsNode is not JsonObject metricsObj)
        {
            errors.Add("Property 'metrics' must be an object");
            return;
        }

        foreach (var prop in metricsObj)
        {
            if (prop.Value is not JsonValue value || !value.TryGetValue(out double _))
            {
                errors.Add($"metrics['{prop.Key}'] must be a number");
            }
        }
    }

    private static void ValidateMetadata(JsonObject root, List<string> errors)
    {
        if (!root.TryGetPropertyValue("metadata", out var metadataNode))
        {
            return;
        }

        if (metadataNode is not JsonObject metadataObj)
        {
            errors.Add("Property 'metadata' must be an object");
            return;
        }

        foreach (var prop in metadataObj)
        {
            if (prop.Value is not JsonValue value || !value.TryGetValue(out string? _))
            {
                errors.Add($"metadata['{prop.Key}'] must be a string");
            }
        }
    }

    private static void ValidateRawOutputRef(JsonObject root, List<string> warnings)
    {
        if (!root.TryGetPropertyValue("rawOutputRef", out var refNode))
        {
            return;
        }

        if (refNode is not JsonValue refValue || !refValue.TryGetValue(out string? _))
        {
            warnings.Add("Property 'rawOutputRef' should be a string");
        }
    }

    private static void ValidateUnknownProperties(JsonObject root, List<string> warnings)
    {
        foreach (var prop in root)
        {
            if (!s_knownProperties.Contains(prop.Key))
            {
                warnings.Add($"Unknown property '{prop.Key}' will be ignored");
            }
        }
    }
}
