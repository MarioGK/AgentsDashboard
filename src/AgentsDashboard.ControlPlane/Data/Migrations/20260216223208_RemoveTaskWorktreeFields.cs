using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentsDashboard.ControlPlane.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTaskWorktreeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tasks_RepositoryId_WorktreeBranch",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_RepositoryId_WorktreePath",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "WorktreeBranch",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "WorktreePath",
                table: "Tasks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WorktreeBranch",
                table: "Tasks",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "WorktreePath",
                table: "Tasks",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_RepositoryId_WorktreeBranch",
                table: "Tasks",
                columns: new[] { "RepositoryId", "WorktreeBranch" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_RepositoryId_WorktreePath",
                table: "Tasks",
                columns: new[] { "RepositoryId", "WorktreePath" });
        }
    }
}
