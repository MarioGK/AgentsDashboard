namespace AgentsDashboard.ControlPlane.Infrastructure.Ai.Models;

public sealed record TaskPromptGenerationResult(bool Success, string Prompt, string? Error);
