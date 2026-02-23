

namespace AgentsDashboard.Contracts.Features.Settings.Models.Api;












public sealed record CreatePromptSkillRequest(
    string RepositoryId,
    string Name,
    string Trigger,
    string Content,
    string Description,
    bool Enabled = true);
