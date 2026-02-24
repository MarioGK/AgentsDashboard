

namespace AgentsDashboard.ControlPlane.Infrastructure.Data;

public sealed class LiteDbCollectionNameResolver : ILiteDbCollectionNameResolver
{
    private static readonly IReadOnlyDictionary<Type, LiteDbCollectionDefinition> Definitions =
        new Dictionary<Type, LiteDbCollectionDefinition>
        {
            [typeof(RepositoryDocument)] = new("repositories", nameof(RepositoryDocument.Id)),
            [typeof(TaskDocument)] = new("tasks", nameof(TaskDocument.Id)),
            [typeof(RunDocument)] = new("runs", nameof(RunDocument.Id)),
            [typeof(WorkspacePromptEntryDocument)] = new("workspace_prompt_entries", nameof(WorkspacePromptEntryDocument.Id)),
            [typeof(WorkspaceQueuedMessageDocument)] = new("workspace_queued_messages", nameof(WorkspaceQueuedMessageDocument.Id)),
            [typeof(RunQuestionRequestDocument)] = new("run_question_requests", nameof(RunQuestionRequestDocument.Id)),
            [typeof(SemanticChunkDocument)] = new("semantic_chunks", nameof(SemanticChunkDocument.Id)),
            [typeof(RunAiSummaryDocument)] = new("run_ai_summaries", nameof(RunAiSummaryDocument.RunId)),
            [typeof(RunLogEvent)] = new("run_events", nameof(RunLogEvent.Id)),
            [typeof(RunStructuredEventDocument)] = new("run_structured_events", nameof(RunStructuredEventDocument.Id)),
            [typeof(RunDiffSnapshotDocument)] = new("run_diff_snapshots", nameof(RunDiffSnapshotDocument.Id)),
            [typeof(RunToolProjectionDocument)] = new("run_tool_projections", nameof(RunToolProjectionDocument.Id)),
            [typeof(RunSessionProfileDocument)] = new("run_session_profiles", nameof(RunSessionProfileDocument.Id)),
            [typeof(RunInstructionStackDocument)] = new("run_instruction_stacks", nameof(RunInstructionStackDocument.Id)),
            [typeof(RunShareBundleDocument)] = new("run_share_bundles", nameof(RunShareBundleDocument.Id)),
            [typeof(ProviderSecretDocument)] = new("provider_secrets", nameof(ProviderSecretDocument.Id)),
            [typeof(TaskRuntimeRegistration)] = new("task_runtime_registrations", nameof(TaskRuntimeRegistration.Id)),
            [typeof(TaskRuntimeDocument)] = new("task_runtimes", nameof(TaskRuntimeDocument.Id)),
            [typeof(TaskRuntimeEventCheckpointDocument)] = new("task_runtime_event_checkpoints", nameof(TaskRuntimeEventCheckpointDocument.Id)),
            [typeof(WebhookRegistration)] = new("webhooks", nameof(WebhookRegistration.Id)),
            [typeof(SystemSettingsDocument)] = new("settings", nameof(SystemSettingsDocument.Id)),
            [typeof(OrchestratorLeaseDocument)] = new("leases", nameof(OrchestratorLeaseDocument.LeaseName)),
            [typeof(AlertRuleDocument)] = new("alert_rules", nameof(AlertRuleDocument.Id)),
            [typeof(AlertEventDocument)] = new("alert_events", nameof(AlertEventDocument.Id)),
            [typeof(RepositoryInstructionDocument)] = new("repository_instructions", nameof(RepositoryInstructionDocument.Id)),
            [typeof(HarnessProviderSettingsDocument)] = new("harness_provider_settings", nameof(HarnessProviderSettingsDocument.Id)),
            [typeof(PromptSkillDocument)] = new("prompt_skills", nameof(PromptSkillDocument.Id)),
            [typeof(RunArtifactDocument)] = new("run_artifacts", nameof(RunArtifactDocument.Id)),
            [typeof(McpRegistryServerDocument)] = new("mcp_registry_servers", nameof(McpRegistryServerDocument.Id)),
            [typeof(McpRegistryStateDocument)] = new("mcp_registry_state", nameof(McpRegistryStateDocument.Id)),
        };

    public LiteDbCollectionDefinition Resolve<T>()
    {
        if (Definitions.TryGetValue(typeof(T), out var definition))
        {
            return definition;
        }

        throw new InvalidOperationException($"No LiteDB collection mapping found for type '{typeof(T).FullName}'.");
    }
}
