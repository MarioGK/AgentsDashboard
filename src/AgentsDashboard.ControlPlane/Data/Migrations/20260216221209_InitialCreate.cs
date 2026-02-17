using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentsDashboard.ControlPlane.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AlertEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    RuleId = table.Column<string>(type: "TEXT", nullable: false),
                    RuleName = table.Column<string>(type: "TEXT", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    FiredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Resolved = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AlertRules",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    RuleType = table.Column<int>(type: "INTEGER", nullable: false),
                    Threshold = table.Column<int>(type: "INTEGER", nullable: false),
                    WindowMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    CooldownMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    WebhookUrl = table.Column<string>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastFiredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AlertRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Findings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<string>(type: "TEXT", nullable: false),
                    RunId = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Severity = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    AssignedTo = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Findings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HarnessProviderSettings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<string>(type: "TEXT", nullable: false),
                    Harness = table.Column<string>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: false),
                    Temperature = table.Column<double>(type: "REAL", nullable: false),
                    MaxTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    AdditionalSettings = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HarnessProviderSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Leases",
                columns: table => new
                {
                    LeaseName = table.Column<string>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leases", x => x.LeaseName);
                });

            migrationBuilder.CreateTable(
                name: "PromptSkills",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Trigger = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromptSkills", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProviderSecrets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<string>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", nullable: false),
                    EncryptedValue = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderSecrets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProxyAudits",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    RepoId = table.Column<string>(type: "TEXT", nullable: false),
                    TaskId = table.Column<string>(type: "TEXT", nullable: false),
                    RunId = table.Column<string>(type: "TEXT", nullable: false),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    UpstreamTarget = table.Column<string>(type: "TEXT", nullable: false),
                    StatusCode = table.Column<int>(type: "INTEGER", nullable: false),
                    LatencyMs = table.Column<double>(type: "REAL", nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProxyAudits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Repositories",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    GitUrl = table.Column<string>(type: "TEXT", nullable: false),
                    LocalPath = table.Column<string>(type: "TEXT", nullable: false),
                    DefaultBranch = table.Column<string>(type: "TEXT", nullable: false),
                    CurrentBranch = table.Column<string>(type: "TEXT", nullable: false),
                    CurrentCommit = table.Column<string>(type: "TEXT", nullable: false),
                    AheadCount = table.Column<int>(type: "INTEGER", nullable: false),
                    BehindCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ModifiedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    StagedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UntrackedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastScannedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastFetchedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastCloneAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastViewedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastSyncError = table.Column<string>(type: "TEXT", nullable: false),
                    InstructionFiles = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Repositories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RepositoryInstructions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RepositoryInstructions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RunAiSummaries",
                columns: table => new
                {
                    RunId = table.Column<string>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<string>(type: "TEXT", nullable: false),
                    TaskId = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: false),
                    SourceFingerprint = table.Column<string>(type: "TEXT", nullable: false),
                    SourceUpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunAiSummaries", x => x.RunId);
                });

            migrationBuilder.CreateTable(
                name: "RunEvents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    RunId = table.Column<string>(type: "TEXT", nullable: false),
                    Level = table.Column<string>(type: "TEXT", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Runs",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<string>(type: "TEXT", nullable: false),
                    TaskId = table.Column<string>(type: "TEXT", nullable: false),
                    WorkerId = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    OutputJson = table.Column<string>(type: "TEXT", nullable: false),
                    Attempt = table.Column<int>(type: "INTEGER", nullable: false),
                    ResultEnvelopeRef = table.Column<string>(type: "TEXT", nullable: false),
                    FailureClass = table.Column<string>(type: "TEXT", nullable: false),
                    PrUrl = table.Column<string>(type: "TEXT", nullable: false),
                    WorkerImageRef = table.Column<string>(type: "TEXT", nullable: false),
                    WorkerImageDigest = table.Column<string>(type: "TEXT", nullable: false),
                    WorkerImageSource = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Runs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SemanticChunks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<string>(type: "TEXT", nullable: false),
                    TaskId = table.Column<string>(type: "TEXT", nullable: false),
                    RunId = table.Column<string>(type: "TEXT", nullable: false),
                    ChunkKey = table.Column<string>(type: "TEXT", nullable: false),
                    SourceType = table.Column<string>(type: "TEXT", nullable: false),
                    SourceRef = table.Column<string>(type: "TEXT", nullable: false),
                    ChunkIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", nullable: false),
                    TokenCount = table.Column<int>(type: "INTEGER", nullable: false),
                    EmbeddingModel = table.Column<string>(type: "TEXT", nullable: false),
                    EmbeddingDimensions = table.Column<int>(type: "INTEGER", nullable: false),
                    EmbeddingPayload = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SemanticChunks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    DockerAllowedImages = table.Column<string>(type: "TEXT", nullable: false),
                    RetentionDaysLogs = table.Column<int>(type: "INTEGER", nullable: false),
                    RetentionDaysRuns = table.Column<int>(type: "INTEGER", nullable: false),
                    VictoriaMetricsEndpoint = table.Column<string>(type: "TEXT", nullable: false),
                    VmUiEndpoint = table.Column<string>(type: "TEXT", nullable: false),
                    Orchestrator = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tasks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Harness = table.Column<string>(type: "TEXT", nullable: false),
                    Prompt = table.Column<string>(type: "TEXT", nullable: false),
                    Command = table.Column<string>(type: "TEXT", nullable: false),
                    AutoCreatePullRequest = table.Column<bool>(type: "INTEGER", nullable: false),
                    CronExpression = table.Column<string>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    NextRunAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    WorktreePath = table.Column<string>(type: "TEXT", nullable: false),
                    WorktreeBranch = table.Column<string>(type: "TEXT", nullable: false),
                    LastGitSyncAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastGitSyncError = table.Column<string>(type: "TEXT", nullable: false),
                    RetryPolicy = table.Column<string>(type: "TEXT", nullable: false),
                    Timeouts = table.Column<string>(type: "TEXT", nullable: false),
                    ApprovalProfile = table.Column<string>(type: "TEXT", nullable: false),
                    SandboxProfile = table.Column<string>(type: "TEXT", nullable: false),
                    ArtifactPolicy = table.Column<string>(type: "TEXT", nullable: false),
                    ArtifactPatterns = table.Column<string>(type: "TEXT", nullable: false),
                    LinkedFailureRuns = table.Column<string>(type: "TEXT", nullable: false),
                    ConcurrencyLimit = table.Column<int>(type: "INTEGER", nullable: false),
                    InstructionFiles = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tasks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TaskTemplates",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    TemplateId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Harness = table.Column<string>(type: "TEXT", nullable: false),
                    Prompt = table.Column<string>(type: "TEXT", nullable: false),
                    Commands = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    AutoCreatePullRequest = table.Column<bool>(type: "INTEGER", nullable: false),
                    CronExpression = table.Column<string>(type: "TEXT", nullable: false),
                    RetryPolicy = table.Column<string>(type: "TEXT", nullable: false),
                    Timeouts = table.Column<string>(type: "TEXT", nullable: false),
                    SandboxProfile = table.Column<string>(type: "TEXT", nullable: false),
                    ArtifactPolicy = table.Column<string>(type: "TEXT", nullable: false),
                    ArtifactPatterns = table.Column<string>(type: "TEXT", nullable: false),
                    LinkedFailureRuns = table.Column<string>(type: "TEXT", nullable: false),
                    IsBuiltIn = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsEditable = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Webhooks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<string>(type: "TEXT", nullable: false),
                    TaskId = table.Column<string>(type: "TEXT", nullable: false),
                    EventFilter = table.Column<string>(type: "TEXT", nullable: false),
                    Secret = table.Column<string>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Webhooks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Workers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    WorkerId = table.Column<string>(type: "TEXT", nullable: false),
                    Endpoint = table.Column<string>(type: "TEXT", nullable: false),
                    MaxSlots = table.Column<int>(type: "INTEGER", nullable: false),
                    ActiveSlots = table.Column<int>(type: "INTEGER", nullable: false),
                    Online = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastHeartbeatUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RegisteredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowExecutions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    WorkflowId = table.Column<string>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentStageIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    StageResults = table.Column<string>(type: "TEXT", nullable: false),
                    PendingApprovalStageId = table.Column<string>(type: "TEXT", nullable: false),
                    ApprovedBy = table.Column<string>(type: "TEXT", nullable: false),
                    FailureReason = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowExecutions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Workflows",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Stages = table.Column<string>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workflows", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkspacePromptEntries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<string>(type: "TEXT", nullable: false),
                    TaskId = table.Column<string>(type: "TEXT", nullable: false),
                    RunId = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspacePromptEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AlertEvents_FiredAtUtc",
                table: "AlertEvents",
                column: "FiredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AlertEvents_RuleId",
                table: "AlertEvents",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "IX_AlertRules_Enabled",
                table: "AlertRules",
                column: "Enabled");

            migrationBuilder.CreateIndex(
                name: "IX_Findings_RepositoryId_CreatedAtUtc",
                table: "Findings",
                columns: new[] { "RepositoryId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Findings_State",
                table: "Findings",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_HarnessProviderSettings_RepositoryId_Harness",
                table: "HarnessProviderSettings",
                columns: new[] { "RepositoryId", "Harness" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Leases_ExpiresAtUtc",
                table: "Leases",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PromptSkills_RepositoryId_Trigger",
                table: "PromptSkills",
                columns: new[] { "RepositoryId", "Trigger" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromptSkills_RepositoryId_UpdatedAtUtc",
                table: "PromptSkills",
                columns: new[] { "RepositoryId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProviderSecrets_RepositoryId_Provider",
                table: "ProviderSecrets",
                columns: new[] { "RepositoryId", "Provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProxyAudits_RunId_TimestampUtc",
                table: "ProxyAudits",
                columns: new[] { "RunId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_LastViewedAtUtc",
                table: "Repositories",
                column: "LastViewedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_Name",
                table: "Repositories",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryInstructions_RepositoryId",
                table: "RepositoryInstructions",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_RepositoryInstructions_RepositoryId_Priority",
                table: "RepositoryInstructions",
                columns: new[] { "RepositoryId", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_RunAiSummaries_ExpiresAtUtc",
                table: "RunAiSummaries",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RunAiSummaries_GeneratedAtUtc",
                table: "RunAiSummaries",
                column: "GeneratedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_RunAiSummaries_RepositoryId_TaskId",
                table: "RunAiSummaries",
                columns: new[] { "RepositoryId", "TaskId" });

            migrationBuilder.CreateIndex(
                name: "IX_RunEvents_RunId_TimestampUtc",
                table: "RunEvents",
                columns: new[] { "RunId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Runs_RepositoryId_CreatedAtUtc",
                table: "Runs",
                columns: new[] { "RepositoryId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Runs_State",
                table: "Runs",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_Runs_TaskId_CreatedAtUtc",
                table: "Runs",
                columns: new[] { "TaskId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Runs_TaskId_State",
                table: "Runs",
                columns: new[] { "TaskId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_SemanticChunks_RepositoryId_RunId",
                table: "SemanticChunks",
                columns: new[] { "RepositoryId", "RunId" });

            migrationBuilder.CreateIndex(
                name: "IX_SemanticChunks_TaskId_ChunkKey",
                table: "SemanticChunks",
                columns: new[] { "TaskId", "ChunkKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SemanticChunks_TaskId_UpdatedAtUtc",
                table: "SemanticChunks",
                columns: new[] { "TaskId", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_LastGitSyncAtUtc",
                table: "Tasks",
                column: "LastGitSyncAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_NextRunAtUtc",
                table: "Tasks",
                column: "NextRunAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_RepositoryId",
                table: "Tasks",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_RepositoryId_WorktreeBranch",
                table: "Tasks",
                columns: new[] { "RepositoryId", "WorktreeBranch" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_RepositoryId_WorktreePath",
                table: "Tasks",
                columns: new[] { "RepositoryId", "WorktreePath" });

            migrationBuilder.CreateIndex(
                name: "IX_Webhooks_RepositoryId",
                table: "Webhooks",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Workers_WorkerId",
                table: "Workers",
                column: "WorkerId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowExecutions_State",
                table: "WorkflowExecutions",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowExecutions_WorkflowId_CreatedAtUtc",
                table: "WorkflowExecutions",
                columns: new[] { "WorkflowId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Workflows_RepositoryId",
                table: "Workflows",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspacePromptEntries_RunId_CreatedAtUtc",
                table: "WorkspacePromptEntries",
                columns: new[] { "RunId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspacePromptEntries_TaskId_CreatedAtUtc",
                table: "WorkspacePromptEntries",
                columns: new[] { "TaskId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AlertEvents");

            migrationBuilder.DropTable(
                name: "AlertRules");

            migrationBuilder.DropTable(
                name: "Findings");

            migrationBuilder.DropTable(
                name: "HarnessProviderSettings");

            migrationBuilder.DropTable(
                name: "Leases");

            migrationBuilder.DropTable(
                name: "PromptSkills");

            migrationBuilder.DropTable(
                name: "ProviderSecrets");

            migrationBuilder.DropTable(
                name: "ProxyAudits");

            migrationBuilder.DropTable(
                name: "Repositories");

            migrationBuilder.DropTable(
                name: "RepositoryInstructions");

            migrationBuilder.DropTable(
                name: "RunAiSummaries");

            migrationBuilder.DropTable(
                name: "RunEvents");

            migrationBuilder.DropTable(
                name: "Runs");

            migrationBuilder.DropTable(
                name: "SemanticChunks");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "Tasks");

            migrationBuilder.DropTable(
                name: "TaskTemplates");

            migrationBuilder.DropTable(
                name: "Webhooks");

            migrationBuilder.DropTable(
                name: "Workers");

            migrationBuilder.DropTable(
                name: "WorkflowExecutions");

            migrationBuilder.DropTable(
                name: "Workflows");

            migrationBuilder.DropTable(
                name: "WorkspacePromptEntries");
        }
    }
}
