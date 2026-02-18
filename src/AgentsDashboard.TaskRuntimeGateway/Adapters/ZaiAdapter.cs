using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.TaskRuntimeGateway.Configuration;
using AgentsDashboard.TaskRuntimeGateway.Services;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.TaskRuntimeGateway.Adapters;

public sealed class ZaiAdapter(
    IOptions<TaskRuntimeOptions> options,
    SecretRedactor secretRedactor,
    ILogger<ZaiAdapter> logger) : HarnessAdapterBase(options, secretRedactor, logger)
{
    public override string HarnessName => "zai";

    protected override void AddHarnessSpecificArguments(HarnessExecutionContext context, List<string> args)
    {
        args.AddRange(new[]
        {
            "-e", "CLAUDE_CODE_FORMAT=json",
            "-e", "CLAUDE_OUTPUT_ENVELOPE=true",
        });

        var model = GetEnvValue(context, "ZAI_MODEL", "HARNESS_MODEL");
        if (!string.IsNullOrWhiteSpace(model))
        {
            args.Add("-e");
            args.Add($"ZAI_MODEL={model}");
        }

        if (context.Env.TryGetValue("Z_AI_API_KEY", out var apiKey) && !string.IsNullOrWhiteSpace(apiKey))
        {
            args.Add("-e");
            args.Add($"Z_AI_API_KEY={EscapeEnv(apiKey)}");
        }

        var maxTokens = GetEnvValue(context, "ZAI_MAX_THINKING_TOKENS", "HARNESS_MAX_TOKENS");
        if (!string.IsNullOrWhiteSpace(maxTokens))
        {
            args.Add("-e");
            args.Add($"ZAI_MAX_THINKING_TOKENS={maxTokens}");
        }
    }

    private static string? GetEnvValue(HarnessExecutionContext context, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (context.Env.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
                return value;
        }
        return null;
    }

    public override FailureClassification ClassifyFailure(HarnessResultEnvelope envelope)
    {
        if (string.Equals(envelope.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
            return FailureClassification.Success();

        var error = string.IsNullOrEmpty(envelope.Error) ? (envelope.Summary ?? string.Empty) : envelope.Error;
        var lowerError = error.ToLowerInvariant();

        if (ContainsAny(lowerError, "overloaded", "capacity", "service unavailable", "503"))
        {
            return FailureClassification.FromClass(
                FailureClass.RateLimitExceeded,
                "GLM-5 service overloaded",
                true,
                60,
                "Wait and retry",
                "Consider reducing request frequency");
        }

        if (ContainsAny(lowerError, "prompt too long", "context length", "max tokens", "input too long"))
        {
            return FailureClassification.FromClass(
                FailureClass.ResourceExhausted,
                "Context too long",
                true,
                30,
                "Reduce prompt size",
                "Use context window efficiently",
                "Split into multiple requests");
        }

        if (ContainsAny(lowerError, "content policy", "safety", "refused", "harmful", "violation"))
        {
            return FailureClassification.FromClass(
                FailureClass.InvalidInput,
                "Content policy violation",
                false,
                0,
                "Review prompt content",
                "Avoid policy-violating requests");
        }

        if (ContainsAny(lowerError, "tool", "function call", "server connection"))
        {
            return FailureClassification.FromClass(
                FailureClass.ConfigurationError,
                "Tool error",
                false,
                0,
                "Check tool configuration",
                "Verify tool availability",
                "Check server logs");
        }

        if (ContainsAny(lowerError, "permission", "approval", "denied", "user rejected"))
        {
            return FailureClassification.FromClass(
                FailureClass.PermissionDenied,
                "Permission denied",
                false,
                0,
                "Check permission settings",
                "Review approval requirements");
        }

        if (ContainsAny(lowerError, "zai", "cc-mirror", "cli", "binary", "installation"))
        {
            return FailureClassification.FromClass(
                FailureClass.InternalError,
                "Zai CLI error",
                true,
                30,
                "Check Zai installation: npx cc-mirror quick --provider zai --api-key \"$Z_AI_API_KEY\"",
                "Verify cc-mirror is installed");
        }

        if (ContainsAny(lowerError, "glm-5", "model", "api"))
        {
            return FailureClassification.FromClass(
                FailureClass.ConfigurationError,
                "GLM-5 API error",
                true,
                30,
                "Check Z_AI_API_KEY is set",
                "Verify API endpoint is accessible",
                "Check model availability");
        }

        return base.ClassifyByErrorPatterns(lowerError);
    }

    public override IReadOnlyList<HarnessArtifact> MapArtifacts(HarnessResultEnvelope envelope)
    {
        var artifacts = base.MapArtifacts(envelope);

        if (envelope.Metadata.TryGetValue("editedFiles", out var editedFilesStr) &&
            !string.IsNullOrWhiteSpace(editedFilesStr))
        {
            var editedFiles = editedFilesStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var file in editedFiles)
            {
                if (!artifacts.Any(a => a.Path.Equals(file, StringComparison.OrdinalIgnoreCase)))
                {
                    artifacts = artifacts.Append(new HarnessArtifact
                    {
                        Name = Path.GetFileName(file),
                        Path = file,
                        Type = DetermineArtifactType(file)
                    }).ToList();
                }
            }
        }

        if (envelope.Metadata.TryGetValue("thinkingOutput", out var thinkingPath) && !string.IsNullOrWhiteSpace(thinkingPath))
        {
            artifacts = artifacts.Append(new HarnessArtifact
            {
                Name = "thinking.md",
                Path = thinkingPath,
                Type = "markdown"
            }).ToList();
        }

        if (envelope.Metadata.TryGetValue("toolUseLog", out var toolLogPath) && !string.IsNullOrWhiteSpace(toolLogPath))
        {
            artifacts = artifacts.Append(new HarnessArtifact
            {
                Name = Path.GetFileName(toolLogPath),
                Path = toolLogPath,
                Type = "json"
            }).ToList();
        }

        return artifacts;
    }

    public override HarnessResultEnvelope ParseEnvelope(string stdout, string stderr, int exitCode)
    {
        var envelope = base.ParseEnvelope(stdout, stderr, exitCode);

        if (envelope.Metadata.TryGetValue("thinking", out var thinking) && !string.IsNullOrWhiteSpace(thinking))
        {
            envelope.Metadata["hasThinking"] = "true";
        }

        if (envelope.Metadata.TryGetValue("toolCalls", out var toolCalls) && !string.IsNullOrWhiteSpace(toolCalls))
        {
            envelope.Metadata["toolCallCount"] = toolCalls.Split(',').Length.ToString();
        }

        return envelope;
    }
}
