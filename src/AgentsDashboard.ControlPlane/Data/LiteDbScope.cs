using AgentsDashboard.Contracts.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace AgentsDashboard.ControlPlane.Data;

public sealed class LiteDbScope : IAsyncDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<Type, ILiteDbSet> _sets = [];

    public LiteDbScope(
        IServiceProvider serviceProvider,
        LiteDbExecutor executor,
        LiteDbDatabase database)
    {
        _serviceProvider = serviceProvider;
        Database = new LiteDbScopeDatabase(executor, database);
    }

    public LiteDbScopeDatabase Database { get; }

    public LiteDbSet<RepositoryDocument> Repositories => GetSet<RepositoryDocument>();
    public LiteDbSet<TaskDocument> Tasks => GetSet<TaskDocument>();
    public LiteDbSet<RunDocument> Runs => GetSet<RunDocument>();
    public LiteDbSet<WorkspacePromptEntryDocument> WorkspacePromptEntries => GetSet<WorkspacePromptEntryDocument>();
    public LiteDbSet<SemanticChunkDocument> SemanticChunks => GetSet<SemanticChunkDocument>();
    public LiteDbSet<RunAiSummaryDocument> RunAiSummaries => GetSet<RunAiSummaryDocument>();
    public LiteDbSet<RunLogEvent> RunEvents => GetSet<RunLogEvent>();
    public LiteDbSet<RunStructuredEventDocument> RunStructuredEvents => GetSet<RunStructuredEventDocument>();
    public LiteDbSet<RunDiffSnapshotDocument> RunDiffSnapshots => GetSet<RunDiffSnapshotDocument>();
    public LiteDbSet<RunToolProjectionDocument> RunToolProjections => GetSet<RunToolProjectionDocument>();
    public LiteDbSet<RunSessionProfileDocument> RunSessionProfiles => GetSet<RunSessionProfileDocument>();
    public LiteDbSet<RunInstructionStackDocument> RunInstructionStacks => GetSet<RunInstructionStackDocument>();
    public LiteDbSet<RunShareBundleDocument> RunShareBundles => GetSet<RunShareBundleDocument>();
    public LiteDbSet<AutomationDefinitionDocument> AutomationDefinitions => GetSet<AutomationDefinitionDocument>();
    public LiteDbSet<AutomationExecutionDocument> AutomationExecutions => GetSet<AutomationExecutionDocument>();
    public LiteDbSet<FindingDocument> Findings => GetSet<FindingDocument>();
    public LiteDbSet<ProviderSecretDocument> ProviderSecrets => GetSet<ProviderSecretDocument>();
    public LiteDbSet<TaskRuntimeRegistration> TaskRuntimeRegistrations => GetSet<TaskRuntimeRegistration>();
    public LiteDbSet<TaskRuntimeDocument> TaskRuntimes => GetSet<TaskRuntimeDocument>();
    public LiteDbSet<WebhookRegistration> Webhooks => GetSet<WebhookRegistration>();
    public LiteDbSet<ProxyAuditDocument> ProxyAudits => GetSet<ProxyAuditDocument>();
    public LiteDbSet<SystemSettingsDocument> Settings => GetSet<SystemSettingsDocument>();
    public LiteDbSet<OrchestratorLeaseDocument> Leases => GetSet<OrchestratorLeaseDocument>();
    public LiteDbSet<WorkflowDocument> Workflows => GetSet<WorkflowDocument>();
    public LiteDbSet<WorkflowExecutionDocument> WorkflowExecutions => GetSet<WorkflowExecutionDocument>();
    public LiteDbSet<AlertRuleDocument> AlertRules => GetSet<AlertRuleDocument>();
    public LiteDbSet<AlertEventDocument> AlertEvents => GetSet<AlertEventDocument>();
    public LiteDbSet<RepositoryInstructionDocument> RepositoryInstructions => GetSet<RepositoryInstructionDocument>();
    public LiteDbSet<HarnessProviderSettingsDocument> HarnessProviderSettings => GetSet<HarnessProviderSettingsDocument>();
    public LiteDbSet<TaskTemplateDocument> TaskTemplates => GetSet<TaskTemplateDocument>();
    public LiteDbSet<PromptSkillDocument> PromptSkills => GetSet<PromptSkillDocument>();
    public LiteDbSet<RunArtifactDocument> RunArtifacts => GetSet<RunArtifactDocument>();

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        foreach (var set in _sets.Values)
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

    private LiteDbSet<T> GetSet<T>()
        where T : class
    {
        if (_sets.TryGetValue(typeof(T), out var existing))
        {
            return (LiteDbSet<T>)existing;
        }

        var repository = _serviceProvider.GetRequiredService<IRepository<T>>();
        var created = new LiteDbSet<T>(repository);
        _sets[typeof(T)] = created;
        return created;
    }
}
