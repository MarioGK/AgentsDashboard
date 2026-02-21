namespace AgentsDashboard.Contracts.Domain;

































































public sealed class HarnessResultEnvelope
{
    public string RunId { get; set; } = string.Empty;
    public string TaskId { get; set; } = string.Empty;
    public string Status { get; set; } = "unknown";
    public string Summary { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public List<string> Artifacts { get; set; } = [];
    public List<HarnessAction> Actions { get; set; } = [];
    public Dictionary<string, double> Metrics { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = [];
    public string RawOutputRef { get; set; } = string.Empty;
}
