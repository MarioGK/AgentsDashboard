namespace AgentsDashboard.ControlPlane.Services;

public interface IBackgroundWorkCoordinator
{
    string Enqueue(
        BackgroundWorkKind kind,
        string operationKey,
        Func<CancellationToken, IProgress<BackgroundWorkSnapshot>, Task> work,
        bool dedupeByOperationKey = true,
        bool isCritical = false);

    IReadOnlyCollection<BackgroundWorkSnapshot> Snapshot();

    bool TryGet(string workId, out BackgroundWorkSnapshot snapshot);

    event Action<BackgroundWorkSnapshot>? Updated;
}
