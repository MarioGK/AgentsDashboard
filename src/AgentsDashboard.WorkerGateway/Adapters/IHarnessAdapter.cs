using AgentsDashboard.Contracts.Domain;
using AgentsDashboard.Contracts.Worker;

namespace AgentsDashboard.WorkerGateway.Adapters;

public interface IHarnessAdapter
{
    string HarnessName { get; }

    HarnessExecutionContext PrepareContext(DispatchJobRequest request);

    HarnessCommand BuildCommand(HarnessExecutionContext context);

    Task<HarnessResultEnvelope> ExecuteAsync(
        HarnessExecutionContext context,
        HarnessCommand command,
        CancellationToken cancellationToken);

    HarnessResultEnvelope ParseEnvelope(string stdout, string stderr, int exitCode);

    IReadOnlyList<HarnessArtifact> MapArtifacts(HarnessResultEnvelope envelope);

    FailureClassification ClassifyFailure(HarnessResultEnvelope envelope);
}
