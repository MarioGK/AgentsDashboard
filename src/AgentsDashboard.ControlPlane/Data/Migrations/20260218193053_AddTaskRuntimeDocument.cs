using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentsDashboard.ControlPlane.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskRuntimeDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TaskRuntimes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    RepositoryId = table.Column<string>(type: "TEXT", nullable: false),
                    TaskId = table.Column<string>(type: "TEXT", nullable: false),
                    RuntimeId = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    ActiveRuns = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxParallelRuns = table.Column<int>(type: "INTEGER", nullable: false),
                    Endpoint = table.Column<string>(type: "TEXT", nullable: false),
                    ContainerId = table.Column<string>(type: "TEXT", nullable: false),
                    WorkspacePath = table.Column<string>(type: "TEXT", nullable: false),
                    RuntimeHomePath = table.Column<string>(type: "TEXT", nullable: false),
                    LastActivityUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    InactiveAfterUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskRuntimes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskRuntimes_RuntimeId",
                table: "TaskRuntimes",
                column: "RuntimeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TaskRuntimes_State_InactiveAfterUtc",
                table: "TaskRuntimes",
                columns: new[] { "State", "InactiveAfterUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_TaskRuntimes_TaskId",
                table: "TaskRuntimes",
                column: "TaskId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TaskRuntimes");
        }
    }
}
