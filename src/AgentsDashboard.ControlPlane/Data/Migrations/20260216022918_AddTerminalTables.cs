using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentsDashboard.ControlPlane.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTerminalTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TerminalAuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<string>(type: "TEXT", nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Direction = table.Column<int>(type: "INTEGER", nullable: false),
                    PayloadBase64 = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TerminalAuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TerminalSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    WorkerId = table.Column<string>(type: "TEXT", nullable: false),
                    RunId = table.Column<string>(type: "TEXT", nullable: true),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    Cols = table.Column<int>(type: "INTEGER", nullable: false),
                    Rows = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CloseReason = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TerminalSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TerminalAuditEvents_SessionId_Sequence",
                table: "TerminalAuditEvents",
                columns: new[] { "SessionId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_TerminalAuditEvents_TimestampUtc",
                table: "TerminalAuditEvents",
                column: "TimestampUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalSessions_LastSeenAtUtc",
                table: "TerminalSessions",
                column: "LastSeenAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalSessions_RunId",
                table: "TerminalSessions",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalSessions_State",
                table: "TerminalSessions",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_TerminalSessions_WorkerId",
                table: "TerminalSessions",
                column: "WorkerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TerminalAuditEvents");

            migrationBuilder.DropTable(
                name: "TerminalSessions");
        }
    }
}
