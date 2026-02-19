using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record UpdateSystemSettingsRequest(
    List<string>? DockerAllowedImages = null,
    int? RetentionDaysLogs = null,
    int? RetentionDaysRuns = null,
    string? VictoriaMetricsEndpoint = null,
    string? VmUiEndpoint = null,
    OrchestratorSettings? Orchestrator = null);
