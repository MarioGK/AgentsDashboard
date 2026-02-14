using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.WorkerGateway.Configuration;
using AgentsDashboard.WorkerGateway.Services;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.WorkerGateway.Adapters;

public sealed class ClaudeCodeAdapter(
    IOptions<WorkerOptions> options,
    SecretRedactor secretRedactor,
    ILogger<ClaudeCodeAdapter> logger) : HarnessAdapterBase(options, secretRedactor, logger)
{
    public override string HarnessName => "claude-code";

    protected override void AddHarnessSpecificArguments(HarnessExecutionContext context, List<string> args)
    {
        args.AddRange(new[]
        {
            "-e", "CLAUDE_CODE_FORMAT=json",
            "-e", "CLAUDE_OUTPUT_ENVELOPE=true",
        });

        var model = GetEnvValue(context, "CLAUDE_MODEL", "ANTHROPIC_MODEL", "HARNESS_MODEL");
        if (!string.IsNullOrWhiteSpace(model))
        {
            args.Add("-e");
            args.Add($"CLAUDE_MODEL={model}");
            args.Add("-e");
            args.Add($"ANTHROPIC_MODEL={model}");
        }

        if (context.Env.TryGetValue("CLAUDE_DANGEROUSLY_SKIP_PERMISSIONS", out var skipPerms) &&
            string.Equals(skipPerms, "true", StringComparison.OrdinalIgnoreCase))
        {
            args.Add("-e");
            args.Add("CLAUDE_DANGEROUSLY_SKIP_PERMISSIONS=true");
        }

        var maxTokens = GetEnvValue(context, "CLAUDE_MAX_THINKING_TOKENS", "HARNESS_MAX_TOKENS");
        if (!string.IsNullOrWhiteSpace(maxTokens))
        {
            args.Add("-e");
            args.Add($"CLAUDE_MAX_THINKING_TOKENS={maxTokens}");
        }

        if (context.Env.TryGetValue("CLAUDE_MCP_SERVERS", out var mcpServers) && !string.IsNullOrWhiteSpace(mcpServers))
        {
            args.Add("-e");
            args.Add($"CLAUDE_MCP_SERVERS={EscapeEnv(mcpServers)}");
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
                "Claude service overloaded",
                true,
                60,
                "Wait and retry",
                "Consider using a different model");
        }

        if (ContainsAny(lowerError, "prompt too long", "context length", "max tokens"))
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

        if (ContainsAny(lowerError, "content policy", "safety", "refused", "harmful"))
        {
            return FailureClassification.FromClass(
                FailureClass.InvalidInput,
                "Content policy violation",
                false,
                0,
                "Review prompt content",
                "Avoid policy-violating requests");
        }

        if (ContainsAny(lowerError, "mcp", "tool", "server connection"))
        {
            return FailureClassification.FromClass(
                FailureClass.ConfigurationError,
                "MCP tool error",
                false,
                0,
                "Check MCP server configuration",
                "Verify tool availability",
                "Check MCP server logs");
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

        if (ContainsAny(lowerError, "claude code", "cli", "binary"))
        {
            return FailureClassification.FromClass(
                FailureClass.InternalError,
                "Claude Code CLI error",
                true,
                30,
                "Check Claude Code installation",
                "Verify CLI version");
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
