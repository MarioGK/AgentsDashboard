using System.Text.Json;
using AgentsDashboard.Contracts.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AgentsDashboard.ControlPlane.Data;

public sealed class OrchestratorDbContext(DbContextOptions<OrchestratorDbContext> options) : DbContext(options)
{
    public DbSet<ProjectDocument> Projects => Set<ProjectDocument>();
    public DbSet<RepositoryDocument> Repositories => Set<RepositoryDocument>();
    public DbSet<TaskDocument> Tasks => Set<TaskDocument>();
    public DbSet<RunDocument> Runs => Set<RunDocument>();
    public DbSet<RunLogEvent> RunEvents => Set<RunLogEvent>();
    public DbSet<FindingDocument> Findings => Set<FindingDocument>();
    public DbSet<ProviderSecretDocument> ProviderSecrets => Set<ProviderSecretDocument>();
    public DbSet<WorkerRegistration> Workers => Set<WorkerRegistration>();
    public DbSet<WebhookRegistration> Webhooks => Set<WebhookRegistration>();
    public DbSet<ProxyAuditDocument> ProxyAudits => Set<ProxyAuditDocument>();
    public DbSet<SystemSettingsDocument> Settings => Set<SystemSettingsDocument>();
    public DbSet<WorkflowDocument> Workflows => Set<WorkflowDocument>();
    public DbSet<WorkflowExecutionDocument> WorkflowExecutions => Set<WorkflowExecutionDocument>();
    public DbSet<AlertRuleDocument> AlertRules => Set<AlertRuleDocument>();
    public DbSet<AlertEventDocument> AlertEvents => Set<AlertEventDocument>();
    public DbSet<RepositoryInstructionDocument> RepositoryInstructions => Set<RepositoryInstructionDocument>();
    public DbSet<HarnessProviderSettingsDocument> HarnessProviderSettings => Set<HarnessProviderSettingsDocument>();
    public DbSet<TaskTemplateDocument> TaskTemplates => Set<TaskTemplateDocument>();
    public DbSet<AgentDocument> Agents => Set<AgentDocument>();
    public DbSet<WorkflowV2Document> WorkflowsV2 => Set<WorkflowV2Document>();
    public DbSet<WorkflowExecutionV2Document> WorkflowExecutionsV2 => Set<WorkflowExecutionV2Document>();
    public DbSet<WorkflowDeadLetterDocument> WorkflowDeadLetters => Set<WorkflowDeadLetterDocument>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ProjectDocument>().HasKey(x => x.Id);
        modelBuilder.Entity<RepositoryDocument>().HasKey(x => x.Id);
        modelBuilder.Entity<TaskDocument>().HasKey(x => x.Id);
        modelBuilder.Entity<RunDocument>().HasKey(x => x.Id);
        modelBuilder.Entity<RunLogEvent>().HasKey(x => x.Id);
        modelBuilder.Entity<FindingDocument>().HasKey(x => x.Id);
        modelBuilder.Entity<ProviderSecretDocument>().HasKey(x => x.Id);
        modelBuilder.Entity<WorkerRegistration>().HasKey(x => x.Id);
        modelBuilder.Entity<WebhookRegistration>().HasKey(x => x.Id);
        modelBuilder.Entity<ProxyAuditDocument>().HasKey(x => x.Id);
        modelBuilder.Entity<SystemSettingsDocument>().HasKey(x => x.Id);
        modelBuilder.Entity<WorkflowDocument>().HasKey(x => x.Id);
        modelBuilder.Entity<WorkflowExecutionDocument>().HasKey(x => x.Id);
        modelBuilder.Entity<AlertRuleDocument>().HasKey(x => x.Id);
        modelBuilder.Entity<AlertEventDocument>().HasKey(x => x.Id);
        modelBuilder.Entity<RepositoryInstructionDocument>().HasKey(x => x.Id);
        modelBuilder.Entity<HarnessProviderSettingsDocument>().HasKey(x => x.Id);
        modelBuilder.Entity<TaskTemplateDocument>().HasKey(x => x.Id);
        modelBuilder.Entity<AgentDocument>().HasKey(x => x.Id);
        modelBuilder.Entity<WorkflowV2Document>().HasKey(x => x.Id);
        modelBuilder.Entity<WorkflowExecutionV2Document>().HasKey(x => x.Id);
        modelBuilder.Entity<WorkflowDeadLetterDocument>().HasKey(x => x.Id);

        modelBuilder.Entity<RepositoryDocument>()
            .Property(x => x.InstructionFiles)
            .HasConversion(JsonConverter<List<InstructionFile>>());

        modelBuilder.Entity<TaskDocument>()
            .Property(x => x.RetryPolicy)
            .HasConversion(JsonConverter<RetryPolicyConfig>());
        modelBuilder.Entity<TaskDocument>()
            .Property(x => x.Timeouts)
            .HasConversion(JsonConverter<TimeoutConfig>());
        modelBuilder.Entity<TaskDocument>()
            .Property(x => x.ApprovalProfile)
            .HasConversion(JsonConverter<ApprovalProfileConfig>());
        modelBuilder.Entity<TaskDocument>()
            .Property(x => x.SandboxProfile)
            .HasConversion(JsonConverter<SandboxProfileConfig>());
        modelBuilder.Entity<TaskDocument>()
            .Property(x => x.ArtifactPolicy)
            .HasConversion(JsonConverter<ArtifactPolicyConfig>());
        modelBuilder.Entity<TaskDocument>()
            .Property(x => x.InstructionFiles)
            .HasConversion(JsonConverter<List<InstructionFile>>());
        modelBuilder.Entity<TaskDocument>()
            .Property(x => x.ArtifactPatterns)
            .HasConversion(JsonConverter<List<string>>());
        modelBuilder.Entity<TaskDocument>()
            .Property(x => x.LinkedFailureRuns)
            .HasConversion(JsonConverter<List<string>>());

        modelBuilder.Entity<WorkflowDocument>()
            .Property(x => x.Stages)
            .HasConversion(JsonConverter<List<WorkflowStageConfig>>());
        modelBuilder.Entity<WorkflowExecutionDocument>()
            .Property(x => x.StageResults)
            .HasConversion(JsonConverter<List<WorkflowStageResult>>());

        modelBuilder.Entity<HarnessProviderSettingsDocument>()
            .Property(x => x.AdditionalSettings)
            .HasConversion(JsonConverter<Dictionary<string, string>>());

        modelBuilder.Entity<TaskTemplateDocument>()
            .Property(x => x.RetryPolicy)
            .HasConversion(JsonConverter<RetryPolicyConfig>());
        modelBuilder.Entity<TaskTemplateDocument>()
            .Property(x => x.Timeouts)
            .HasConversion(JsonConverter<TimeoutConfig>());
        modelBuilder.Entity<TaskTemplateDocument>()
            .Property(x => x.SandboxProfile)
            .HasConversion(JsonConverter<SandboxProfileConfig>());
        modelBuilder.Entity<TaskTemplateDocument>()
            .Property(x => x.ArtifactPolicy)
            .HasConversion(JsonConverter<ArtifactPolicyConfig>());
        modelBuilder.Entity<TaskTemplateDocument>()
            .Property(x => x.Commands)
            .HasConversion(JsonConverter<List<string>>());
        modelBuilder.Entity<TaskTemplateDocument>()
            .Property(x => x.ArtifactPatterns)
            .HasConversion(JsonConverter<List<string>>());
        modelBuilder.Entity<TaskTemplateDocument>()
            .Property(x => x.LinkedFailureRuns)
            .HasConversion(JsonConverter<List<string>>());

        modelBuilder.Entity<AgentDocument>()
            .Property(x => x.RetryPolicy)
            .HasConversion(JsonConverter<RetryPolicyConfig>());
        modelBuilder.Entity<AgentDocument>()
            .Property(x => x.Timeouts)
            .HasConversion(JsonConverter<TimeoutConfig>());
        modelBuilder.Entity<AgentDocument>()
            .Property(x => x.SandboxProfile)
            .HasConversion(JsonConverter<SandboxProfileConfig>());
        modelBuilder.Entity<AgentDocument>()
            .Property(x => x.ArtifactPolicy)
            .HasConversion(JsonConverter<ArtifactPolicyConfig>());
        modelBuilder.Entity<AgentDocument>()
            .Property(x => x.ArtifactPatterns)
            .HasConversion(JsonConverter<List<string>>());
        modelBuilder.Entity<AgentDocument>()
            .Property(x => x.InstructionFiles)
            .HasConversion(JsonConverter<List<InstructionFile>>());

        modelBuilder.Entity<WorkflowV2Document>()
            .Property(x => x.Nodes)
            .HasConversion(JsonConverter<List<WorkflowNodeConfig>>());
        modelBuilder.Entity<WorkflowV2Document>()
            .Property(x => x.Edges)
            .HasConversion(JsonConverter<List<WorkflowEdgeConfig>>());
        modelBuilder.Entity<WorkflowV2Document>()
            .Property(x => x.Trigger)
            .HasConversion(JsonConverter<WorkflowV2TriggerConfig>());

        modelBuilder.Entity<WorkflowExecutionV2Document>()
            .Property(x => x.Context)
            .HasConversion(JsonConverter<Dictionary<string, System.Text.Json.JsonElement>>());
        modelBuilder.Entity<WorkflowExecutionV2Document>()
            .Property(x => x.NodeResults)
            .HasConversion(JsonConverter<List<WorkflowNodeResult>>());

        modelBuilder.Entity<WorkflowDeadLetterDocument>()
            .Property(x => x.InputContextSnapshot)
            .HasConversion(JsonConverter<Dictionary<string, System.Text.Json.JsonElement>>());

        modelBuilder.Entity<ProjectDocument>()
            .HasIndex(x => x.Name);
        modelBuilder.Entity<RepositoryDocument>()
            .HasIndex(x => x.ProjectId);
        modelBuilder.Entity<TaskDocument>()
            .HasIndex(x => x.RepositoryId);
        modelBuilder.Entity<TaskDocument>()
            .HasIndex(x => x.NextRunAtUtc);
        modelBuilder.Entity<RunDocument>()
            .HasIndex(x => new { x.RepositoryId, x.CreatedAtUtc });
        modelBuilder.Entity<RunDocument>()
            .HasIndex(x => x.State);
        modelBuilder.Entity<RunDocument>()
            .HasIndex(x => new { x.ProjectId, x.State });
        modelBuilder.Entity<RunDocument>()
            .HasIndex(x => new { x.TaskId, x.State });
        modelBuilder.Entity<FindingDocument>()
            .HasIndex(x => new { x.RepositoryId, x.CreatedAtUtc });
        modelBuilder.Entity<FindingDocument>()
            .HasIndex(x => x.State);
        modelBuilder.Entity<RunLogEvent>()
            .HasIndex(x => new { x.RunId, x.TimestampUtc });
        modelBuilder.Entity<ProviderSecretDocument>()
            .HasIndex(x => new { x.RepositoryId, x.Provider })
            .IsUnique();
        modelBuilder.Entity<WorkerRegistration>()
            .HasIndex(x => x.WorkerId)
            .IsUnique();
        modelBuilder.Entity<WebhookRegistration>()
            .HasIndex(x => x.RepositoryId);
        modelBuilder.Entity<ProxyAuditDocument>()
            .HasIndex(x => new { x.RunId, x.TimestampUtc });
        modelBuilder.Entity<WorkflowDocument>()
            .HasIndex(x => x.RepositoryId);
        modelBuilder.Entity<WorkflowExecutionDocument>()
            .HasIndex(x => new { x.WorkflowId, x.CreatedAtUtc });
        modelBuilder.Entity<WorkflowExecutionDocument>()
            .HasIndex(x => x.State);
        modelBuilder.Entity<AlertRuleDocument>()
            .HasIndex(x => x.Enabled);
        modelBuilder.Entity<AlertEventDocument>()
            .HasIndex(x => x.FiredAtUtc);
        modelBuilder.Entity<AlertEventDocument>()
            .HasIndex(x => x.RuleId);
        modelBuilder.Entity<RepositoryInstructionDocument>()
            .HasIndex(x => x.RepositoryId);
        modelBuilder.Entity<RepositoryInstructionDocument>()
            .HasIndex(x => new { x.RepositoryId, x.Priority });
        modelBuilder.Entity<HarnessProviderSettingsDocument>()
            .HasIndex(x => new { x.RepositoryId, x.Harness })
            .IsUnique();

        modelBuilder.Entity<AgentDocument>()
            .HasIndex(x => x.RepositoryId);
        modelBuilder.Entity<AgentDocument>()
            .HasIndex(x => new { x.RepositoryId, x.Name })
            .IsUnique();
        modelBuilder.Entity<WorkflowV2Document>()
            .HasIndex(x => x.RepositoryId);
        modelBuilder.Entity<WorkflowExecutionV2Document>()
            .HasIndex(x => new { x.WorkflowV2Id, x.CreatedAtUtc });
        modelBuilder.Entity<WorkflowExecutionV2Document>()
            .HasIndex(x => x.State);
        modelBuilder.Entity<WorkflowDeadLetterDocument>()
            .HasIndex(x => x.ExecutionId);
        modelBuilder.Entity<WorkflowDeadLetterDocument>()
            .HasIndex(x => x.Replayed);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties().Where(p => p.ClrType == typeof(Guid)))
            {
                property.SetColumnType("BLOB");
                property.SetValueConverter(new GuidToBytesConverter());
            }

            foreach (var property in entityType.GetProperties().Where(p => p.ClrType == typeof(Guid?)))
            {
                property.SetColumnType("BLOB");
                property.SetValueConverter(
                    new ValueConverter<Guid?, byte[]?>(
                        value => value.HasValue ? value.Value.ToByteArray() : null,
                        value => value == null ? null : new Guid(value)));
            }
        }
    }

    private static ValueConverter<T, string> JsonConverter<T>()
    {
        return new ValueConverter<T, string>(
            value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
            value => JsonSerializer.Deserialize<T>(value, (JsonSerializerOptions?)null)!);
    }
}
