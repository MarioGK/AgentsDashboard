using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentsDashboard.ControlPlane.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkspacePromptEntryImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasImages",
                table: "WorkspacePromptEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ImageMetadataJson",
                table: "WorkspacePromptEntries",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_WorkspacePromptEntries_TaskId_HasImages_CreatedAtUtc",
                table: "WorkspacePromptEntries",
                columns: new[] { "TaskId", "HasImages", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkspacePromptEntries_TaskId_HasImages_CreatedAtUtc",
                table: "WorkspacePromptEntries");

            migrationBuilder.DropColumn(
                name: "HasImages",
                table: "WorkspacePromptEntries");

            migrationBuilder.DropColumn(
                name: "ImageMetadataJson",
                table: "WorkspacePromptEntries");
        }
    }
}
