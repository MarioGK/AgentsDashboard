using System.Collections.Concurrent;

namespace AgentsDashboard.ControlPlane.Services;

public interface IUiRealtimeBroker
{
    IDisposable Subscribe<TEvent>(Func<TEvent, Task> handler, Func<TEvent, bool>? filter = null);
    Task PublishAsync<TEvent>(TEvent message, CancellationToken cancellationToken = default);
}

public sealed partial class UiRealtimeBroker(ILogger<UiRealtimeBroker> logger) : IUiRealtimeBroker
{
    private readonly ConcurrentDictionary<Type, ConcurrentDictionary<Guid, Subscription>> _subscriptions = [];

    public IDisposable Subscribe<TEvent>(Func<TEvent, Task> handler, Func<TEvent, bool>? filter = null)
    {
        var eventType = typeof(TEvent);
        var handlers = _subscriptions.GetOrAdd(eventType, static _ => []);
        var subscriptionId = Guid.NewGuid();

        handlers[subscriptionId] = new Subscription(
            payload => handler((TEvent)payload),
            filter is null ? null : payload => filter((TEvent)payload));

        return new SubscriptionToken(() =>
        {
            if (_subscriptions.TryGetValue(eventType, out var current))
            {
                current.TryRemove(subscriptionId, out _);
                if (current.IsEmpty)
                {
                    _subscriptions.TryRemove(eventType, out _);
                }
            }
        });
    }

    public async Task PublishAsync<TEvent>(TEvent message, CancellationToken cancellationToken = default)
    {
        if (!_subscriptions.TryGetValue(typeof(TEvent), out var handlers) || handlers.IsEmpty)
        {
            return;
        }

        var payload = (object)message!;
        var snapshot = handlers.Values.ToArray();

        foreach (var subscription in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                if (subscription.Filter is not null && !subscription.Filter(payload))
                {
                    continue;
                }

                await subscription.Handler(payload);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to deliver UI realtime event {EventType}", typeof(TEvent).Name);
            }
        }
    }


}
