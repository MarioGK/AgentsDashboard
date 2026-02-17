using System.Text;
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
public sealed record LlmTornadoTextResult(bool Success, string Text, string? Error);

public sealed class LlmTornadoGatewayService(ILogger<LlmTornadoGatewayService> logger)
{
    private const string RequiredModel = "glm-5";
    private const int MaxSummaryInputCharacters = 12000;

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
            if (IsModelAccessError(ex))
            {
                return new AiDockerfileResult(false, string.Empty, $"The current account/plan cannot use {RequiredModel}. Update your Z.ai plan/access and retry.");
            }

            logger.ZLogWarning(ex, "LlmTornado Dockerfile generation failed");
            return new AiDockerfileResult(false, string.Empty, $"AI generation failed: {ex.Message}");
        }
    }

    public async Task<LlmTornadoTextResult> SuggestPromptContinuationAsync(
        string prompt,
        string? context,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new LlmTornadoTextResult(false, string.Empty, "Prompt is required.");
        }

        var result = await GenerateTextAsync(
            """
You complete prompts for coding agents.
Continue the prompt in the same style and intent.
Return continuation text only.
Do not wrap output in markdown fences.
""",
            BuildPromptContinuationInput(prompt, context),
            apiKey,
            temperature: 0.25,
            maxTokens: 512,
            cancellationToken,
            operationName: "prompt continuation");

        if (!result.Success)
        {
            return result;
        }

        var continuation = result.Text;
        if (continuation.StartsWith(prompt, StringComparison.OrdinalIgnoreCase))
        {
            continuation = continuation[prompt.Length..].TrimStart();
        }

        return new LlmTornadoTextResult(true, continuation, null);
    }

    public Task<LlmTornadoTextResult> ImprovePromptAsync(
        string prompt,
        string? context,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return Task.FromResult(new LlmTornadoTextResult(false, string.Empty, "Prompt is required."));
        }

        return GenerateTextAsync(
            """
You improve coding workflow prompts.
Preserve user intent and constraints.
Improve clarity, structure, and testability.
Return only the improved prompt text.
Do not wrap output in markdown fences.
""",
            BuildPromptImprovementInput(prompt, context),
            apiKey,
            temperature: 0.3,
            maxTokens: 2048,
            cancellationToken,
            operationName: "prompt improvement");
    }

    public Task<LlmTornadoTextResult> GeneratePromptFromContextAsync(
        string context,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context))
        {
            return Task.FromResult(new LlmTornadoTextResult(false, string.Empty, "Context is required."));
        }

        return GenerateTextAsync(
            """
You write high-signal prompts for coding agents.
Output concise markdown with sections:
- Goal
- Constraints
- Required steps
- Validation checks
- Expected output format
Do not include code fences.
""",
            $"Context:\n{context}",
            apiKey,
            temperature: 0.35,
            maxTokens: 2048,
            cancellationToken,
            operationName: "context prompt generation");
    }

    public Task<LlmTornadoTextResult> SummarizeRunOutputAsync(
        string outputJson,
        string runLogs,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var normalizedOutput = TruncateForModelInput(outputJson, MaxSummaryInputCharacters);
        var normalizedLogs = TruncateForModelInput(runLogs, MaxSummaryInputCharacters);

        var userInput = new StringBuilder()
            .AppendLine("Output JSON:")
            .AppendLine(string.IsNullOrWhiteSpace(normalizedOutput) ? "(none)" : normalizedOutput)
            .AppendLine()
            .AppendLine("Run Logs:")
            .AppendLine(string.IsNullOrWhiteSpace(normalizedLogs) ? "(none)" : normalizedLogs)
            .ToString();

        return GenerateTextAsync(
            """
You summarize AI harness run output for an operations dashboard.
Focus on:
- Final status and result
- Key actions taken
- Errors or blockers
- Artifacts produced
- Next operator action, if any
Return concise markdown without code fences.
""",
            userInput,
            apiKey,
            temperature: 0.2,
            maxTokens: 1200,
            cancellationToken,
            operationName: "run output summary");
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
            if (IsModelAccessError(ex))
            {
                return new TaskPromptGenerationResult(false, string.Empty, $"The current account/plan cannot use {RequiredModel}. Update your Z.ai plan/access and retry.");
            }

            logger.ZLogWarning(ex, "LlmTornado task prompt generation failed");
            return new TaskPromptGenerationResult(false, string.Empty, $"AI generation failed: {ex.Message}");
        }
    }

    private async Task<LlmTornadoTextResult> GenerateTextAsync(
        string systemPrompt,
        string userInput,
        string apiKey,
        double temperature,
        int maxTokens,
        CancellationToken cancellationToken,
        string operationName)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new LlmTornadoTextResult(false, string.Empty, "Z.ai API key not configured.");
        }

        try
        {
            var api = CreateApi(apiKey);
            var response = await api.Chat
                .CreateConversation(ChatModel.Zai.Glm.Glm5, temperature: temperature, maxTokens: maxTokens)
                .AppendSystemMessage(systemPrompt)
                .AppendUserInput(userInput)
                .GetResponse();

            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(response))
            {
                return new LlmTornadoTextResult(false, string.Empty, "LLM returned an empty response.");
            }

            return new LlmTornadoTextResult(true, NormalizeTextResponse(response), null);
        }
        catch (Exception ex)
        {
            if (IsModelAccessError(ex))
            {
                return new LlmTornadoTextResult(false, string.Empty, $"The current account/plan cannot use {RequiredModel}. Update your Z.ai plan/access and retry.");
            }

            logger.ZLogWarning(ex, "LlmTornado {OperationName} failed", operationName);
            return new LlmTornadoTextResult(false, string.Empty, $"AI generation failed: {ex.Message}");
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

    private static bool IsModelAccessError(Exception ex)
    {
        var lower = ex.Message.ToLowerInvariant();
        return lower.Contains(RequiredModel) ||
               lower.Contains("model") ||
               lower.Contains("not found") ||
               lower.Contains("unsupported");
    }

    private static string BuildPromptContinuationInput(string prompt, string? context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Current prompt draft:");
        builder.AppendLine(prompt);

        if (!string.IsNullOrWhiteSpace(context))
        {
            builder.AppendLine();
            builder.AppendLine("Additional context:");
            builder.AppendLine(context);
        }

        return builder.ToString();
    }

    private static string BuildPromptImprovementInput(string prompt, string? context)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Prompt to improve:");
        builder.AppendLine(prompt);

        if (!string.IsNullOrWhiteSpace(context))
        {
            builder.AppendLine();
            builder.AppendLine("Context:");
            builder.AppendLine(context);
        }

        return builder.ToString();
    }

    private static string TruncateForModelInput(string value, int maxCharacters)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = NormalizeTextResponse(value);
        return normalized.Length <= maxCharacters
            ? normalized
            : normalized[..maxCharacters];
    }
}
