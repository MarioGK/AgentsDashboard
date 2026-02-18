namespace AgentsDashboard.ControlPlane.Services;

public sealed record NotificationMessage(
    string Id,
    DateTimeOffset Timestamp,
    string Title,
    string? Body,
    NotificationSeverity Severity,
    NotificationSource Source,
    string? CorrelationId,
    bool IsRead,
    bool IsDismissed);
