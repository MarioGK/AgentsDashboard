using AgentsDashboard.Contracts.Domain;

namespace AgentsDashboard.ControlPlane.Data;

public sealed class OrchestratorRepositorySession : IAsyncDisposable
{
    private readonly IReadOnlyList<ITrackedRepositorySet> _sets;

    public OrchestratorRepositorySession(
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
        IRepository<ProxyAuditDocument> proxyAudits,
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
    {
        Repositories = new TrackedRepositorySet<RepositoryDocument>(repositories);
        Tasks = new TrackedRepositorySet<TaskDocument>(tasks);
        Runs = new TrackedRepositorySet<RunDocument>(runs);
        WorkspacePromptEntries = new TrackedRepositorySet<WorkspacePromptEntryDocument>(workspacePromptEntries);
        SemanticChunks = new TrackedRepositorySet<SemanticChunkDocument>(semanticChunks);
        RunAiSummaries = new TrackedRepositorySet<RunAiSummaryDocument>(runAiSummaries);
        RunEvents = new TrackedRepositorySet<RunLogEvent>(runEvents);
        RunStructuredEvents = new TrackedRepositorySet<RunStructuredEventDocument>(runStructuredEvents);
        RunDiffSnapshots = new TrackedRepositorySet<RunDiffSnapshotDocument>(runDiffSnapshots);
        RunToolProjections = new TrackedRepositorySet<RunToolProjectionDocument>(runToolProjections);
        RunSessionProfiles = new TrackedRepositorySet<RunSessionProfileDocument>(runSessionProfiles);
        RunInstructionStacks = new TrackedRepositorySet<RunInstructionStackDocument>(runInstructionStacks);
        RunShareBundles = new TrackedRepositorySet<RunShareBundleDocument>(runShareBundles);
        AutomationDefinitions = new TrackedRepositorySet<AutomationDefinitionDocument>(automationDefinitions);
        AutomationExecutions = new TrackedRepositorySet<AutomationExecutionDocument>(automationExecutions);
        Findings = new TrackedRepositorySet<FindingDocument>(findings);
        ProviderSecrets = new TrackedRepositorySet<ProviderSecretDocument>(providerSecrets);
        TaskRuntimeRegistrations = new TrackedRepositorySet<TaskRuntimeRegistration>(taskRuntimeRegistrations);
        TaskRuntimes = new TrackedRepositorySet<TaskRuntimeDocument>(taskRuntimes);
        Webhooks = new TrackedRepositorySet<WebhookRegistration>(webhooks);
        ProxyAudits = new TrackedRepositorySet<ProxyAuditDocument>(proxyAudits);
        Settings = new TrackedRepositorySet<SystemSettingsDocument>(settings);
        Leases = new TrackedRepositorySet<OrchestratorLeaseDocument>(leases);
        Workflows = new TrackedRepositorySet<WorkflowDocument>(workflows);
        WorkflowExecutions = new TrackedRepositorySet<WorkflowExecutionDocument>(workflowExecutions);
        AlertRules = new TrackedRepositorySet<AlertRuleDocument>(alertRules);
        AlertEvents = new TrackedRepositorySet<AlertEventDocument>(alertEvents);
        RepositoryInstructions = new TrackedRepositorySet<RepositoryInstructionDocument>(repositoryInstructions);
        HarnessProviderSettings = new TrackedRepositorySet<HarnessProviderSettingsDocument>(harnessProviderSettings);
        PromptSkills = new TrackedRepositorySet<PromptSkillDocument>(promptSkills);
        Database = new LiteDbExecutionDatabase(liteDbExecutor, liteDbDatabase);

        _sets =
        [
            Repositories,
            Tasks,
            Runs,
            WorkspacePromptEntries,
            SemanticChunks,
            RunAiSummaries,
            RunEvents,
            RunStructuredEvents,
            RunDiffSnapshots,
            RunToolProjections,
            RunSessionProfiles,
            RunInstructionStacks,
            RunShareBundles,
            AutomationDefinitions,
            AutomationExecutions,
            Findings,
            ProviderSecrets,
            TaskRuntimeRegistrations,
            TaskRuntimes,
            Webhooks,
            ProxyAudits,
            Settings,
            Leases,
            Workflows,
            WorkflowExecutions,
            AlertRules,
            AlertEvents,
            RepositoryInstructions,
            HarnessProviderSettings,
            PromptSkills,
        ];
    }

    public LiteDbExecutionDatabase Database { get; }
    public TrackedRepositorySet<RepositoryDocument> Repositories { get; }
    public TrackedRepositorySet<TaskDocument> Tasks { get; }
    public TrackedRepositorySet<RunDocument> Runs { get; }
    public TrackedRepositorySet<WorkspacePromptEntryDocument> WorkspacePromptEntries { get; }
    public TrackedRepositorySet<SemanticChunkDocument> SemanticChunks { get; }
    public TrackedRepositorySet<RunAiSummaryDocument> RunAiSummaries { get; }
    public TrackedRepositorySet<RunLogEvent> RunEvents { get; }
    public TrackedRepositorySet<RunStructuredEventDocument> RunStructuredEvents { get; }
    public TrackedRepositorySet<RunDiffSnapshotDocument> RunDiffSnapshots { get; }
    public TrackedRepositorySet<RunToolProjectionDocument> RunToolProjections { get; }
    public TrackedRepositorySet<RunSessionProfileDocument> RunSessionProfiles { get; }
    public TrackedRepositorySet<RunInstructionStackDocument> RunInstructionStacks { get; }
    public TrackedRepositorySet<RunShareBundleDocument> RunShareBundles { get; }
    public TrackedRepositorySet<AutomationDefinitionDocument> AutomationDefinitions { get; }
    public TrackedRepositorySet<AutomationExecutionDocument> AutomationExecutions { get; }
    public TrackedRepositorySet<FindingDocument> Findings { get; }
    public TrackedRepositorySet<ProviderSecretDocument> ProviderSecrets { get; }
    public TrackedRepositorySet<TaskRuntimeRegistration> TaskRuntimeRegistrations { get; }
    public TrackedRepositorySet<TaskRuntimeDocument> TaskRuntimes { get; }
    public TrackedRepositorySet<WebhookRegistration> Webhooks { get; }
    public TrackedRepositorySet<ProxyAuditDocument> ProxyAudits { get; }
    public TrackedRepositorySet<SystemSettingsDocument> Settings { get; }
    public TrackedRepositorySet<OrchestratorLeaseDocument> Leases { get; }
    public TrackedRepositorySet<WorkflowDocument> Workflows { get; }
    public TrackedRepositorySet<WorkflowExecutionDocument> WorkflowExecutions { get; }
    public TrackedRepositorySet<AlertRuleDocument> AlertRules { get; }
    public TrackedRepositorySet<AlertEventDocument> AlertEvents { get; }
    public TrackedRepositorySet<RepositoryInstructionDocument> RepositoryInstructions { get; }
    public TrackedRepositorySet<HarnessProviderSettingsDocument> HarnessProviderSettings { get; }
    public TrackedRepositorySet<PromptSkillDocument> PromptSkills { get; }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        foreach (var set in _sets)
        {
            await set.SaveChangesAsync(cancellationToken);
        }
    }

    public LiteDbEntityEntry<T> Entry<T>(T entity)
        where T : class
    {
        return new LiteDbEntityEntry<T>(entity);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
