namespace AgentsDashboard.ControlPlane.Infrastructure.BackgroundWork;

public interface INotificationService : INotificationSink
{
    IReadOnlyCollection<NotificationMessage> Snapshot();

    IAsyncEnumerable<NotificationMessage> StreamAsync(CancellationToken cancellationToken);
}
