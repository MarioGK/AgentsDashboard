namespace AgentsDashboard.Contracts.Features.Workspace.Models.Domain;

public sealed class RunQuestionAnswerDocument
{
    public string QuestionId { get; set; } = string.Empty;
    public string SelectedOptionValue { get; set; } = string.Empty;
    public string SelectedOptionLabel { get; set; } = string.Empty;
    public string SelectedOptionDescription { get; set; } = string.Empty;
    public string AdditionalContext { get; set; } = string.Empty;
}
