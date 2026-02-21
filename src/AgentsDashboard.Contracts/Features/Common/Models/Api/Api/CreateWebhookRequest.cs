using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record CreateWebhookRequest(string RepositoryId, string TaskId, string EventFilter, string Secret);
