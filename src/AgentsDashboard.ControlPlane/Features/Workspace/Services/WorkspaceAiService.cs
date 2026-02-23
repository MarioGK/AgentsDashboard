using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AgentsDashboard.ControlPlane.Features.Workspace.Services;

public interface IWorkspaceAiService
{
    Task<WorkspaceAiTextResult> SuggestPromptContinuationAsync(
        string repositoryId,
        string prompt,
        string? context,
        CancellationToken cancellationToken);

    Task<WorkspaceAiTextResult> ImprovePromptAsync(
        string repositoryId,
        string prompt,
        string? context,
        CancellationToken cancellationToken);

    Task<WorkspaceAiTextResult> GeneratePromptFromContextAsync(
        string repositoryId,
        string context,
        CancellationToken cancellationToken);

    Task<WorkspaceAiTextResult> GenerateTaskTitleAsync(
        string repositoryId,
        string prompt,
        CancellationToken cancellationToken);

    Task<WorkspaceAiTextResult> SummarizeRunOutputAsync(
        string repositoryId,
        string outputJson,
        IReadOnlyList<RunLogEvent> runLogs,
        CancellationToken cancellationToken);

    Task<WorkspaceEmbeddingResult> CreateEmbeddingAsync(
        string repositoryId,
        string text,
        CancellationToken cancellationToken);
}

public sealed record WorkspaceAiTextResult(
    bool Success,
    string Text,
    bool UsedFallback,
    bool KeyConfigured,
    string? Message);

public sealed record WorkspaceEmbeddingResult(
    bool Success,
    string Payload,
    int Dimensions,
    string Model,
    bool UsedFallback,
    bool KeyConfigured,
    string? Message);

public sealed class WorkspaceAiService(
    IRepositoryStore store,
    ISecretCryptoService secretCryptoService,
    ZAiGatewayService zAiGatewayService,
    IHarnessOutputParserService parserService,
    ILogger<WorkspaceAiService> logger) : IWorkspaceAiService
{
    private const int MaxEmbeddingInputCharacters = 6000;
    private const int FallbackEmbeddingDimensions = 128;
    private const int TaskTitleMaxRetries = 5;
    private const int TaskTitleMaxLength = 80;

    public async Task<WorkspaceAiTextResult> SuggestPromptContinuationAsync(
        string _,
        string prompt,
        string? context,
        CancellationToken cancellationToken)
    {
        var fallback = BuildFallbackPromptContinuation(prompt);
        var apiKey = await ResolveApiKeyAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new WorkspaceAiTextResult(
                true,
                fallback,
                UsedFallback: true,
                KeyConfigured: false,
                Message: "AI key not configured; returned heuristic continuation.");
        }

        var result = await zAiGatewayService.SuggestPromptContinuationAsync(
            prompt,
            context,
            apiKey,
            cancellationToken);

        if (result.Success && !string.IsNullOrWhiteSpace(result.Text))
        {
            return new WorkspaceAiTextResult(
                true,
                result.Text,
                UsedFallback: false,
                KeyConfigured: true,
                Message: null);
        }

        return new WorkspaceAiTextResult(
            true,
            fallback,
            UsedFallback: true,
            KeyConfigured: true,
            Message: result.Error ?? "AI continuation failed; returned heuristic continuation.");
    }

    public async Task<WorkspaceAiTextResult> ImprovePromptAsync(
        string _,
        string prompt,
        string? context,
        CancellationToken cancellationToken)
    {
        var fallback = BuildFallbackPromptImprovement(prompt);
        var apiKey = await ResolveApiKeyAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new WorkspaceAiTextResult(
                true,
                fallback,
                UsedFallback: true,
                KeyConfigured: false,
                Message: "AI key not configured; returned heuristic prompt improvement.");
        }

        var result = await zAiGatewayService.ImprovePromptAsync(
            prompt,
            context,
            apiKey,
            cancellationToken);

        if (result.Success && !string.IsNullOrWhiteSpace(result.Text))
        {
            return new WorkspaceAiTextResult(
                true,
                result.Text,
                UsedFallback: false,
                KeyConfigured: true,
                Message: null);
        }

        return new WorkspaceAiTextResult(
            true,
            fallback,
            UsedFallback: true,
            KeyConfigured: true,
            Message: result.Error ?? "AI improvement failed; returned heuristic prompt improvement.");
    }

    public async Task<WorkspaceAiTextResult> GeneratePromptFromContextAsync(
        string _,
        string context,
        CancellationToken cancellationToken)
    {
        var fallback = BuildFallbackGeneratedPrompt(context);
        var apiKey = await ResolveApiKeyAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new WorkspaceAiTextResult(
                true,
                fallback,
                UsedFallback: true,
                KeyConfigured: false,
                Message: "AI key not configured; returned template prompt.");
        }

        var result = await zAiGatewayService.GeneratePromptFromContextAsync(
            context,
            apiKey,
            cancellationToken);

        if (result.Success && !string.IsNullOrWhiteSpace(result.Text))
        {
            return new WorkspaceAiTextResult(
                true,
                result.Text,
                UsedFallback: false,
                KeyConfigured: true,
                Message: null);
        }

        return new WorkspaceAiTextResult(
            true,
            fallback,
            UsedFallback: true,
            KeyConfigured: true,
            Message: result.Error ?? "AI generation failed; returned template prompt.");
    }

    public async Task<WorkspaceAiTextResult> GenerateTaskTitleAsync(
        string _,
        string prompt,
        CancellationToken cancellationToken)
    {
        var normalizedPrompt = prompt?.Trim() ?? string.Empty;
        if (normalizedPrompt.Length == 0)
        {
            return new WorkspaceAiTextResult(
                false,
                string.Empty,
                UsedFallback: true,
                KeyConfigured: false,
                Message: "Prompt is required.");
        }

        var fallback = BuildFallbackTaskTitle(normalizedPrompt);
        var apiKey = await ResolveApiKeyAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new WorkspaceAiTextResult(
                true,
                fallback,
                UsedFallback: true,
                KeyConfigured: false,
                Message: "AI key not configured; using fallback task title.");
        }

        string? lastError = null;

        for (var attempt = 0; attempt < TaskTitleMaxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await zAiGatewayService.GenerateTaskTitleAsync(
                normalizedPrompt,
                apiKey,
                cancellationToken);

            if (result.Success)
            {
                var title = NormalizeTaskTitle(result.Text);
                if (!string.IsNullOrWhiteSpace(title))
                {
                    return new WorkspaceAiTextResult(
                        true,
                        title,
                        UsedFallback: false,
                        KeyConfigured: true,
                        Message: null);
                }
            }

            lastError = result.Error;
        }

        return new WorkspaceAiTextResult(
            true,
            fallback,
            UsedFallback: true,
            KeyConfigured: true,
            Message: lastError ?? "AI title generation failed; using fallback task title.");
    }

    public async Task<WorkspaceAiTextResult> SummarizeRunOutputAsync(
        string _,
        string outputJson,
        IReadOnlyList<RunLogEvent> runLogs,
        CancellationToken cancellationToken)
    {
        var fallback = BuildFallbackRunSummary(outputJson, runLogs);
        var apiKey = await ResolveApiKeyAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new WorkspaceAiTextResult(
                true,
                fallback,
                UsedFallback: true,
                KeyConfigured: false,
                Message: "AI key not configured; returned parser-based summary.");
        }

        var logText = string.Join(
            Environment.NewLine,
            runLogs
                .OrderBy(x => x.TimestampUtc)
                .TakeLast(250)
                .Select(x => $"[{x.TimestampUtc:O}] {x.Level}: {x.Message}"));

        var result = await zAiGatewayService.SummarizeRunOutputAsync(
            outputJson,
            logText,
            apiKey,
            cancellationToken);

        if (result.Success && !string.IsNullOrWhiteSpace(result.Text))
        {
            return new WorkspaceAiTextResult(
                true,
                result.Text,
                UsedFallback: false,
                KeyConfigured: true,
                Message: null);
        }

        return new WorkspaceAiTextResult(
            true,
            fallback,
            UsedFallback: true,
            KeyConfigured: true,
            Message: result.Error ?? "AI summarization failed; returned parser-based summary.");
    }

    public async Task<WorkspaceEmbeddingResult> CreateEmbeddingAsync(
        string _,
        string text,
        CancellationToken cancellationToken)
    {
        var normalizedText = NormalizeForEmbedding(text);
        if (normalizedText.Length == 0)
        {
            return new WorkspaceEmbeddingResult(
                Success: false,
                Payload: string.Empty,
                Dimensions: 0,
                Model: string.Empty,
                UsedFallback: true,
                KeyConfigured: false,
                Message: "Embedding input is empty.");
        }

        var apiKey = await ResolveApiKeyAsync(cancellationToken);
        var keyConfigured = !string.IsNullOrWhiteSpace(apiKey);
        var fallback = BuildDeterministicEmbeddingPayload(normalizedText, FallbackEmbeddingDimensions);
        return new WorkspaceEmbeddingResult(
            Success: true,
            Payload: fallback,
            Dimensions: FallbackEmbeddingDimensions,
            Model: "deterministic-fallback",
            UsedFallback: true,
            KeyConfigured: keyConfigured,
            Message: "Using deterministic fallback embedding.");
    }

    private async Task<string?> ResolveApiKeyAsync(CancellationToken cancellationToken)
    {
        var candidates = new[]
        {
            (RepositoryId: "global", Provider: "zai"),
        };

        foreach (var candidate in candidates)
        {
            var secret = await store.GetProviderSecretAsync(candidate.RepositoryId, candidate.Provider, cancellationToken);
            if (secret is null || string.IsNullOrWhiteSpace(secret.EncryptedValue))
            {
                continue;
            }

            try
            {
                var decrypted = secretCryptoService.Decrypt(secret.EncryptedValue);
                if (!string.IsNullOrWhiteSpace(decrypted))
                {
                    return decrypted;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Failed to decrypt provider secret for repository {RepositoryId} and provider {Provider}",
                    candidate.RepositoryId,
                    candidate.Provider);
            }
        }

        return null;
    }

    private static string NormalizeForEmbedding(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var normalized = text.Trim();
        return normalized.Length <= MaxEmbeddingInputCharacters
            ? normalized
            : normalized[..MaxEmbeddingInputCharacters];
    }

    private static string BuildDeterministicEmbeddingPayload(string text, int dimensions)
    {
        if (dimensions <= 0)
        {
            dimensions = FallbackEmbeddingDimensions;
        }

        var vector = new double[dimensions];
        var tokens = text
            .Split([' ', '\n', '\r', '\t', ',', '.', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\''],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 1)
            .Take(400)
            .ToList();

        if (tokens.Count == 0)
        {
            return $"[{string.Join(",", Enumerable.Repeat("0", dimensions))}]";
        }

        foreach (var token in tokens)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token.ToLowerInvariant()));
            var bucket = ((hash[0] << 8) | hash[1]) % dimensions;
            var sign = (hash[2] & 1) == 0 ? 1d : -1d;
            var magnitude = (hash[3] / 255d) + 0.05d;
            vector[bucket] += sign * magnitude;
        }

        var norm = Math.Sqrt(vector.Sum(value => value * value));
        if (norm > 0d)
        {
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }
        }

        return $"[{string.Join(",", vector.Select(value => value.ToString("0.######", CultureInfo.InvariantCulture)))}]";
    }

    private static string BuildFallbackPromptContinuation(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return "Add goal, constraints, validation checks, and expected output format.";
        }

        var normalized = prompt.Trim();
        if (normalized.EndsWith(':'))
        {
            return "\n- Add concrete steps\n- Define validation checks\n- Define expected output";
        }

        return "\n\nValidation checks:\n- Build passes\n- Tests pass\n\nExpected output format:\n- Structured summary with actions and artifacts";
    }

    private static string BuildFallbackPromptImprovement(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return "Goal:\n- Describe the intended outcome\n\nRequired steps:\n- Implement changes\n- Validate behavior\n\nExpected output format:\n- Brief summary plus verification evidence";
        }

        var normalized = prompt.Trim();
        var builder = new StringBuilder(normalized);

        if (!normalized.Contains("Validation", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine();
            builder.AppendLine();
            builder.AppendLine("Validation checks:");
            builder.AppendLine("- Build succeeds");
            builder.AppendLine("- Relevant tests pass");
        }

        if (!normalized.Contains("Expected output", StringComparison.OrdinalIgnoreCase))
        {
            builder.AppendLine();
            builder.AppendLine("Expected output format:");
            builder.AppendLine("- Concise summary of changes");
            builder.AppendLine("- Verification results");
            builder.AppendLine("- Risks or follow-ups");
        }

        return builder.ToString().Trim();
    }

    private static string BuildFallbackGeneratedPrompt(string context)
    {
        var normalizedContext = string.IsNullOrWhiteSpace(context)
            ? "No context provided"
            : context.Trim();

        return $"""
Goal:
- {normalizedContext}

Constraints:
- Keep scope focused and deterministic.
- Preserve existing behavior unless explicitly requested.

Required steps:
- Inspect relevant files and dependencies.
- Implement minimal, correct changes.
- Validate with focused checks.

Validation checks:
- Build succeeds.
- Behavior matches requested goal.

Expected output format:
- Summary of changes
- Verification performed
- Remaining risks
""";
    }

    private static string BuildFallbackTaskTitle(string prompt)
    {
        var firstLine = prompt
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;

        var normalized = Regex.Replace(firstLine, @"\s+", " ").Trim();
        if (normalized.Length == 0)
        {
            return $"Task {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}";
        }

        return normalized.Length <= TaskTitleMaxLength
            ? normalized
            : normalized[..TaskTitleMaxLength].TrimEnd();
    }

    private static string NormalizeTaskTitle(string value)
    {
        var firstLine = value
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;

        var normalized = Regex.Replace(firstLine, @"\s+", " ").Trim();
        normalized = normalized.Trim('"', '\'', '`', '.', ':', ';', '-', '*');
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        return normalized.Length <= TaskTitleMaxLength
            ? normalized
            : normalized[..TaskTitleMaxLength].TrimEnd();
    }

    private string BuildFallbackRunSummary(string outputJson, IReadOnlyList<RunLogEvent> runLogs)
    {
        var parsed = parserService.Parse(outputJson, runLogs);

        var lines = new List<string>
        {
            $"Status: {parsed.Status}",
        };

        if (!string.IsNullOrWhiteSpace(parsed.Summary))
        {
            lines.Add($"Summary: {parsed.Summary}");
        }

        if (!string.IsNullOrWhiteSpace(parsed.Error))
        {
            lines.Add($"Error: {parsed.Error}");
        }

        var artifactSection = parsed.Sections.FirstOrDefault(x =>
            string.Equals(x.Key, "artifacts", StringComparison.OrdinalIgnoreCase));
        if (artifactSection is not null && artifactSection.Fields.Count > 0)
        {
            lines.Add($"Artifacts: {artifactSection.Fields.Count}");
        }

        if (parsed.ToolCallGroups.Count > 0)
        {
            lines.Add($"Tool calls: {parsed.ToolCallGroups.Count}");
        }

        var latestErrorLog = runLogs
            .OrderByDescending(x => x.TimestampUtc)
            .FirstOrDefault(x => x.Level.Contains("error", StringComparison.OrdinalIgnoreCase));
        if (latestErrorLog is not null)
        {
            lines.Add($"Latest error log: {latestErrorLog.Message}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
