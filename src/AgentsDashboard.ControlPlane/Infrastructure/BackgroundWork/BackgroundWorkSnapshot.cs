namespace AgentsDashboard.ControlPlane.Infrastructure.BackgroundWork;

public sealed record BackgroundWorkSnapshot(
    string WorkId,
    string OperationKey,
    BackgroundWorkKind Kind,
    BackgroundWorkState State,
    int? PercentComplete,
    string Message,
    DateTimeOffset? StartedAt,
    DateTimeOffset? UpdatedAt,
    string? ErrorCode,
    string? ErrorMessage);
