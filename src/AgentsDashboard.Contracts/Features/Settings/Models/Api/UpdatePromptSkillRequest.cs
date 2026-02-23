

namespace AgentsDashboard.Contracts.Features.Settings.Models.Api;












public sealed record UpdatePromptSkillRequest(
    string Name,
    string Trigger,
    string Content,
    string Description,
    bool Enabled = true);
