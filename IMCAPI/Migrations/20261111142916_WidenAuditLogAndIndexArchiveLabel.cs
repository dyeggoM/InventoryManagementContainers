using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IMCAPI.Migrations
{
    /// <inheritdoc />
    public partial class WidenAuditLogAndIndexArchiveLabel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // This migration also widens `AuditLogs.Action` (32 -> 64) and `EntityId` (512 -> 1024) in
            // the model. SQLite stores both as TEXT, so no column operation is generated here; the
            // model is what a SQL Server migration would read, and it now carries the larger widths.
            // That is the point of the change: on SQL Server a 33-character action would fail after a
            // migration instead of at the insert that wrote it.
            migrationBuilder.CreateIndex(
                name: "IX_ItemsArchive_Label",
                table: "ItemsArchive",
                column: "Label");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ItemsArchive_Label",
                table: "ItemsArchive");
        }
    }
}
