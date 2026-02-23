

namespace AgentsDashboard.Contracts.Features.Repositories.Models.Api;












public sealed record UpdateWebhookRequest(string TaskId, string EventFilter, string Secret, bool Enabled);
