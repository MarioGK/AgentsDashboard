

namespace AgentsDashboard.ControlPlane.Infrastructure.Data;

public sealed class OrchestratorRepositorySessionFactory(
    IRepository<RepositoryDocument> repositories,
    IRepository<TaskDocument> tasks,
    IRepository<RunDocument> runs,
    IRepository<WorkspacePromptEntryDocument> workspacePromptEntries,
    IRepository<WorkspaceQueuedMessageDocument> workspaceQueuedMessages,
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
    IRepository<ProviderSecretDocument> providerSecrets,
    IRepository<TaskRuntimeRegistration> taskRuntimeRegistrations,
    IRepository<TaskRuntimeDocument> taskRuntimes,
    IRepository<TaskRuntimeEventCheckpointDocument> taskRuntimeEventCheckpoints,
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
            workspaceQueuedMessages,
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
                providerSecrets,
                taskRuntimeRegistrations,
            taskRuntimes,
            taskRuntimeEventCheckpoints,
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
