using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentsDashboard.ControlPlane.Data.Migrations
{
    /// <inheritdoc />
    public partial class _20260216_WorkspaceSemanticIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "RunAiSummaries");

            migrationBuilder.DropTable(
                name: "SemanticChunks");

            migrationBuilder.DropTable(
                name: "WorkspacePromptEntries");
        }
    }
}
