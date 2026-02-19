using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record UpsertAutomationDefinitionRequest(
    string RepositoryId,
    string TaskId,
    string Name,
    string CronExpression,
    string TriggerKind,
    string ReplayPolicy,
    bool Enabled);
