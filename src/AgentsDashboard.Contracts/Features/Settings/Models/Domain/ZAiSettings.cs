namespace AgentsDashboard.Contracts.Features.Settings.Models.Domain;

public sealed class ZAiSettings
{
    public const string DefaultBaseUrl = "https://api.z.ai/api/anthropic";

    public string BaseUrl { get; set; } = DefaultBaseUrl;
}
