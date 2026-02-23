namespace AgentsDashboard.ControlPlane.Infrastructure.BackgroundWork;

public interface INotificationSink
{
    Task PublishAsync(
        string title,
        string? message,
        NotificationSeverity severity,
        NotificationSource source = NotificationSource.BackgroundWork,
        string? correlationId = null);
}
