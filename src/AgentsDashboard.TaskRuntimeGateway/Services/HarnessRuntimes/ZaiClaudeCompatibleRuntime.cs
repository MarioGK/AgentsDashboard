using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.TaskRuntimeGateway.Services;

namespace AgentsDashboard.TaskRuntimeGateway.Services.HarnessRuntimes;

public sealed class ZaiClaudeCompatibleRuntime(
    SecretRedactor secretRedactor,
    ILogger<ZaiClaudeCompatibleRuntime> logger) : ClaudeStreamRuntime(secretRedactor, logger)
{
    private const string EnforcedModel = "glm-5";
    private const string ZaiAnthropicBaseUrl = "https://api.z.ai/api/anthropic";

    public override string Name => "zai-claude-compatible-stream-json";

    protected override string ProviderName => "zai";

    protected override bool SupportsHarness(string harness)
    {
        return string.Equals(harness, "zai", StringComparison.OrdinalIgnoreCase);
    }

    protected override void ApplyEnvironment(
        Dictionary<string, string> environment,
        HarnessRunRequest request,
        RuntimeExecutionMode mode)
    {
        base.ApplyEnvironment(environment, request, mode);

        SetEnvironmentValue(environment, "ANTHROPIC_BASE_URL", ZaiAnthropicBaseUrl);
        SetEnvironmentValue(environment, "HARNESS_MODEL", EnforcedModel);
        SetEnvironmentValue(environment, "ZAI_MODEL", EnforcedModel);
        SetEnvironmentValue(environment, "CLAUDE_MODEL", EnforcedModel);
        SetEnvironmentValue(environment, "ANTHROPIC_MODEL", EnforcedModel);
        SetEnvironmentValue(environment, "ZAI_MODEL_POLICY", "strict");

        var key = GetEnvironmentValue(environment, "Z_AI_API_KEY")
            ?? GetEnvironmentValue(environment, "ANTHROPIC_AUTH_TOKEN")
            ?? GetEnvironmentValue(environment, "ANTHROPIC_API_KEY");

        if (!string.IsNullOrWhiteSpace(key))
        {
            SetEnvironmentValue(environment, "Z_AI_API_KEY", key);
            SetEnvironmentValue(environment, "ANTHROPIC_AUTH_TOKEN", key);
            SetEnvironmentValue(environment, "ANTHROPIC_API_KEY", key);
        }
    }

    protected override string ResolveModel(IReadOnlyDictionary<string, string> environment)
    {
        return EnforcedModel;
    }

    protected override void ApplyEnvelopePolicy(HarnessResultEnvelope envelope)
    {
        envelope.Metadata["provider"] = "zai";
        envelope.Metadata["modelPolicy"] = "strict-glm-5";
        envelope.Metadata["enforcedModel"] = EnforcedModel;
        envelope.Metadata["anthropicBaseUrl"] = ZaiAnthropicBaseUrl;

        if (!envelope.Metadata.TryGetValue("model", out var model) || string.IsNullOrWhiteSpace(model))
        {
            envelope.Metadata["model"] = EnforcedModel;
            return;
        }

        if (!model.Contains(EnforcedModel, StringComparison.OrdinalIgnoreCase))
        {
            envelope.Status = "failed";
            envelope.Summary = "Z.ai runtime rejected non-glm-5 model";
            envelope.Error = $"Model '{model}' violates strict glm-5 policy.";
            envelope.Metadata["modelPolicyViolation"] = model;
        }
    }
}
