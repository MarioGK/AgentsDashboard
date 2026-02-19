using AgentsDashboard.ControlPlane.Data;

namespace AgentsDashboard.ControlPlane.Services;

public sealed partial class LeaseCoordinator
{
    private sealed class LeaseHandle(IOrchestratorStore store, string leaseName, string ownerId) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await store.ReleaseLeaseAsync(leaseName, ownerId, CancellationToken.None);
            }
            catch
            {
            }
        }
    }
}
