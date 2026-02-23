namespace AgentsDashboard.ControlPlane.Features.Settings.Models;

public sealed record ZAiValidationResult(
    bool Success,
    string TestName,
    string Message,
    int StatusCode,
    long DurationMs,
    string ResponsePreview);
