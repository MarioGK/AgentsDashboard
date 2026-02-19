using System.Text.Json;
using System.Text.RegularExpressions;
using AgentsDashboard.Contracts.Api;
using AgentsDashboard.Contracts.Domain;
using Cronos;

namespace AgentsDashboard.ControlPlane.Data;

public sealed partial class OrchestratorStore
    IRepository<RepositoryDocument> repositories,
    IRepository<TaskDocument> tasks,
    IRepository<RunDocument> runs,
    IRepository<WorkspacePromptEntryDocument> workspacePromptEntries,
    IRepository<SemanticChunkDocument> semanticChunks,
    IRepository<RunAiSummaryDocument> runAiSummaries,
    IRepository<RunLogEvent> runEvents,
    IRepository<RunStructuredEventDocument> runStructuredEvents,
    IRepository<RunDiffSnapshotDocument> runDiffSnapshots,
    IRepository<RunToolProjectionDocument> runToolProjections,
    IRepository<RunSessionProfileDocument> runSessionProfiles,
    IRepository<RunInstructionStackDocument> runInstructionStacks,
    IRepository<RunShareBundleDocument> runShareBundles,
    IRepository<AutomationDefinitionDocument> automationDefinitions,
    IRepository<AutomationExecutionDocument> automationExecutions,
    IRepository<FindingDocument> findings,
    IRepository<ProviderSecretDocument> providerSecrets,
    IRepository<TaskRuntimeRegistration> taskRuntimeRegistrations,
    IRepository<TaskRuntimeDocument> taskRuntimes,
    IRepository<WebhookRegistration> webhooks,
    IRepository<SystemSettingsDocument> settings,
    IRepository<OrchestratorLeaseDocument> leases,
    IRepository<WorkflowDocument> workflows,
    IRepository<WorkflowExecutionDocument> workflowExecutions,
    IRepository<AlertRuleDocument> alertRules,
    IRepository<AlertEventDocument> alertEvents,
    IRepository<RepositoryInstructionDocument> repositoryInstructions,
    IRepository<HarnessProviderSettingsDocument> harnessProviderSettings,
    IRepository<PromptSkillDocument> promptSkills,
    IRunArtifactStorage runArtifactStorage,
    LiteDbExecutor liteDbExecutor,
    LiteDbDatabase liteDbDatabase) : IOrchestratorStore, IAsyncDisposable
{
    private sealed record RunPruneSeed(
        string RunId,
        string TaskId,
        string RepositoryId);

    private sealed record TaskCleanupSeed(
        string TaskId,
        string RepositoryId,
        DateTime CreatedAtUtc,
        bool Enabled);

    private sealed record TaskRunAggregate(
        string TaskId,
        int RunCount,
        DateTime? OldestRunAtUtc,
        DateTime? LatestRunAtUtc,
        bool HasActiveRuns);

    private sealed record TaskTimestampAggregate(
        string TaskId,
        DateTime? TimestampUtc);
}
