namespace AgentsDashboard.ControlPlane.Infrastructure.Ai.Models;

public sealed record TaskPromptGenerationRequest(
    string RepositoryName,
    string TaskName,
    string Harness,
    string Command);
