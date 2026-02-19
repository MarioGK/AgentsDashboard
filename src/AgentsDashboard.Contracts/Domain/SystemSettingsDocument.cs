namespace AgentsDashboard.Contracts.Domain;

































































public sealed class SystemSettingsDocument
{
    public string Id { get; set; } = "singleton";
    public List<string> DockerAllowedImages { get; set; } = [];
    public int RetentionDaysLogs { get; set; } = 30;
    public int RetentionDaysRuns { get; set; } = 90;
    public string VictoriaMetricsEndpoint { get; set; } = "http://localhost:8428";
    public string VmUiEndpoint { get; set; } = "http://localhost:8081";
    public OrchestratorSettings Orchestrator { get; set; } = new();
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
