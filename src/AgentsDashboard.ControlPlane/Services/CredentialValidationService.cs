using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AgentsDashboard.ControlPlane.Services;

public sealed class CredentialValidationService(IHttpClientFactory httpClientFactory, ILogger<CredentialValidationService> logger)
{
    public async Task<(bool Success, string Message)> ValidateAsync(string provider, string secretValue, CancellationToken ct)
    {
        return provider.ToLowerInvariant() switch
        {
            "github" => await ValidateGitHubAsync(secretValue, ct),
            "codex" => await ValidateOpenAiAsync(secretValue, ct),
            "opencode" => await ValidateOpenAiAsync(secretValue, ct),
            "llmtornado" => await ValidateLlmTornadoAsync(secretValue, ct),
            _ => (false, $"Unknown provider: {provider}")
        };
    }

    private async Task<(bool Success, string Message)> ValidateGitHubAsync(string token, CancellationToken ct)
    {
        try
        {
            using var client = httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("AgentsDashboard/1.0");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            var response = await client.GetAsync("https://api.github.com/user", ct);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
                var login = json.TryGetProperty("login", out var l) ? l.GetString() : "unknown";
                return (true, $"Authenticated as {login}");
            }

            return (false, $"GitHub API returned {(int)response.StatusCode}: {response.ReasonPhrase}");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "GitHub credential validation failed");
            return (false, $"Connection failed: {ex.Message}");
        }
    }

    private async Task<(bool Success, string Message)> ValidateOpenAiAsync(string apiKey, CancellationToken ct)
    {
        try
        {
            using var client = httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            var response = await client.GetAsync("https://api.openai.com/v1/models", ct);
            if (response.IsSuccessStatusCode || (int)response.StatusCode == 429)
                return (true, "OpenAI API key is valid");

            if ((int)response.StatusCode == 401)
                return (false, "Invalid OpenAI API key");

            return (false, $"OpenAI API returned {(int)response.StatusCode}: {response.ReasonPhrase}");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OpenAI credential validation failed");
            return (false, $"Connection failed: {ex.Message}");
        }
    }

    private async Task<(bool Success, string Message)> ValidateLlmTornadoAsync(string apiKey, CancellationToken ct)
    {
        try
        {
            using var client = httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            var body = new
            {
                model = "glm-5",
                max_tokens = 1,
                messages = new[] { new { role = "user", content = "hi" } }
            };

            var response = await client.PostAsJsonAsync("https://api.z.ai/api/anthropic/v1/messages", body, ct);
            if (response.IsSuccessStatusCode || (int)response.StatusCode == 429)
                return (true, "LlmTornado/Z.ai key is valid");

            if ((int)response.StatusCode == 401)
                return (false, "Invalid LlmTornado/Z.ai API key");

            return (false, $"LlmTornado/Z.ai API returned {(int)response.StatusCode}: {response.ReasonPhrase}");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "LlmTornado credential validation failed");
            return (false, $"Connection failed: {ex.Message}");
        }
    }
}
