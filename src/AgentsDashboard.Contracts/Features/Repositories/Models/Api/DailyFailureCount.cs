using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record DailyFailureCount(DateTime Date, int Count);
