namespace AgentsDashboard.ControlPlane.Features.RuntimeOrchestration.Services.Models;

public sealed record TaskRuntimeHealthIncident(
    string Id,
    DateTime TimestampUtc,
    string RuntimeId,
    TaskRuntimeHealthStatus Status,
    string Reason,
    string Action,
    bool Success,
    string Message);
