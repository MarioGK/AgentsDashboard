using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record UpdateWorkflowRequest(string Name, string Description, List<WorkflowStageConfigRequest> Stages, bool Enabled);
