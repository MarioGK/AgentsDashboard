namespace AgentsDashboard.ControlPlane.Services;

using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.ControlPlane.Data;


public record TaskTemplate(
    string Id,
    string Name,
    string Description,
    string Harness,
    TaskKind Kind,
    string Prompt,
    string Command
);
