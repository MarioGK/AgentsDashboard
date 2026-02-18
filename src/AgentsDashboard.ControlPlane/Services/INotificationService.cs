namespace AgentsDashboard.ControlPlane.Services;

public interface INotificationService : INotificationSink
{
    IReadOnlyCollection<NotificationMessage> Snapshot();

    IAsyncEnumerable<NotificationMessage> StreamAsync(CancellationToken cancellationToken);
}
