using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record UpdateWebhookRequest(string TaskId, string EventFilter, string Secret, bool Enabled);
