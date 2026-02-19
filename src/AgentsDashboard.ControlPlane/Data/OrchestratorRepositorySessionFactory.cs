using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.ControlPlane.Data;

public sealed class OrchestratorRepositorySessionFactory(
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
            semanticChunks,
            runAiSummaries,
            runEvents,
            runStructuredEvents,
            runDiffSnapshots,
            runToolProjections,
            runSessionProfiles,
            runInstructionStacks,
            runShareBundles,
            automationDefinitions,
            automationExecutions,
            findings,
            providerSecrets,
            taskRuntimeRegistrations,
            taskRuntimes,
            webhooks,
            settings,
            leases,
            workflows,
            workflowExecutions,
            alertRules,
            alertEvents,
            repositoryInstructions,
            harnessProviderSettings,
            promptSkills,
            liteDbExecutor,
            liteDbDatabase);

        return ValueTask.FromResult(scope);
    }
}
