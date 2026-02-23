

namespace AgentsDashboard.Contracts.Features.Repositories.Models.Api;












public sealed record CreateWebhookRequest(string RepositoryId, string TaskId, string EventFilter, string Secret);
