using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.Contracts.Api;












public sealed record UpdateRepositoryInstructionsRequest(List<InstructionFile> InstructionFiles);
