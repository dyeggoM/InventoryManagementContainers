using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IMCAPI.Migrations
{
    /// <inheritdoc />
    public partial class Organizations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Constants_Group_Label",
                table: "Constants");

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "RefreshTokens",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "ItemsArchive",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Items",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "ImpersonationSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Constants",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "AuditLogs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Attachments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_TenantId_UserId",
                table: "RefreshTokens",
                columns: new[] { "TenantId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemsArchive_TenantId_ItemId",
                table: "ItemsArchive",
                columns: new[] { "TenantId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_Items_TenantId_ParentId",
                table: "Items",
                columns: new[] { "TenantId", "ParentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ImpersonationSessions_TenantId_ActorUserId",
                table: "ImpersonationSessions",
                columns: new[] { "TenantId", "ActorUserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Constants_TenantId_Group_Label",
                table: "Constants",
                columns: new[] { "TenantId", "Group", "Label" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId_TimestampUtc",
                table: "AuditLogs",
                columns: new[] { "TenantId", "TimestampUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Attachments_TenantId_ItemId",
                table: "Attachments",
                columns: new[] { "TenantId", "ItemId" });

            migrationBuilder.Sql("""
                INSERT INTO Constants (Id, TenantId, "Group", Icon, Label, SortOrder, UpdatedBy, IsActive, UpdatedAt)
                SELECT org.Id, 0, 'Organization', '', org.Label, 0, 'system', 1, CURRENT_TIMESTAMP
                FROM (SELECT 900001 AS Id, 'Persona' AS Label UNION ALL
                      SELECT 900002, 'Live Room' UNION ALL SELECT 900003, 'Alquimia') org
                WHERE NOT EXISTS (SELECT 1 FROM Constants c WHERE c.Id = org.Id);
                UPDATE Items SET TenantId = 900001;
                UPDATE Attachments SET TenantId = 900001;
                UPDATE ItemsArchive SET TenantId = 900001;
                UPDATE AuditLogs SET TenantId = 900001;
                UPDATE ImpersonationSessions SET TenantId = 900001;
                UPDATE RefreshTokens SET TenantId = 900001;
                UPDATE Constants SET TenantId = 900001
                  WHERE "Group" NOT IN ('Organization', 'FieldType', 'AttachmentType');
                """);
            migrationBuilder.Sql(IMCAPI.DB.OrganizationConfiguration.CopySql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_TenantId_UserId",
                table: "RefreshTokens");

            migrationBuilder.DropIndex(
                name: "IX_ItemsArchive_TenantId_ItemId",
                table: "ItemsArchive");

            migrationBuilder.DropIndex(
                name: "IX_Items_TenantId_ParentId",
                table: "Items");

            migrationBuilder.DropIndex(
                name: "IX_ImpersonationSessions_TenantId_ActorUserId",
                table: "ImpersonationSessions");

            migrationBuilder.DropIndex(
                name: "IX_Constants_TenantId_Group_Label",
                table: "Constants");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_TenantId_TimestampUtc",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_Attachments_TenantId_ItemId",
                table: "Attachments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ItemsArchive");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ImpersonationSessions");

            // Up copied Persona's constants into Live Room and Alquimia and inserted the three
            // Organization rows. The index recreated below is unique on (Group, Label), so those
            // copies have to go before the TenantId column that tells them apart is dropped:
            // otherwise the rollback dies on a unique violation and leaves the schema half-migrated.
            // The copies are trees, and the self-reference is a Restrict foreign key, so the rows are
            // unparented first: deleting a parent before its child (or in the same statement) is a
            // "FOREIGN KEY constraint failed" of its own. Only the rows Up created are removed;
            // Persona's own rows stay, so nothing the user wrote is lost.
            migrationBuilder.Sql("""
                UPDATE Constants SET ParentId = NULL WHERE TenantId IN (900002, 900003);
                DELETE FROM Constants WHERE TenantId IN (900002, 900003);
                DELETE FROM Constants WHERE "Group" = 'Organization' AND Id IN (900001, 900002, 900003);
                """);

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Constants");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Attachments");

            migrationBuilder.CreateIndex(
                name: "IX_Constants_Group_Label",
                table: "Constants",
                columns: new[] { "Group", "Label" },
                unique: true);
        }
    }
}
