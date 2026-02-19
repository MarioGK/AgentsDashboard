using System.Collections.Concurrent;
using System.Threading.Channels;

namespace AgentsDashboard.ControlPlane.Services;

public sealed partial class BackgroundWorkScheduler
{
    private sealed record QueuedBackgroundWork(
        string WorkId,
        string OperationKey,
        BackgroundWorkKind Kind,
        Func<CancellationToken, IProgress<BackgroundWorkSnapshot>, Task> Work,
        bool DedupeByOperationKey,
        bool IsCritical);
}
