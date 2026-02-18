using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentsDashboard.ControlPlane.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateModelForPendingChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SessionProfileId",
                table: "Tasks",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "AutomationRunId",
                table: "Runs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "InstructionStackHash",
                table: "Runs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "McpConfigSnapshotJson",
                table: "Runs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SessionProfileId",
                table: "Runs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "AutomationDefinitions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<string>(type: "TEXT", nullable: false),
                    TaskId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    CronExpression = table.Column<string>(type: "TEXT", nullable: false),
                    TriggerKind = table.Column<string>(type: "TEXT", nullable: false),
                    ReplayPolicy = table.Column<string>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    NextRunAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AutomationExecutions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    AutomationDefinitionId = table.Column<string>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<string>(type: "TEXT", nullable: false),
                    TaskId = table.Column<string>(type: "TEXT", nullable: false),
                    RunId = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    TriggeredBy = table.Column<string>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AutomationExecutions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RunInstructionStacks",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    RunId = table.Column<string>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<string>(type: "TEXT", nullable: false),
                    TaskId = table.Column<string>(type: "TEXT", nullable: false),
                    SessionProfileId = table.Column<string>(type: "TEXT", nullable: false),
                    GlobalRules = table.Column<string>(type: "TEXT", nullable: false),
                    RepositoryRules = table.Column<string>(type: "TEXT", nullable: false),
                    TaskRules = table.Column<string>(type: "TEXT", nullable: false),
                    RunOverrides = table.Column<string>(type: "TEXT", nullable: false),
                    ResolvedText = table.Column<string>(type: "TEXT", nullable: false),
                    Hash = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunInstructionStacks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RunSessionProfiles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Scope = table.Column<int>(type: "INTEGER", nullable: false),
                    RepositoryId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Harness = table.Column<string>(type: "TEXT", nullable: false),
                    ExecutionModeDefault = table.Column<int>(type: "INTEGER", nullable: false),
                    ApprovalMode = table.Column<string>(type: "TEXT", nullable: false),
                    DiffViewDefault = table.Column<string>(type: "TEXT", nullable: false),
                    ToolTimelineMode = table.Column<string>(type: "TEXT", nullable: false),
                    McpConfigJson = table.Column<string>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunSessionProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RunShareBundles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    RunId = table.Column<string>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<string>(type: "TEXT", nullable: false),
                    TaskId = table.Column<string>(type: "TEXT", nullable: false),
                    BundleJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunShareBundles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationDefinitions_RepositoryId_Enabled_NextRunAtUtc",
                table: "AutomationDefinitions",
                columns: new[] { "RepositoryId", "Enabled", "NextRunAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationExecutions_AutomationDefinitionId_StartedAtUtc",
                table: "AutomationExecutions",
                columns: new[] { "AutomationDefinitionId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AutomationExecutions_RepositoryId_TaskId_StartedAtUtc",
                table: "AutomationExecutions",
                columns: new[] { "RepositoryId", "TaskId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RunInstructionStacks_RepositoryId_TaskId_CreatedAtUtc",
                table: "RunInstructionStacks",
                columns: new[] { "RepositoryId", "TaskId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RunInstructionStacks_RunId_Hash",
                table: "RunInstructionStacks",
                columns: new[] { "RunId", "Hash" });

            migrationBuilder.CreateIndex(
                name: "IX_RunSessionProfiles_RepositoryId_Name",
                table: "RunSessionProfiles",
                columns: new[] { "RepositoryId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_RunSessionProfiles_Scope_Enabled",
                table: "RunSessionProfiles",
                columns: new[] { "Scope", "Enabled" });

            migrationBuilder.CreateIndex(
                name: "IX_RunShareBundles_RepositoryId_TaskId_CreatedAtUtc",
                table: "RunShareBundles",
                columns: new[] { "RepositoryId", "TaskId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RunShareBundles_RunId",
                table: "RunShareBundles",
                column: "RunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AutomationDefinitions");

            migrationBuilder.DropTable(
                name: "AutomationExecutions");

            migrationBuilder.DropTable(
                name: "RunInstructionStacks");

            migrationBuilder.DropTable(
                name: "RunSessionProfiles");

            migrationBuilder.DropTable(
                name: "RunShareBundles");

            migrationBuilder.DropColumn(
                name: "SessionProfileId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "AutomationRunId",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "InstructionStackHash",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "McpConfigSnapshotJson",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "SessionProfileId",
                table: "Runs");
        }
    }
}
