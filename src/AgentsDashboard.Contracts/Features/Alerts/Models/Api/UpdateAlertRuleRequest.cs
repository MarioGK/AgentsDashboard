

namespace AgentsDashboard.Contracts.Features.Alerts.Models.Api;












public sealed record UpdateAlertRuleRequest(
    string Name,
    AlertRuleType RuleType,
    int Threshold,
    int WindowMinutes,
    string? WebhookUrl = null,
    bool Enabled = true,
    int CooldownMinutes = 15);
