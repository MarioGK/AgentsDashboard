using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.TaskRuntime.Configuration;
using AgentsDashboard.TaskRuntime.Services;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.TaskRuntime.Adapters;

public sealed class CodexAdapter(
    IOptions<TaskRuntimeOptions> options,
    SecretRedactor secretRedactor,
    ILogger<CodexAdapter> logger) : HarnessAdapterBase(options, secretRedactor, logger)
{
    public override string HarnessName => "codex";

    protected override void AddHarnessSpecificArguments(HarnessExecutionContext context, List<string> args)
    {
        args.AddRange(new[]
        {
            "-e", "CODEX_FORMAT=json",
            "-e", "CODEX_OUTPUT_ENVELOPE=true",
        });

        var model = GetEnvValue(context, "CODEX_MODEL", "HARNESS_MODEL");
        if (!string.IsNullOrWhiteSpace(model))
        {
            args.Add("-e");
            args.Add($"CODEX_MODEL={model}");
        }

        var maxTokens = GetEnvValue(context, "CODEX_MAX_TOKENS", "HARNESS_MAX_TOKENS");
        if (!string.IsNullOrWhiteSpace(maxTokens))
        {
            args.Add("-e");
            args.Add($"CODEX_MAX_TOKENS={maxTokens}");
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

        if (ContainsAny(lowerError, "codex", "sandbox", "execution environment"))
        {
            return FailureClassification.FromClass(
                FailureClass.InternalError,
                "Codex sandbox error",
                true,
                30,
                "Check Codex environment",
                "Verify sandbox configuration");
        }

        if (ContainsAny(lowerError, "tool", "function call", "tool_use"))
        {
            return FailureClassification.FromClass(
                FailureClass.InvalidInput,
                "Tool execution failed",
                false,
                0,
                "Check tool parameters",
                "Verify tool availability");
        }

        if (envelope.Metadata.TryGetValue("exitCode", out var exitCodeStr) &&
            int.TryParse(exitCodeStr, out var exitCode) &&
            exitCode == 137)
        {
            return FailureClassification.FromClass(
                FailureClass.ResourceExhausted,
                "Container killed (OOM)",
                true,
                60,
                "Increase memory limit",
                "Reduce task complexity");
        }

        return base.ClassifyByErrorPatterns(lowerError);
    }

    public override IReadOnlyList<HarnessArtifact> MapArtifacts(HarnessResultEnvelope envelope)
    {
        var artifacts = base.MapArtifacts(envelope);

        if (envelope.Metadata.TryGetValue("patchFile", out var patchFile) && !string.IsNullOrWhiteSpace(patchFile))
        {
            artifacts = artifacts.Append(new HarnessArtifact
            {
                Name = Path.GetFileName(patchFile),
                Path = patchFile,
                Type = "diff"
            }).ToList();
        }

        if (envelope.Metadata.TryGetValue("outputFile", out var outputFile) && !string.IsNullOrWhiteSpace(outputFile))
        {
            artifacts = artifacts.Append(new HarnessArtifact
            {
                Name = Path.GetFileName(outputFile),
                Path = outputFile,
                Type = DetermineArtifactType(outputFile)
            }).ToList();
        }

        return artifacts;
    }
}
