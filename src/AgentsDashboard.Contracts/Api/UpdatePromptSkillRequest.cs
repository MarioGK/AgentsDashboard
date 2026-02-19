using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record UpdatePromptSkillRequest(
    string Name,
    string Trigger,
    string Content,
    string Description,
    bool Enabled = true);
