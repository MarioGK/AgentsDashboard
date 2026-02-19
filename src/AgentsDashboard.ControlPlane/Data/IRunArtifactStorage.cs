namespace AgentsDashboard.ControlPlane.Data;

public interface IRunArtifactStorage
{
    Task SaveAsync(string runId, string fileName, Stream stream, CancellationToken cancellationToken);
    Task<List<string>> ListAsync(string runId, CancellationToken cancellationToken);
    Task<Stream?> GetAsync(string runId, string fileName, CancellationToken cancellationToken);
    Task DeleteByRunIdsAsync(IReadOnlyList<string> runIds, CancellationToken cancellationToken);
}
