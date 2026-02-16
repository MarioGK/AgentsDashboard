using System.Text.Json;
using AgentsDashboard.Contracts.Domain;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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
    public DbSet<OrchestratorLeaseDocument> Leases => Set<OrchestratorLeaseDocument>();
    public DbSet<WorkflowDocument> Workflows => Set<WorkflowDocument>();
    public DbSet<WorkflowExecutionDocument> WorkflowExecutions => Set<WorkflowExecutionDocument>();
    public DbSet<AlertRuleDocument> AlertRules => Set<AlertRuleDocument>();
    public DbSet<AlertEventDocument> AlertEvents => Set<AlertEventDocument>();
    public DbSet<RepositoryInstructionDocument> RepositoryInstructions => Set<RepositoryInstructionDocument>();
    public DbSet<HarnessProviderSettingsDocument> HarnessProviderSettings => Set<HarnessProviderSettingsDocument>();
    public DbSet<TaskTemplateDocument> TaskTemplates => Set<TaskTemplateDocument>();
    public DbSet<TerminalSessionDocument> TerminalSessions => Set<TerminalSessionDocument>();
    public DbSet<TerminalAuditEventDocument> TerminalAuditEvents => Set<TerminalAuditEventDocument>();

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
        modelBuilder.Entity<OrchestratorLeaseDocument>().HasKey(x => x.LeaseName);
        modelBuilder.Entity<WorkflowDocument>().HasKey(x => x.Id);
        modelBuilder.Entity<WorkflowExecutionDocument>().HasKey(x => x.Id);
        modelBuilder.Entity<AlertRuleDocument>().HasKey(x => x.Id);
        modelBuilder.Entity<AlertEventDocument>().HasKey(x => x.Id);
        modelBuilder.Entity<RepositoryInstructionDocument>().HasKey(x => x.Id);
        modelBuilder.Entity<HarnessProviderSettingsDocument>().HasKey(x => x.Id);
        modelBuilder.Entity<TaskTemplateDocument>().HasKey(x => x.Id);
        modelBuilder.Entity<TerminalSessionDocument>().HasKey(x => x.Id);
        modelBuilder.Entity<TerminalAuditEventDocument>().HasKey(x => x.Id);

        var repositoryInstructionFiles = modelBuilder.Entity<RepositoryDocument>()
            .Property(x => x.InstructionFiles)
            .HasConversion(JsonConverter<List<InstructionFile>>());
        repositoryInstructionFiles.Metadata.SetValueComparer(JsonValueComparer<List<InstructionFile>>());

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
        var taskInstructionFiles = modelBuilder.Entity<TaskDocument>()
            .Property(x => x.InstructionFiles)
            .HasConversion(JsonConverter<List<InstructionFile>>());
        taskInstructionFiles.Metadata.SetValueComparer(JsonValueComparer<List<InstructionFile>>());

        var taskArtifactPatterns = modelBuilder.Entity<TaskDocument>()
            .Property(x => x.ArtifactPatterns)
            .HasConversion(JsonConverter<List<string>>());
        taskArtifactPatterns.Metadata.SetValueComparer(JsonValueComparer<List<string>>());

        var taskLinkedFailureRuns = modelBuilder.Entity<TaskDocument>()
            .Property(x => x.LinkedFailureRuns)
            .HasConversion(JsonConverter<List<string>>());
        taskLinkedFailureRuns.Metadata.SetValueComparer(JsonValueComparer<List<string>>());

        var workflowStages = modelBuilder.Entity<WorkflowDocument>()
            .Property(x => x.Stages)
            .HasConversion(JsonConverter<List<WorkflowStageConfig>>());
        workflowStages.Metadata.SetValueComparer(JsonValueComparer<List<WorkflowStageConfig>>());

        var workflowStageResults = modelBuilder.Entity<WorkflowExecutionDocument>()
            .Property(x => x.StageResults)
            .HasConversion(JsonConverter<List<WorkflowStageResult>>());
        workflowStageResults.Metadata.SetValueComparer(JsonValueComparer<List<WorkflowStageResult>>());

        var harnessAdditionalSettings = modelBuilder.Entity<HarnessProviderSettingsDocument>()
            .Property(x => x.AdditionalSettings)
            .HasConversion(JsonConverter<Dictionary<string, string>>());
        harnessAdditionalSettings.Metadata.SetValueComparer(JsonValueComparer<Dictionary<string, string>>());
        modelBuilder.Entity<SystemSettingsDocument>()
            .Property(x => x.Orchestrator)
            .HasConversion(
                value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                value => string.IsNullOrWhiteSpace(value)
                    ? new OrchestratorSettings()
                    : JsonSerializer.Deserialize<OrchestratorSettings>(value, (JsonSerializerOptions?)null) ?? new OrchestratorSettings());

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
        var taskTemplateCommands = modelBuilder.Entity<TaskTemplateDocument>()
            .Property(x => x.Commands)
            .HasConversion(JsonConverter<List<string>>());
        taskTemplateCommands.Metadata.SetValueComparer(JsonValueComparer<List<string>>());

        var taskTemplateArtifactPatterns = modelBuilder.Entity<TaskTemplateDocument>()
            .Property(x => x.ArtifactPatterns)
            .HasConversion(JsonConverter<List<string>>());
        taskTemplateArtifactPatterns.Metadata.SetValueComparer(JsonValueComparer<List<string>>());

        var taskTemplateLinkedFailureRuns = modelBuilder.Entity<TaskTemplateDocument>()
            .Property(x => x.LinkedFailureRuns)
            .HasConversion(JsonConverter<List<string>>());
        taskTemplateLinkedFailureRuns.Metadata.SetValueComparer(JsonValueComparer<List<string>>());

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
        modelBuilder.Entity<OrchestratorLeaseDocument>()
            .HasIndex(x => x.ExpiresAtUtc);
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

        modelBuilder.Entity<TerminalSessionDocument>()
            .HasIndex(x => x.WorkerId);
        modelBuilder.Entity<TerminalSessionDocument>()
            .HasIndex(x => x.RunId);
        modelBuilder.Entity<TerminalSessionDocument>()
            .HasIndex(x => x.State);
        modelBuilder.Entity<TerminalSessionDocument>()
            .HasIndex(x => x.LastSeenAtUtc);
        modelBuilder.Entity<TerminalAuditEventDocument>()
            .HasIndex(x => new { x.SessionId, x.Sequence });
        modelBuilder.Entity<TerminalAuditEventDocument>()
            .HasIndex(x => x.TimestampUtc);

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

    private static ValueComparer<T> JsonValueComparer<T>()
    {
        return new ValueComparer<T>(
            (left, right) => AreEqualForComparison(left, right),
            value => GetComparisonHashCode(value),
            value => CloneForComparison(value));
    }

    private static string SerializeForComparison<T>(T value)
    {
        return ReferenceEquals(value, null)
            ? "null"
            : JsonSerializer.Serialize(value, (JsonSerializerOptions?)null);
    }

    private static bool AreEqualForComparison<T>(T left, T right)
    {
        return string.Equals(SerializeForComparison(left), SerializeForComparison(right), StringComparison.Ordinal);
    }

    private static int GetComparisonHashCode<T>(T value)
    {
        return SerializeForComparison(value).GetHashCode();
    }

    private static T CloneForComparison<T>(T value)
    {
        return ReferenceEquals(value, null)
            ? default!
            : JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!;
    }
}
