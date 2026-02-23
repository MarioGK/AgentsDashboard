using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record CreateAlertRuleRequest(
    string Name,
    AlertRuleType RuleType,
    int Threshold,
    int WindowMinutes,
    string? WebhookUrl = null,
    bool Enabled = true,
    int CooldownMinutes = 15);
