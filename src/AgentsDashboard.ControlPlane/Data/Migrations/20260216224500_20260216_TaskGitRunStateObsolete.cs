using System;
using AgentsDashboard.ControlPlane.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentsDashboard.ControlPlane.Data.Migrations
{
    [DbContext(typeof(OrchestratorDbContext))]
    [Migration("20260216224500_20260216_TaskGitRunStateObsolete")]
    public partial class _20260216_TaskGitRunStateObsolete : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastGitSyncAtUtc",
                table: "Tasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastGitSyncError",
                table: "Tasks",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

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
                name: "IX_Runs_TaskId_CreatedAtUtc",
                table: "Runs",
                columns: new[] { "TaskId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_LastGitSyncAtUtc",
                table: "Tasks",
                column: "LastGitSyncAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_RepositoryId_WorktreeBranch",
                table: "Tasks",
                columns: new[] { "RepositoryId", "WorktreeBranch" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_RepositoryId_WorktreePath",
                table: "Tasks",
                columns: new[] { "RepositoryId", "WorktreePath" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Runs_TaskId_CreatedAtUtc",
                table: "Runs");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_LastGitSyncAtUtc",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_RepositoryId_WorktreeBranch",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_RepositoryId_WorktreePath",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "LastGitSyncAtUtc",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "LastGitSyncError",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "WorktreeBranch",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "WorktreePath",
                table: "Tasks");
        }
    }
}
