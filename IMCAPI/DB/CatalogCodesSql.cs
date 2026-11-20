namespace IMCAPI.DB;

/// <summary>
/// The code/label contract, applied to catalog data: <c>Code</c> is the stable identity
/// (today's former label, never translated), <c>Label</c> is the display text (Spanish).
/// One script serves both paths so they cannot diverge: <see cref="DatabaseSeeder.SeedConfiguration"/>
/// runs it on a fresh database right after <c>seed.sql</c>, and the <c>ConstantCodes</c> migration
/// runs it on a database that was seeded before the column existed — with <c>seed.sql</c> still
/// writing the former labels and no <c>Code</c>, both routes land on the same rows.
/// </summary>
public static class CatalogCodesSql
{
    /// <summary>
    /// Statements to run in order. Keep them together: a database that got only one of the two
    /// steps would carry Spanish labels with Spanish codes, which is exactly the identity bug
    /// this contract removes.
    /// </summary>
    public const string BackfillAndTranslate = $$"""
        -- 1. Backfill: the label a row was seeded with is its identity. Codes are never translated.
        UPDATE Constants SET "Code" = "Label" WHERE "Code" = '' OR "Code" IS NULL;

        -- 2. Display labels to Spanish, keyed by code. Codes stay exactly as they are.
        UPDATE Constants SET "Label" = CASE "Code"
            -- Attribute definitions and the group roots that share their name
            WHEN 'Brand' THEN 'Marca'
            WHEN 'BuyPrice' THEN 'Precio de compra'
            WHEN 'RentPrice' THEN 'Precio de alquiler'
            WHEN 'Category' THEN 'Categoría'
            WHEN 'Classification' THEN 'Clasificación'
            WHEN 'Description' THEN 'Descripción'
            WHEN 'Notes' THEN 'Notas'
            WHEN 'Type' THEN 'Tipo'
            WHEN 'Attribute' THEN 'Atributo'
            -- Type values
            WHEN 'Instrument' THEN 'Instrumento'
            WHEN 'Component' THEN 'Componente'
            WHEN 'Part' THEN 'Pieza'
            WHEN 'SubPart' THEN 'Subpieza'
            WHEN 'Set' THEN 'Conjunto'
            WHEN 'Accessory' THEN 'Accesorio'
            -- Category values (the level-1 categories)
            WHEN 'Hardware' THEN 'Herrajes'
            WHEN 'Toys' THEN 'Juguetes'
            WHEN 'Percusion' THEN 'Percusión'
            WHEN 'Cymbals' THEN 'Platillos'
            WHEN 'Drums' THEN 'Baterías'
            WHEN 'Instruments' THEN 'Instrumentos'
            WHEN 'Amplifiers' THEN 'Amplificadores'
            WHEN 'Other' THEN 'Otros'
            WHEN 'Cases' THEN 'Estuches'
            WHEN 'Vehiculos' THEN 'Vehículos'
            -- Classification values
            WHEN 'Articulos' THEN 'Artículos'
            ELSE "Label"
        END
        WHERE "Code" IN ('Brand', 'BuyPrice', 'RentPrice', 'Category', 'Classification', 'Description',
            'Notes', 'Type', 'Attribute', 'Instrument', 'Component', 'Part', 'SubPart', 'Set',
            'Accessory', 'Hardware', 'Toys', 'Percusion', 'Cymbals', 'Drums', 'Instruments',
            'Amplifiers', 'Other', 'Cases', 'Vehiculos', 'Articulos');
        """;
}
