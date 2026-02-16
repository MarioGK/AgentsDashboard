using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentsDashboard.ControlPlane.Data.Migrations
{
    /// <inheritdoc />
    public partial class _20260216_RepoFirstGitWorkspace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_Runs_ProjectId_State",
                table: "Runs");

            migrationBuilder.DropIndex(
                name: "IX_Repositories_ProjectId",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "WorkflowExecutions");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "Runs");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "ProxyAudits");

            migrationBuilder.RenameColumn(
                name: "ProjectId",
                table: "Repositories",
                newName: "LocalPath");

            migrationBuilder.AddColumn<int>(
                name: "AheadCount",
                table: "Repositories",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "BehindCount",
                table: "Repositories",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CurrentBranch",
                table: "Repositories",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "CurrentCommit",
                table: "Repositories",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastCloneAtUtc",
                table: "Repositories",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastFetchedAtUtc",
                table: "Repositories",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastScannedAtUtc",
                table: "Repositories",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastSyncError",
                table: "Repositories",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastViewedAtUtc",
                table: "Repositories",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ModifiedCount",
                table: "Repositories",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StagedCount",
                table: "Repositories",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UntrackedCount",
                table: "Repositories",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_LastViewedAtUtc",
                table: "Repositories",
                column: "LastViewedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_Name",
                table: "Repositories",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Repositories_LastViewedAtUtc",
                table: "Repositories");

            migrationBuilder.DropIndex(
                name: "IX_Repositories_Name",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "AheadCount",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "BehindCount",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "CurrentBranch",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "CurrentCommit",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "LastCloneAtUtc",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "LastFetchedAtUtc",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "LastScannedAtUtc",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "LastSyncError",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "LastViewedAtUtc",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "ModifiedCount",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "StagedCount",
                table: "Repositories");

            migrationBuilder.DropColumn(
                name: "UntrackedCount",
                table: "Repositories");

            migrationBuilder.RenameColumn(
                name: "LocalPath",
                table: "Repositories",
                newName: "ProjectId");

            migrationBuilder.AddColumn<string>(
                name: "ProjectId",
                table: "WorkflowExecutions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProjectId",
                table: "Runs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ProjectId",
                table: "ProxyAudits",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Runs_ProjectId_State",
                table: "Runs",
                columns: new[] { "ProjectId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_Repositories_ProjectId",
                table: "Repositories",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Name",
                table: "Projects",
                column: "Name");
        }
    }
}
