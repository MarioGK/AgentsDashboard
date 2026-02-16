using System.Text.RegularExpressions;
using LlmTornado;
using LlmTornado.Chat.Models;
using LlmTornado.Code;

namespace AgentsDashboard.ControlPlane.Services;

public sealed record TaskPromptGenerationRequest(
    string RepositoryName,
    string TaskName,
    string Harness,
    string Kind,
    string Command,
    string? CronExpression);

public sealed record TaskPromptGenerationResult(bool Success, string Prompt, string? Error);

public sealed class LlmTornadoGatewayService(ILogger<LlmTornadoGatewayService> logger)
{
    private const string RequiredModel = "glm-5";

    public async Task<AiDockerfileResult> GenerateDockerfileAsync(
        string description,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AiDockerfileResult(false, string.Empty, "Z.ai API key not configured.");
        }

        try
        {
            var api = CreateApi(apiKey);
            var response = await api.Chat
                .CreateConversation(ChatModel.Zai.Glm.Glm5, temperature: 0.2, maxTokens: 4096)
                .AppendSystemMessage("""
You are an expert DevOps engineer specializing in Docker image design.
Generate a production-ready Dockerfile from user requirements.

Rules:
1. Use Docker best practices and keep layers minimal.
2. Always create a non-root user named agent with UID 1000.
3. Create /workspace and /artifacts owned by agent.
4. Set HOME=/home/agent and DEBIAN_FRONTEND=noninteractive where applicable.
5. Clean package manager caches.
6. Avoid embedding secrets.
7. Use pinned base image tags, never latest.
8. WORKDIR must be /workspace.
9. Default CMD should be an interactive shell.

Output only Dockerfile content.
""")
                .AppendUserInput($"Create a Dockerfile for:\n{description}")
                .GetResponse();

            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(response))
            {
                return new AiDockerfileResult(false, string.Empty, "LLM returned an empty response.");
            }

            var dockerfile = NormalizeTextResponse(response);
            if (!dockerfile.Contains("FROM", StringComparison.OrdinalIgnoreCase))
            {
                return new AiDockerfileResult(false, string.Empty, "Generated content does not look like a Dockerfile.");
            }

            return new AiDockerfileResult(true, dockerfile, null);
        }
        catch (Exception ex)
        {
            var lower = ex.Message.ToLowerInvariant();
            if (lower.Contains(RequiredModel) || lower.Contains("model") || lower.Contains("not found") || lower.Contains("unsupported"))
            {
                return new AiDockerfileResult(false, string.Empty, $"The current account/plan cannot use {RequiredModel}. Update your Z.ai plan/access and retry.");
            }

            logger.LogWarning(ex, "LlmTornado Dockerfile generation failed");
            return new AiDockerfileResult(false, string.Empty, $"AI generation failed: {ex.Message}");
        }
    }

    public async Task<TaskPromptGenerationResult> GenerateTaskPromptAsync(
        TaskPromptGenerationRequest request,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new TaskPromptGenerationResult(false, string.Empty, "Z.ai API key not configured.");
        }

        try
        {
            var api = CreateApi(apiKey);
            var response = await api.Chat
                .CreateConversation(ChatModel.Zai.Glm.Glm5, temperature: 0.3, maxTokens: 2048)
                .AppendSystemMessage("""
You create high-signal agent task prompts for coding workflows.
Return concise markdown with:
- Goal
- Required steps
- Validation checks
- Expected output format

Do not include code fences.
""")
                .AppendUserInput($"""
Repository: {request.RepositoryName}
Task Name: {request.TaskName}
Harness: {request.Harness}
Kind: {request.Kind}
Command: {request.Command}
Cron: {request.CronExpression}

Write the final task prompt.
""")
                .GetResponse();

            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(response))
            {
                return new TaskPromptGenerationResult(false, string.Empty, "LLM returned an empty prompt.");
            }

            return new TaskPromptGenerationResult(true, NormalizeTextResponse(response), null);
        }
        catch (Exception ex)
        {
            var lower = ex.Message.ToLowerInvariant();
            if (lower.Contains(RequiredModel) || lower.Contains("model") || lower.Contains("not found") || lower.Contains("unsupported"))
            {
                return new TaskPromptGenerationResult(false, string.Empty, $"The current account/plan cannot use {RequiredModel}. Update your Z.ai plan/access and retry.");
            }

            logger.LogWarning(ex, "LlmTornado task prompt generation failed");
            return new TaskPromptGenerationResult(false, string.Empty, $"AI generation failed: {ex.Message}");
        }
    }

    private static TornadoApi CreateApi(string apiKey)
    {
        return new TornadoApi(LLmProviders.Zai, apiKey);
    }

    private static string NormalizeTextResponse(string content)
    {
        var trimmed = content.Trim();
        trimmed = Regex.Replace(trimmed, @"^```(?:dockerfile|md|markdown)?\s*", string.Empty, RegexOptions.IgnoreCase);
        trimmed = Regex.Replace(trimmed, @"\s*```$", string.Empty, RegexOptions.IgnoreCase);
        return trimmed.Trim();
    }
}
