namespace AgentsDashboard.Contracts.Domain;

public sealed class RunQuestionItemDocument
{
    public string Id { get; set; } = string.Empty;
    public string Header { get; set; } = string.Empty;
    public string Prompt { get; set; } = string.Empty;
    public int Order { get; set; }
    public List<RunQuestionOptionDocument> Options { get; set; } = [];
}
