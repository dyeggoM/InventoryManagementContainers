using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IMCAPI.Migrations
{
    /// <inheritdoc />
    public partial class ConstantCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "Constants",
                type: "TEXT",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            // The identity backfill and the Spanish labels come from the one script
            // DatabaseSeeder.SeedConfiguration runs on a fresh database, so a database seeded
            // before this column existed ends up identical to a freshly seeded one.
            migrationBuilder.Sql(IMCAPI.DB.CatalogCodesSql.BackfillAndTranslate);

            migrationBuilder.CreateIndex(
                name: "IX_Constants_TenantId_Group_Code",
                table: "Constants",
                columns: new[] { "TenantId", "Group", "Code" },
                unique: true,
                filter: "\"Code\" != ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Constants_TenantId_Group_Code",
                table: "Constants");

            // The inverse of the translation, keyed on label: rewinding to a pre-code schema has to
            // leave the labels the earlier migrations know (ItemSetKinds.Down deletes its rows by
            // label), otherwise a rollback of a translated database leaves rows behind.
            migrationBuilder.Sql("""
                UPDATE Constants SET "Label" = CASE "Label"
                    WHEN 'Marca' THEN 'Brand'
                    WHEN 'Precio de compra' THEN 'BuyPrice'
                    WHEN 'Precio de alquiler' THEN 'RentPrice'
                    WHEN 'Categoría' THEN 'Category'
                    WHEN 'Clasificación' THEN 'Classification'
                    WHEN 'Descripción' THEN 'Description'
                    WHEN 'Notas' THEN 'Notes'
                    WHEN 'Tipo' THEN 'Type'
                    WHEN 'Atributo' THEN 'Attribute'
                    WHEN 'Instrumento' THEN 'Instrument'
                    WHEN 'Componente' THEN 'Component'
                    WHEN 'Pieza' THEN 'Part'
                    WHEN 'Subpieza' THEN 'SubPart'
                    WHEN 'Conjunto' THEN 'Set'
                    WHEN 'Accesorio' THEN 'Accessory'
                    WHEN 'Herrajes' THEN 'Hardware'
                    WHEN 'Juguetes' THEN 'Toys'
                    WHEN 'Percusión' THEN 'Percusion'
                    WHEN 'Platillos' THEN 'Cymbals'
                    WHEN 'Baterías' THEN 'Drums'
                    WHEN 'Instrumentos' THEN 'Instruments'
                    WHEN 'Amplificadores' THEN 'Amplifiers'
                    WHEN 'Otros' THEN 'Other'
                    WHEN 'Estuches' THEN 'Cases'
                    WHEN 'Vehículos' THEN 'Vehiculos'
                    WHEN 'Artículos' THEN 'Articulos'
                    ELSE "Label"
                END
                WHERE "Label" IN ('Marca', 'Precio de compra', 'Precio de alquiler', 'Categoría',
                    'Clasificación', 'Descripción', 'Notas', 'Tipo', 'Atributo', 'Instrumento',
                    'Componente', 'Pieza', 'Subpieza', 'Conjunto', 'Accesorio', 'Herrajes',
                    'Juguetes', 'Percusión', 'Platillos', 'Baterías', 'Instrumentos',
                    'Amplificadores', 'Otros', 'Estuches', 'Vehículos', 'Artículos');
                """);

            migrationBuilder.DropColumn(
                name: "Code",
                table: "Constants");
        }
    }
}
