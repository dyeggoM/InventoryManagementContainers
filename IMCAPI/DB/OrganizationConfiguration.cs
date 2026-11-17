namespace IMCAPI.DB;

/// <summary>Copies Persona's configuration with a complete id map before fixing parent links.</summary>
public static class OrganizationConfiguration
{
    public const int DefaultTenantId = 900001;
    public static readonly string CopySql = $$"""
        CREATE TEMP TABLE org_copy_map (source_id INTEGER NOT NULL, target_tenant INTEGER NOT NULL, target_id INTEGER NOT NULL);
        INSERT INTO org_copy_map (source_id, target_tenant, target_id)
        SELECT c.Id, t.Id, (SELECT COALESCE(MAX(Id), 0) FROM Constants)
            + ROW_NUMBER() OVER (ORDER BY t.Id, c.Id)
        FROM Constants c CROSS JOIN Constants t
        WHERE c.TenantId = {{DefaultTenantId}} AND c."Group" NOT IN ('Organization', 'FieldType', 'AttachmentType')
          AND t."Group" = 'Organization' AND t.Id IN (900002, 900003) AND t.IsActive = 1;
        INSERT INTO Constants (Id, TenantId, "Group", Label, Icon, Description, DataType, SortOrder,
            ParentId, IsActive, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy)
        SELECT m.target_id, m.target_tenant, c."Group", c.Label, c.Icon, c.Description, c.DataType,
            c.SortOrder, NULL, c.IsActive, c.CreatedAt, c.CreatedBy, c.UpdatedAt, c.UpdatedBy
        FROM org_copy_map m JOIN Constants c ON c.Id = m.source_id;
        UPDATE Constants SET ParentId = (
            SELECT parent.target_id FROM org_copy_map child
            JOIN Constants original ON original.Id = child.source_id
            JOIN org_copy_map parent ON parent.source_id = original.ParentId
                AND parent.target_tenant = child.target_tenant
            WHERE child.target_id = Constants.Id)
        WHERE Id IN (SELECT target_id FROM org_copy_map)
            AND (SELECT ParentId FROM Constants original WHERE original.Id =
                (SELECT source_id FROM org_copy_map WHERE target_id = Constants.Id)) IS NOT NULL;
        DROP TABLE org_copy_map;
        """;
}
