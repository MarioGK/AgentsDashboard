namespace AgentsDashboard.ControlPlane.Infrastructure.Data;

public interface ISystemStore
{
    Task InitializeAsync(CancellationToken cancellationToken);

    Task<SystemSettingsDocument> GetSettingsAsync(CancellationToken cancellationToken);
    Task<SystemSettingsDocument> UpdateSettingsAsync(SystemSettingsDocument settings, CancellationToken cancellationToken);

    Task<List<McpRegistryServerDocument>> ListMcpRegistryServersAsync(CancellationToken cancellationToken);
    Task ReplaceMcpRegistryServersAsync(List<McpRegistryServerDocument> servers, CancellationToken cancellationToken);
    Task<McpRegistryStateDocument> GetMcpRegistryStateAsync(CancellationToken cancellationToken);
    Task<McpRegistryStateDocument> UpsertMcpRegistryStateAsync(McpRegistryStateDocument state, CancellationToken cancellationToken);

    Task<AlertRuleDocument> CreateAlertRuleAsync(AlertRuleDocument rule, CancellationToken cancellationToken);
    Task<List<AlertRuleDocument>> ListAlertRulesAsync(CancellationToken cancellationToken);
    Task<List<AlertRuleDocument>> ListEnabledAlertRulesAsync(CancellationToken cancellationToken);
    Task<AlertRuleDocument?> GetAlertRuleAsync(string ruleId, CancellationToken cancellationToken);
    Task<AlertRuleDocument?> UpdateAlertRuleAsync(string ruleId, AlertRuleDocument rule, CancellationToken cancellationToken);
    Task<bool> DeleteAlertRuleAsync(string ruleId, CancellationToken cancellationToken);

    Task<AlertEventDocument> RecordAlertEventAsync(AlertEventDocument alertEvent, CancellationToken cancellationToken);
    Task<AlertEventDocument?> GetAlertEventAsync(string eventId, CancellationToken cancellationToken);
    Task<List<AlertEventDocument>> ListRecentAlertEventsAsync(int limit, CancellationToken cancellationToken);
    Task<List<AlertEventDocument>> ListAlertEventsByRuleAsync(string ruleId, CancellationToken cancellationToken);
    Task<AlertEventDocument?> ResolveAlertEventAsync(string eventId, CancellationToken cancellationToken);
    Task<int> ResolveAlertEventsAsync(List<string> eventIds, CancellationToken cancellationToken);
}
