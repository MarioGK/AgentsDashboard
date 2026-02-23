using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace AgentsDashboard.ControlPlane.Infrastructure.BackgroundWork;

public sealed class NotificationService : INotificationService
{
    private const int MaxRetainedNotifications = 300;

    private readonly LinkedList<NotificationMessage> _ringBuffer = [];
    private readonly ConcurrentDictionary<Guid, Channel<NotificationMessage>> _subscribers = [];
    private readonly object _sync = new();

    public Task PublishAsync(
        string title,
        string? message,
        NotificationSeverity severity,
        NotificationSource source = NotificationSource.BackgroundWork,
        string? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException("Notification title is required.", nameof(title));
        }

        var notification = new NotificationMessage(
            Id: Guid.NewGuid().ToString("N"),
            Timestamp: DateTimeOffset.UtcNow,
            Title: title,
            Body: message,
            Severity: severity,
            Source: source,
            CorrelationId: correlationId,
            IsRead: false,
            IsDismissed: false);

        lock (_sync)
        {
            _ringBuffer.AddLast(notification);
            while (_ringBuffer.Count > MaxRetainedNotifications)
            {
                _ringBuffer.RemoveFirst();
            }
        }

        foreach (var subscriber in _subscribers.Values)
        {
            subscriber.Writer.TryWrite(notification);
        }

        return Task.CompletedTask;
    }

    public IReadOnlyCollection<NotificationMessage> Snapshot()
    {
        lock (_sync)
        {
            return _ringBuffer
                .Reverse()
                .ToArray();
        }
    }

    public async IAsyncEnumerable<NotificationMessage> StreamAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var subscriberId = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<NotificationMessage>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });

        if (!_subscribers.TryAdd(subscriberId, channel))
        {
            throw new InvalidOperationException("Failed to subscribe to notification stream.");
        }

        try
        {
            await foreach (var notification in channel.Reader.ReadAllAsync(cancellationToken))
            {
                yield return notification;
            }
        }
        finally
        {
            if (_subscribers.TryRemove(subscriberId, out var subscriber))
            {
                subscriber.Writer.TryComplete();
            }
        }
    }
}
