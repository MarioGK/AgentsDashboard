

namespace AgentsDashboard.Contracts.Features.Providers.Models.Api;












public sealed record UpdateHarnessProviderSettingsRequest(
    string Model,
    double Temperature,
    int MaxTokens,
    Dictionary<string, string>? AdditionalSettings = null);
