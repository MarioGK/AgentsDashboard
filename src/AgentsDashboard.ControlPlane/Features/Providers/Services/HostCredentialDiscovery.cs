using System.Text.Json;

namespace AgentsDashboard.ControlPlane.Features.Providers.Services;

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

    public static string? TryGetHostSshDirectory(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            var configuredFullPath = Path.GetFullPath(configuredPath.Trim());
            return Directory.Exists(configuredFullPath) ? configuredFullPath : null;
        }

        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userHome))
        {
            return null;
        }

        var defaultSshDirectory = Path.Combine(userHome, ".ssh");
        return Directory.Exists(defaultSshDirectory) ? defaultSshDirectory : null;
    }

    public static string? TryGetHostSshAgentSocketPath(string? configuredPath)
    {
        var candidatePath = configuredPath;
        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            candidatePath = Environment.GetEnvironmentVariable("SSH_AUTH_SOCK");
        }

        if (string.IsNullOrWhiteSpace(candidatePath))
        {
            return null;
        }

        var normalizedPath = Path.GetFullPath(candidatePath.Trim());
        return Path.Exists(normalizedPath) ? normalizedPath : null;
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
