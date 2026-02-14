using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentsDashboard.ControlPlane.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGraphWorkflowSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Agents",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Harness = table.Column<string>(type: "TEXT", nullable: false),
                    Prompt = table.Column<string>(type: "TEXT", nullable: false),
                    Command = table.Column<string>(type: "TEXT", nullable: false),
                    AutoCreatePullRequest = table.Column<bool>(type: "INTEGER", nullable: false),
                    RetryPolicy = table.Column<string>(type: "TEXT", nullable: false),
                    Timeouts = table.Column<string>(type: "TEXT", nullable: false),
                    SandboxProfile = table.Column<string>(type: "TEXT", nullable: false),
                    ArtifactPolicy = table.Column<string>(type: "TEXT", nullable: false),
                    ArtifactPatterns = table.Column<string>(type: "TEXT", nullable: false),
                    InstructionFiles = table.Column<string>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Agents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowsV2",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Nodes = table.Column<string>(type: "TEXT", nullable: false),
                    Edges = table.Column<string>(type: "TEXT", nullable: false),
                    Trigger = table.Column<string>(type: "TEXT", nullable: false),
                    WebhookToken = table.Column<string>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaxConcurrentNodes = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowsV2", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowExecutionsV2",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    WorkflowV2Id = table.Column<string>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    CurrentNodeId = table.Column<string>(type: "TEXT", nullable: false),
                    Context = table.Column<string>(type: "TEXT", nullable: false),
                    NodeResults = table.Column<string>(type: "TEXT", nullable: false),
                    PendingApprovalNodeId = table.Column<string>(type: "TEXT", nullable: false),
                    ApprovedBy = table.Column<string>(type: "TEXT", nullable: false),
                    FailureReason = table.Column<string>(type: "TEXT", nullable: false),
                    TriggeredBy = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowExecutionsV2", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowDeadLetters",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    ExecutionId = table.Column<string>(type: "TEXT", nullable: false),
                    WorkflowV2Id = table.Column<string>(type: "TEXT", nullable: false),
                    FailedNodeId = table.Column<string>(type: "TEXT", nullable: false),
                    FailedNodeName = table.Column<string>(type: "TEXT", nullable: false),
                    FailureReason = table.Column<string>(type: "TEXT", nullable: false),
                    InputContextSnapshot = table.Column<string>(type: "TEXT", nullable: false),
                    RunId = table.Column<string>(type: "TEXT", nullable: true),
                    Attempt = table.Column<int>(type: "INTEGER", nullable: false),
                    Replayed = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReplayedExecutionId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReplayedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowDeadLetters", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Agents_RepositoryId",
                table: "Agents",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Agents_RepositoryId_Name",
                table: "Agents",
                columns: new[] { "RepositoryId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowsV2_RepositoryId",
                table: "WorkflowsV2",
                column: "RepositoryId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowExecutionsV2_WorkflowV2Id_CreatedAtUtc",
                table: "WorkflowExecutionsV2",
                columns: new[] { "WorkflowV2Id", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowExecutionsV2_State",
                table: "WorkflowExecutionsV2",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDeadLetters_ExecutionId",
                table: "WorkflowDeadLetters",
                column: "ExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowDeadLetters_Replayed",
                table: "WorkflowDeadLetters",
                column: "Replayed");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "WorkflowDeadLetters");
            migrationBuilder.DropTable(name: "WorkflowExecutionsV2");
            migrationBuilder.DropTable(name: "WorkflowsV2");
            migrationBuilder.DropTable(name: "Agents");
        }
    }
}
