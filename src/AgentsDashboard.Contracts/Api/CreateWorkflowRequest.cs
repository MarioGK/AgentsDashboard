using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record CreateWorkflowRequest(string RepositoryId, string Name, string Description, List<WorkflowStageConfigRequest> Stages);
