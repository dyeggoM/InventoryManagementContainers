using Microsoft.EntityFrameworkCore.Migrations;

namespace IMCAPI.Migrations;

public partial class ItemSetKinds : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Add vocabulary labels to seed.sql and to a migration that skips databases
        // without a catalog; startup seeding owns the initial catalog.
        // Part belongs to the equipment; an Accessory (strap, case, strings) does not.
        migrationBuilder.Sql("""
            INSERT INTO Constants (TenantId, "Group", Icon, Label, Description, SortOrder,
                                   ParentId, UpdatedBy, IsActive, UpdatedAt)
            SELECT org.Id, 'Type', '', kind.Label, '', 0,
                   root.Id, 'system', 1, CURRENT_TIMESTAMP
            FROM Constants org
            CROSS JOIN (SELECT 'Set' AS Label UNION ALL SELECT 'Accessory') kind
            JOIN Constants root ON root.TenantId = org.Id AND root."Group" = 'Type'
              AND root.Label = 'Type'
            WHERE org."Group" = 'Organization' AND org.TenantId = 0 AND org.IsActive = 1
              AND NOT EXISTS (
                  SELECT 1 FROM Constants existing
                  WHERE existing.TenantId = org.Id
                    AND existing."Group" = 'Type' AND existing.Label = kind.Label);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM Constants WHERE "Group" = 'Type' AND Label IN ('Set', 'Accessory')
              AND TenantId IN (SELECT Id FROM Constants WHERE "Group" = 'Organization' AND TenantId = 0);
            """);
    }
}
