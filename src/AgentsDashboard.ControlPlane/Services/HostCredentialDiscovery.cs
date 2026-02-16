using System.Text.Json;

namespace AgentsDashboard.ControlPlane.Services;

public static class HostCredentialDiscovery
{
    public static string? TryGetCodexApiKey()
    {
        var openAiApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(openAiApiKey))
            return openAiApiKey.Trim();

        var codexApiKey = Environment.GetEnvironmentVariable("CODEX_API_KEY");
        if (!string.IsNullOrWhiteSpace(codexApiKey))
            return codexApiKey.Trim();

        var authPath = GetCodexAuthPath();
        if (string.IsNullOrWhiteSpace(authPath) || !File.Exists(authPath))
            return null;

        try
        {
            using var stream = File.OpenRead(authPath);
            using var document = JsonDocument.Parse(stream);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            if (!document.RootElement.TryGetProperty("OPENAI_API_KEY", out var apiKeyElement))
                return null;

            var apiKey = apiKeyElement.GetString();
            return string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        }
        catch
        {
            return null;
        }
    }

    private static string? GetCodexAuthPath()
    {
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(codexHome))
            return Path.Combine(codexHome, "auth.json");

        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userHome))
            return null;

        return Path.Combine(userHome, ".codex", "auth.json");
    }
}
