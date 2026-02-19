using System.Collections.Concurrent;

namespace AgentsDashboard.ControlPlane.Services;

public sealed partial class UiRealtimeBroker
{
    private sealed record Subscription(
        Func<object, Task> Handler,
        Func<object, bool>? Filter);

    private sealed class SubscriptionToken(Action onDispose) : IDisposable
    {
        private Action? _onDispose = onDispose;

        public void Dispose()
        {
            Interlocked.Exchange(ref _onDispose, null)?.Invoke();
        }
    }
}
