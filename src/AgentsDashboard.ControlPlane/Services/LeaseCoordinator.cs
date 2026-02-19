using AgentsDashboard.ControlPlane.Data;

namespace AgentsDashboard.ControlPlane.Services;

public interface ILeaseCoordinator
{
    Task<IAsyncDisposable?> TryAcquireAsync(string leaseName, TimeSpan ttl, CancellationToken cancellationToken);
}

public sealed partial class LeaseCoordinator(IOrchestratorStore store) : ILeaseCoordinator
{
    private readonly string _ownerId = $"{Environment.MachineName}:{Guid.NewGuid():N}";

    public async Task<IAsyncDisposable?> TryAcquireAsync(string leaseName, TimeSpan ttl, CancellationToken cancellationToken)
    {
        var acquired = await store.TryAcquireLeaseAsync(leaseName, _ownerId, ttl, cancellationToken);
        if (!acquired)
        {
            return null;
        }

        return new LeaseHandle(store, leaseName, _ownerId);
    }

}
