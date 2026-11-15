using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IMCAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddImpersonationSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImpersonationSessionId",
                table: "AuditLogs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ImpersonationSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ActorUserId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ActorEmail = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    TargetRole = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndedBy = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImpersonationSessions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ImpersonationSessions_ActorUserId",
                table: "ImpersonationSessions",
                column: "ActorUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ImpersonationSessions");

            migrationBuilder.DropColumn(
                name: "ImpersonationSessionId",
                table: "AuditLogs");
        }
    }
}
