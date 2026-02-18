using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentsDashboard.ControlPlane.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskRuntimeTelemetryAndRegistrationRename : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "Workers",
                newName: "TaskRuntimeRegistrations");

            migrationBuilder.RenameColumn(
                name: "TaskRuntimeId",
                table: "TaskRuntimeRegistrations",
                newName: "RuntimeId");

            migrationBuilder.RenameIndex(
                name: "IX_Workers_TaskRuntimeId",
                table: "TaskRuntimeRegistrations",
                newName: "IX_TaskRuntimeRegistrations_RuntimeId");

            migrationBuilder.AddColumn<long>(
                name: "ColdStartCount",
                table: "TaskRuntimes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "ColdStartDurationTotalMs",
                table: "TaskRuntimes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "InactiveDurationTotalMs",
                table: "TaskRuntimes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "InactiveTransitionCount",
                table: "TaskRuntimes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastBecameInactiveUtc",
                table: "TaskRuntimes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastColdStartDurationMs",
                table: "TaskRuntimes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "LastInactiveDurationMs",
                table: "TaskRuntimes",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastReadyAtUtc",
                table: "TaskRuntimes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastStartedAtUtc",
                table: "TaskRuntimes",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastStateChangeUtc",
                table: "TaskRuntimes",
                type: "TEXT",
                nullable: true);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ColdStartCount",
                table: "TaskRuntimes");

            migrationBuilder.DropColumn(
                name: "ColdStartDurationTotalMs",
                table: "TaskRuntimes");

            migrationBuilder.DropColumn(
                name: "InactiveDurationTotalMs",
                table: "TaskRuntimes");

            migrationBuilder.DropColumn(
                name: "InactiveTransitionCount",
                table: "TaskRuntimes");

            migrationBuilder.DropColumn(
                name: "LastBecameInactiveUtc",
                table: "TaskRuntimes");

            migrationBuilder.DropColumn(
                name: "LastColdStartDurationMs",
                table: "TaskRuntimes");

            migrationBuilder.DropColumn(
                name: "LastInactiveDurationMs",
                table: "TaskRuntimes");

            migrationBuilder.DropColumn(
                name: "LastReadyAtUtc",
                table: "TaskRuntimes");

            migrationBuilder.DropColumn(
                name: "LastStartedAtUtc",
                table: "TaskRuntimes");

            migrationBuilder.DropColumn(
                name: "LastStateChangeUtc",
                table: "TaskRuntimes");

            migrationBuilder.RenameIndex(
                name: "IX_TaskRuntimeRegistrations_RuntimeId",
                table: "TaskRuntimeRegistrations",
                newName: "IX_Workers_TaskRuntimeId");

            migrationBuilder.RenameColumn(
                name: "RuntimeId",
                table: "TaskRuntimeRegistrations",
                newName: "TaskRuntimeId");

            migrationBuilder.RenameTable(
                name: "TaskRuntimeRegistrations",
                newName: "Workers");
        }
    }
}
