using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record ValidateProviderRequest(string Provider, string SecretValue);
