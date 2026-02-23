

namespace AgentsDashboard.ControlPlane.Infrastructure.Data;

public sealed class OrchestratorRepositorySessionFactory(
    IRepository<RepositoryDocument> repositories,
    IRepository<TaskDocument> tasks,
    IRepository<RunDocument> runs,
    IRepository<WorkspacePromptEntryDocument> workspacePromptEntries,
    IRepository<RunQuestionRequestDocument> runQuestionRequests,
    IRepository<SemanticChunkDocument> semanticChunks,
    IRepository<RunAiSummaryDocument> runAiSummaries,
    IRepository<RunLogEvent> runEvents,
    IRepository<RunStructuredEventDocument> runStructuredEvents,
    IRepository<RunDiffSnapshotDocument> runDiffSnapshots,
    IRepository<RunToolProjectionDocument> runToolProjections,
    IRepository<RunSessionProfileDocument> runSessionProfiles,
    IRepository<RunInstructionStackDocument> runInstructionStacks,
    IRepository<RunShareBundleDocument> runShareBundles,
    IRepository<FindingDocument> findings,
    IRepository<ProviderSecretDocument> providerSecrets,
    IRepository<TaskRuntimeRegistration> taskRuntimeRegistrations,
    IRepository<TaskRuntimeDocument> taskRuntimes,
    IRepository<WebhookRegistration> webhooks,
    IRepository<SystemSettingsDocument> settings,
    IRepository<OrchestratorLeaseDocument> leases,
    IRepository<AlertRuleDocument> alertRules,
    IRepository<AlertEventDocument> alertEvents,
    IRepository<RepositoryInstructionDocument> repositoryInstructions,
    IRepository<HarnessProviderSettingsDocument> harnessProviderSettings,
    IRepository<PromptSkillDocument> promptSkills,
    IRepository<McpRegistryServerDocument> mcpRegistryServers,
    IRepository<McpRegistryStateDocument> mcpRegistryState,
    LiteDbExecutor liteDbExecutor,
    LiteDbDatabase liteDbDatabase)
    : IOrchestratorRepositorySessionFactory
{
    public ValueTask<OrchestratorRepositorySession> CreateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var scope = new OrchestratorRepositorySession(
            repositories,
            tasks,
            runs,
            workspacePromptEntries,
            runQuestionRequests,
            semanticChunks,
            runAiSummaries,
            runEvents,
            runStructuredEvents,
            runDiffSnapshots,
            runToolProjections,
            runSessionProfiles,
            runInstructionStacks,
            runShareBundles,
            findings,
            providerSecrets,
            taskRuntimeRegistrations,
            taskRuntimes,
            webhooks,
            settings,
            leases,
            alertRules,
            alertEvents,
            repositoryInstructions,
            harnessProviderSettings,
            promptSkills,
            mcpRegistryServers,
            mcpRegistryState,
            liteDbExecutor,
            liteDbDatabase);

        return ValueTask.FromResult(scope);
    }
}
