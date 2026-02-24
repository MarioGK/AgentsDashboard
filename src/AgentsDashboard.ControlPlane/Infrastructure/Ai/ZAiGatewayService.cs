using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AgentsDashboard.ControlPlane.Infrastructure.Ai.Models;

namespace AgentsDashboard.ControlPlane.Infrastructure.Ai;

public sealed class ZAiGatewayService(
    ILogger<ZAiGatewayService> logger,
    ISystemStore systemStore,
    IHttpClientFactory httpClientFactory)
{
    private const string RequiredModel = "glm-5";
    private const int MaxSummaryInputCharacters = 12000;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(3);

    public async Task<AiDockerfileResult> GenerateDockerfileAsync(
        string description,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new AiDockerfileResult(false, string.Empty, "Z.ai API key not configured.");
        }

        var result = await GenerateTextAsync(
            """
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
10. Create credential mount targets under /home/agent: .ssh, .gitconfig, .git-credentials, .netrc, .config/gh, .config/git, .config/opencode, .codex.

Output only Dockerfile content.
""",
            $"Create a Dockerfile for:\n{description}",
            apiKey,
            temperature: 0.2,
            maxTokens: 4096,
            cancellationToken,
            operationName: "dockerfile generation");

        if (!result.Success)
        {
            return new AiDockerfileResult(false, string.Empty, result.Error);
        }

        var dockerfile = result.Text;
        if (!dockerfile.Contains("FROM", StringComparison.OrdinalIgnoreCase))
        {
            return new AiDockerfileResult(false, string.Empty, "Generated content does not look like a Dockerfile.");
        }

        return new AiDockerfileResult(true, dockerfile, null);
    }

    public async Task<ZAiTextResult> SuggestPromptContinuationAsync(
        string prompt,
        string? context,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return new ZAiTextResult(false, string.Empty, "Prompt is required.");
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

        return new ZAiTextResult(true, continuation, null);
    }

    public Task<ZAiTextResult> ImprovePromptAsync(
        string prompt,
        string? context,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return Task.FromResult(new ZAiTextResult(false, string.Empty, "Prompt is required."));
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

    public Task<ZAiTextResult> GeneratePromptFromContextAsync(
        string context,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context))
        {
            return Task.FromResult(new ZAiTextResult(false, string.Empty, "Context is required."));
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

    public Task<ZAiTextResult> GenerateTaskTitleAsync(
        string prompt,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return Task.FromResult(new ZAiTextResult(false, string.Empty, "Prompt is required."));
        }

        return GenerateTextAsync(
            """
You create concise task titles for coding work items.
Rules:
- Output one single line.
- Maximum 80 characters.
- No markdown, quotes, bullets, or trailing punctuation.
- Keep wording specific and action-oriented.
""",
            $"Prompt:\n{prompt}",
            apiKey,
            temperature: 0.2,
            maxTokens: 120,
            cancellationToken,
            operationName: "task title generation");
    }

    public Task<ZAiTextResult> SummarizeRunOutputAsync(
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

        var result = await GenerateTextAsync(
            """
You create high-signal agent task prompts for coding workflows.
Return concise markdown with:
- Goal
- Required steps
- Validation checks
- Expected output format

Do not include code fences.
""",
            $"""
Repository: {request.RepositoryName}
Task Name: {request.TaskName}
Harness: {request.Harness}
Command: {request.Command}

Write the final task prompt.
""",
            apiKey,
            temperature: 0.3,
            maxTokens: 2048,
            cancellationToken,
            operationName: "task prompt generation");

        if (!result.Success)
        {
            return new TaskPromptGenerationResult(false, string.Empty, result.Error);
        }

        return new TaskPromptGenerationResult(true, result.Text, null);
    }

    private async Task<ZAiTextResult> GenerateTextAsync(
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
            return new ZAiTextResult(false, string.Empty, "Z.ai API key not configured.");
        }

        try
        {
            var response = await RunInBackgroundAsync(
                async token => await RequestTextAsync(
                    systemPrompt,
                    userInput,
                    apiKey,
                    temperature,
                    maxTokens,
                    token),
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(response))
            {
                return new ZAiTextResult(false, string.Empty, "LLM returned an empty response.");
            }

            return new ZAiTextResult(true, NormalizeTextResponse(response), null);
        }
        catch (Exception ex)
        {
            if (IsModelAccessError(ex))
            {
                return new ZAiTextResult(false, string.Empty, $"The current account/plan cannot use {RequiredModel}. Update your Z.ai plan/access and retry.");
            }

            logger.LogWarning(ex, "Z.ai {OperationName} failed", operationName);
            return new ZAiTextResult(false, string.Empty, $"AI generation failed: {ex.Message}");
        }
    }

    private async Task<string> RequestTextAsync(
        string systemPrompt,
        string userInput,
        string apiKey,
        double temperature,
        int maxTokens,
        CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient();
        client.Timeout = RequestTimeout;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var endpoint = await ResolveMessagesEndpointAsync(cancellationToken);
        var payload = new
        {
            model = RequiredModel,
            max_tokens = Math.Clamp(maxTokens, 1, 8192),
            temperature,
            system = systemPrompt,
            messages = new[] { new { role = "user", content = userInput } }
        };

        var response = await client.PostAsJsonAsync(endpoint, payload, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            return ExtractResponseText(content);
        }

        var error = ExtractErrorMessage(content);
        var message = string.IsNullOrWhiteSpace(error)
            ? $"Z.ai API returned {(int)response.StatusCode}: {response.ReasonPhrase}"
            : error;
        throw new InvalidOperationException(message);
    }

    private static Task<T> RunInBackgroundAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => operation(cancellationToken), cancellationToken);
    }

    private async Task<string> ResolveMessagesEndpointAsync(CancellationToken cancellationToken)
    {
        try
        {
            var settings = await systemStore.GetSettingsAsync(cancellationToken);
            return BuildMessagesEndpoint(settings.ZAi?.BaseUrl);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to load Z.ai settings; falling back to default base URL");
            return BuildMessagesEndpoint(ZAiSettings.DefaultBaseUrl);
        }
    }

    private static string BuildMessagesEndpoint(string? baseUrl)
    {
        var normalized = NormalizeBaseUrl(baseUrl);
        if (normalized.EndsWith("/v1/messages", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        if (normalized.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return $"{normalized}/messages";
        }

        return $"{normalized}/v1/messages";
    }

    private static string NormalizeBaseUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return ZAiSettings.DefaultBaseUrl;
        }

        return value.Trim().TrimEnd('/');
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

    private static string ExtractResponseText(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            if (!document.RootElement.TryGetProperty("content", out var contentArray) ||
                contentArray.ValueKind is not JsonValueKind.Array)
            {
                return string.Empty;
            }

            foreach (var item in contentArray.EnumerateArray())
            {
                if (!item.TryGetProperty("text", out var textNode))
                {
                    continue;
                }

                var text = textNode.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text.Trim();
                }
            }
        }
        catch
        {
        }

        return string.Empty;
    }

    private static string ExtractErrorMessage(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "Request failed.";
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            if (document.RootElement.TryGetProperty("error", out var errorElement))
            {
                if (errorElement.ValueKind == JsonValueKind.String)
                {
                    var direct = errorElement.GetString();
                    if (!string.IsNullOrWhiteSpace(direct))
                    {
                        return direct.Trim();
                    }
                }

                if (errorElement.ValueKind == JsonValueKind.Object &&
                    errorElement.TryGetProperty("message", out var messageElement))
                {
                    var message = messageElement.GetString();
                    if (!string.IsNullOrWhiteSpace(message))
                    {
                        return message.Trim();
                    }
                }
            }

            if (document.RootElement.TryGetProperty("message", out var rootMessage))
            {
                var message = rootMessage.GetString();
                if (!string.IsNullOrWhiteSpace(message))
                {
                    return message.Trim();
                }
            }
        }
        catch
        {
        }

        return "Request failed.";
    }
}
