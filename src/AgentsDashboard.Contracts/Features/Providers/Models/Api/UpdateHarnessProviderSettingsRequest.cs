using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record UpdateHarnessProviderSettingsRequest(
    string Model,
    double Temperature,
    int MaxTokens,
    Dictionary<string, string>? AdditionalSettings = null);
