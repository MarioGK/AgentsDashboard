using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.WorkerGateway.Configuration;
using AgentsDashboard.WorkerGateway.Services;
using Microsoft.Extensions.Options;

namespace AgentsDashboard.WorkerGateway.Adapters;

public sealed class OpenCodeAdapter(
    IOptions<WorkerOptions> options,
    SecretRedactor secretRedactor,
    ILogger<OpenCodeAdapter> logger) : HarnessAdapterBase(options, secretRedactor, logger)
{
    public override string HarnessName => "opencode";

    protected override void AddHarnessSpecificArguments(HarnessExecutionContext context, List<string> args)
    {
        args.AddRange(new[]
        {
            "-e", "OPENCODE_FORMAT=json",
            "-e", "OPENCODE_OUTPUT_ENVELOPE=true",
        });

        var model = GetEnvValue(context, "OPENCODE_MODEL", "HARNESS_MODEL");
        if (!string.IsNullOrWhiteSpace(model))
        {
            args.Add("-e");
            args.Add($"OPENCODE_MODEL={model}");
        }

        if (context.Env.TryGetValue("OPENCODE_PROVIDER", out var provider) && !string.IsNullOrWhiteSpace(provider))
        {
            args.Add("-e");
            args.Add($"OPENCODE_PROVIDER={provider}");
        }

        var temperature = GetEnvValue(context, "OPENCODE_TEMPERATURE", "HARNESS_TEMPERATURE");
        if (!string.IsNullOrWhiteSpace(temperature))
        {
            args.Add("-e");
            args.Add($"OPENCODE_TEMPERATURE={temperature}");
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

        var error = envelope.Error ?? envelope.Summary ?? string.Empty;
        var lowerError = error.ToLowerInvariant();

        if (ContainsAny(lowerError, "opencode", "context window", "token limit"))
        {
            return FailureClassification.FromClass(
                FailureClass.ResourceExhausted,
                "Context limit exceeded",
                true,
                30,
                "Reduce context size",
                "Split into smaller tasks",
                "Use context compression");
        }

        if (ContainsAny(lowerError, "editor", "file edit", "write"))
        {
            return FailureClassification.FromClass(
                FailureClass.PermissionDenied,
                "File edit failed",
                false,
                0,
                "Check file permissions",
                "Verify workspace is writable");
        }

        if (ContainsAny(lowerError, "terminal", "shell", "command execution"))
        {
            return FailureClassification.FromClass(
                FailureClass.InternalError,
                "Terminal command failed",
                true,
                10,
                "Check command syntax",
                "Verify command availability");
        }

        if (ContainsAny(lowerError, "provider", "model", "not available", "unsupported"))
        {
            return FailureClassification.FromClass(
                FailureClass.ConfigurationError,
                "Provider/model configuration error",
                false,
                0,
                "Check provider configuration",
                "Verify model availability");
        }

        return base.ClassifyByErrorPatterns(lowerError);
    }

    public override IReadOnlyList<HarnessArtifact> MapArtifacts(HarnessResultEnvelope envelope)
    {
        var artifacts = base.MapArtifacts(envelope);

        if (envelope.Metadata.TryGetValue("changedFiles", out var changedFilesStr) &&
            !string.IsNullOrWhiteSpace(changedFilesStr))
        {
            var changedFiles = changedFilesStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var file in changedFiles)
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

        if (envelope.Metadata.TryGetValue("logFile", out var logFile) && !string.IsNullOrWhiteSpace(logFile))
        {
            artifacts = artifacts.Append(new HarnessArtifact
            {
                Name = Path.GetFileName(logFile),
                Path = logFile,
                Type = "log"
            }).ToList();
        }

        return artifacts;
    }
}
