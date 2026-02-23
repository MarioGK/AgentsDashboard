using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using AgentsDashboard.ControlPlane.Features.Settings.Models;

namespace AgentsDashboard.ControlPlane.Features.Settings.Services;

public sealed class ZAiValidationService(IHttpClientFactory httpClientFactory)
{
    private const string Model = "glm-5";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(2);

    public Task<ZAiValidationResult> TestConnectivityAsync(
        string apiKey,
        string baseUrl,
        CancellationToken cancellationToken)
    {
        return ExecuteChatValidationAsync(
            testName: "Connectivity",
            apiKey,
            baseUrl,
            prompt: "ping",
            maxTokens: 1,
            cancellationToken);
    }

    public Task<ZAiValidationResult> TestChatAsync(
        string apiKey,
        string baseUrl,
        string prompt,
        CancellationToken cancellationToken)
    {
        var normalizedPrompt = string.IsNullOrWhiteSpace(prompt)
            ? "Respond with: ready"
            : prompt.Trim();

        return ExecuteChatValidationAsync(
            testName: "Chat",
            apiKey,
            baseUrl,
            normalizedPrompt,
            maxTokens: 256,
            cancellationToken);
    }

    private async Task<ZAiValidationResult> ExecuteChatValidationAsync(
        string testName,
        string apiKey,
        string baseUrl,
        string prompt,
        int maxTokens,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new ZAiValidationResult(
                Success: false,
                TestName: testName,
                Message: "API key is required.",
                StatusCode: 0,
                DurationMs: 0,
                ResponsePreview: string.Empty);
        }

        var endpoint = BuildEndpoint(baseUrl);
        using var client = httpClientFactory.CreateClient();
        client.Timeout = RequestTimeout;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

        var payload = new
        {
            model = Model,
            max_tokens = Math.Clamp(maxTokens, 1, 4096),
            messages = new[] { new { role = "user", content = prompt } }
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await RunInBackgroundAsync(
                async token =>
                {
                    using var httpResponse = await client.PostAsJsonAsync(endpoint, payload, token);
                    var body = await httpResponse.Content.ReadAsStringAsync(token);
                    return (
                        IsSuccess: httpResponse.IsSuccessStatusCode,
                        StatusCode: (int)httpResponse.StatusCode,
                        ReasonPhrase: httpResponse.ReasonPhrase,
                        Content: body);
                },
                cancellationToken);
            stopwatch.Stop();

            if (result.IsSuccess)
            {
                var text = ExtractResponseText(result.Content);
                return new ZAiValidationResult(
                    Success: true,
                    TestName: testName,
                    Message: "Validation succeeded.",
                    StatusCode: result.StatusCode,
                    DurationMs: stopwatch.ElapsedMilliseconds,
                    ResponsePreview: Truncate(text, 300));
            }

            var failurePreview = ExtractErrorMessage(result.Content);
            return new ZAiValidationResult(
                Success: false,
                TestName: testName,
                Message: failurePreview,
                StatusCode: result.StatusCode,
                DurationMs: stopwatch.ElapsedMilliseconds,
                ResponsePreview: Truncate(result.Content, 300));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ZAiValidationResult(
                Success: false,
                TestName: testName,
                Message: $"Connection failed: {ex.Message}",
                StatusCode: 0,
                DurationMs: stopwatch.ElapsedMilliseconds,
                ResponsePreview: string.Empty);
        }
    }

    private static Task<T> RunInBackgroundAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        return Task.Run(() => operation(cancellationToken), cancellationToken);
    }

    private static string BuildEndpoint(string baseUrl)
    {
        var normalized = string.IsNullOrWhiteSpace(baseUrl)
            ? ZAiSettings.DefaultBaseUrl
            : baseUrl.Trim().TrimEnd('/');

        return $"{normalized}/v1/messages";
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
                if (item.TryGetProperty("text", out var textNode))
                {
                    var text = textNode.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text.Trim();
                    }
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
            return "Validation failed.";
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

        return "Validation failed.";
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength
            ? value
            : value[..maxLength];
    }
}
