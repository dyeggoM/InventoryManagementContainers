using System.Text.Json;
using System.Text.RegularExpressions;
using ExcelDataReader;
using IMCAPI.DB;
using Microsoft.EntityFrameworkCore;

namespace IMCAPI.Services;

/// <summary>
/// Reads a spreadsheet of stock and answers what would happen if it were imported: the tree it
/// describes, and a red/yellow/green verdict per row. It writes nothing — the import itself is a
/// separate, explicit step — so a user can look at the answer before anything changes.
///
/// The shape it expects is the one the workshop already keeps: a sheet per category, a row per
/// physical element, a SUB CATEGORIA column that groups them. Anything else it does not invent.
/// </summary>
public sealed class ItemImporter
{
    public const string Green = "green";
    public const string Yellow = "yellow";
    public const string Red = "red";

    /// <summary>A code is an identifier, not prose: letters, digits, dot, dash, underscore.</summary>
    private static readonly Regex CodeShape = new("^[A-Za-z0-9._-]{1,64}$", RegexOptions.Compiled);

    /// <summary>Rows whose first cell looks like a record id rather than a section title.</summary>
    private static readonly Regex CodeLike = new(@"^-?[A-Za-z]{1,4}\d{2,}", RegexOptions.Compiled);

    /// <summary>
    /// The subcategory spellings the source file uses for the same thing. Kept small and explicit:
    /// a guess that silently merges two real categories costs more than a yellow row that says why.
    /// </summary>
    private static readonly Dictionary<string, string> SubcategoryAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GTAR COMBO"] = "GTR COMBO",
        ["GTR-COMBO"] = "GTR COMBO",
        ["TECALDO SENCILLA"] = "TECLADO SENCILLA",
        ["MICROFONOS"] = "MICROFONO",
    };

    private readonly ApplicationContext _context;

    public ItemImporter(ApplicationContext context) => _context = context;

    public sealed record ImportItem(string Label, string? Code, string? Brand, string? Reference,
        string? Serial, string? Notes, string? Dimensions, string Status, IReadOnlyList<string> Reasons, int Row);

    public sealed record ImportGroup(string Name, string Status, int Green, int Yellow, int Red,
        IReadOnlyList<ImportItem> Items);

    public sealed record ImportSheet(string Name, string Status, int Green, int Yellow, int Red,
        int SkippedSections, IReadOnlyList<ImportGroup> Groups);

    public sealed record ImportSummary(int Rows, int Green, int Yellow, int Red, int Importable, int Skipped);

    public sealed record ImportIssue(string Sheet, int Row, string Level, string Message);

    public sealed record ImportPreview(IReadOnlyList<ImportSheet> Sheets, ImportSummary Summary,
        IReadOnlyList<ImportIssue> Issues, IReadOnlyList<string> Normalizations);

    private sealed record RawRow(int Row, string Sheet, Dictionary<string, string> Cells);

    public async Task<ImportPreview> PreviewAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        // .xls needs the code pages; .xlsx does not, but registering once is harmless and the
        // registration is idempotent.
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        var brands = await KnownBrandsAsync(cancellationToken);
        var takenCodes = await ExistingCodesAsync(cancellationToken);

        var sheets = new List<ImportSheet>();
        var issues = new List<ImportIssue>();
        var normalizations = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        using var reader = ExcelReaderFactory.CreateReader(stream);
        var seenCodes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (sheetName, rows) in ReadSheets(reader))
        {
            if (rows.Count == 0) continue;

            // The header is the first row that names the columns: some sheets carry a title row
            // ("CASES") above it, and a sheet without one is not importable at all.
            var headerIndex = rows.FindIndex(row => HeaderMap(row.Cells).ContainsKey("description"));
            if (headerIndex < 0)
            {
                issues.Add(new ImportIssue(sheetName, rows[0].Row, Red,
                    "La hoja no tiene una fila de cabecera con DESCRIPCION; se ignora."));
                continue;
            }

            var header = HeaderMap(rows[headerIndex].Cells);
            var data = rows.Skip(headerIndex + 1).Select(row => MapCells(row, header)).ToList();
            sheets.Add(BuildSheet(sheetName, data, brands, takenCodes, seenCodes, duplicates, issues, normalizations));
        }

        foreach (var duplicate in duplicates)
        {
            issues.Add(new ImportIssue("*", 0, Red, $"El código {duplicate} aparece más de una vez en el fichero."));
        }

        // A duplicated code is a problem for every row that carries it, not just the second one:
        // importing the first and refusing the twin would leave the file half in.
        if (duplicates.Count > 0)
        {
            sheets = sheets.Select(sheet => sheet with
            {
                Groups = sheet.Groups.Select(group => group with
                {
                    Items = group.Items.Select(item => item.Code is not null && duplicates.Contains(item.Code)
                        ? item with
                        {
                            Status = Red,
                            Reasons = item.Reasons
                                .Where(reason => !reason.Contains("ya apareció", StringComparison.Ordinal))
                                .Append($"El código {item.Code} está repetido en el fichero: ninguna de sus filas se importa.")
                                .ToList()
                        }
                        : item).ToList()
                }).Select(group => group with
                {
                    Green = group.Items.Count(item => item.Status == Green),
                    Yellow = group.Items.Count(item => item.Status == Yellow),
                    Red = group.Items.Count(item => item.Status == Red)
                }).ToList()
            }).Select(sheet => sheet with
            {
                Green = sheet.Groups.Sum(group => group.Green),
                Yellow = sheet.Groups.Sum(group => group.Yellow),
                Red = sheet.Groups.Sum(group => group.Red),
                Status = sheet.Groups.Any(group => group.Red > 0) ? Red
                    : sheet.Groups.Any(group => group.Yellow > 0) ? Yellow : Green
            }).ToList();
        }

        var all = sheets.SelectMany(sheet => sheet.Groups).SelectMany(group => group.Items).ToList();
        var summary = new ImportSummary(
            Rows: all.Count + sheets.Sum(sheet => sheet.SkippedSections),
            Green: all.Count(item => item.Status == Green),
            Yellow: all.Count(item => item.Status == Yellow),
            Red: all.Count(item => item.Status == Red),
            Importable: all.Count(item => item.Status != Red),
            Skipped: all.Count(item => item.Status == Red));

        return new ImportPreview(sheets, summary, issues, normalizations.ToList());
    }

    private ImportSheet BuildSheet(string name, IEnumerable<RawRow> rows, HashSet<string> brands,
        HashSet<string> takenCodes, Dictionary<string, int> seenCodes, HashSet<string> duplicates,
        List<ImportIssue> issues, SortedSet<string> normalizations)
    {
        // Grouped by name, not by adjacency: the file interleaves subcategories, and a tree that
        // repeats "ATRIL" every time the sheet comes back to it is not the tree the file describes.
        var groups = new List<ImportGroup>();
        var buckets = new Dictionary<string, List<ImportItem>>(StringComparer.Ordinal);
        var order = new List<string>();

        void Add(ImportItem item, string groupName)
        {
            if (!buckets.TryGetValue(groupName, out var bucket))
            {
                bucket = new List<ImportItem>();
                buckets[groupName] = bucket;
                order.Add(groupName);
            }

            bucket.Add(item);
        }

        var skippedSections = 0;

        foreach (var row in rows)
        {
            var description = Clean(row.Cells.GetValueOrDefault("description"));
            var subcategory = Clean(row.Cells.GetValueOrDefault("subcategory"));

            if (description.Length == 0)
            {
                // A section title ("HARDWARE", "FOOT SWITCH") is not a lost record; a row with a code
                // or a brand and no name is.
                if (LooksLikeSectionTitle(row)) { skippedSections++; continue; }
                var rowCode = Clean(row.Cells.GetValueOrDefault("code"));
                Add(new ImportItem(
                    Label: rowCode is { Length: > 0 } ? rowCode : "(sin nombre)",
                    Code: CleanOrNull(row.Cells.GetValueOrDefault("code")),
                    Brand: CleanOrNull(row.Cells.GetValueOrDefault("brand")),
                    Reference: CleanOrNull(row.Cells.GetValueOrDefault("reference")),
                    Serial: CleanOrNull(row.Cells.GetValueOrDefault("serial")),
                    Notes: CleanOrNull(row.Cells.GetValueOrDefault("notes")),
                    Dimensions: CleanOrNull(row.Cells.GetValueOrDefault("dimensions")),
                    Status: Red, Reasons: new[] { "Sin descripción." }, Row: row.Row),
                    subcategory.Length > 0
                        ? NormalizeSubcategory(subcategory, normalizations, out _)
                        : Normalize(name));
                issues.Add(new ImportIssue(name, row.Row, Red, "Fila sin descripción: no se puede nombrar el elemento."));
                continue;
            }

            var corrected = false;
            var groupName = subcategory.Length > 0
                ? NormalizeSubcategory(subcategory, normalizations, out corrected)
                : Normalize(name);
            var judged = Judge(name, row, description, brands, takenCodes, seenCodes, duplicates, issues);
            if (corrected)
            {
                // A corrected spelling is importable, but the user should see that the file said
                // something else before it went in.
                judged = judged with
                {
                    Status = judged.Status == Green ? Yellow : judged.Status,
                    Reasons = judged.Reasons.Append($"Subcategoría corregida a «{groupName}».").ToList()
                };
            }

            Add(judged, groupName);
        }

        foreach (var groupName in order)
        {
            var bucket = buckets[groupName];
            groups.Add(new ImportGroup(groupName,
                bucket.Any(item => item.Status == Red) ? Red : bucket.Any(item => item.Status == Yellow) ? Yellow : Green,
                bucket.Count(item => item.Status == Green),
                bucket.Count(item => item.Status == Yellow),
                bucket.Count(item => item.Status == Red),
                bucket));
        }

        return new ImportSheet(name,
            groups.Any(group => group.Status == Red) ? Red : groups.Any(group => group.Status == Yellow) ? Yellow : Green,
            groups.Sum(group => group.Green), groups.Sum(group => group.Yellow), groups.Sum(group => group.Red),
            skippedSections, groups);
    }

    private ImportItem Judge(string sheet, RawRow row, string description, HashSet<string> brands,
        HashSet<string> takenCodes, Dictionary<string, int> seenCodes, HashSet<string> duplicates,
        List<ImportIssue> issues)
    {
        var reasons = new List<string>();
        var status = Green;

        // Red wins over yellow, and yellow over green: one blocking problem is enough.
        void Flag(string reason, string level = Yellow)
        {
            reasons.Add(reason);
            if (level == Red) status = Red;
            else if (status == Green) status = Yellow;
        }

        var code = CleanOrNull(row.Cells.GetValueOrDefault("code"));
        if (code is not null)
        {
            if (!CodeShape.IsMatch(code))
            {
                Flag($"Código «{code}» fuera del formato permitido (letras, números, punto, guion).", Red);
            }
            else if (takenCodes.Contains(code))
            {
                Flag($"El código {code} ya existe en el inventario.", Red);
            }
            else if (seenCodes.TryGetValue(code, out var firstRow))
            {
                duplicates.Add(code);
                Flag($"El código {code} ya apareció en la fila {firstRow} del fichero.", Red);
            }
            else
            {
                seenCodes[code] = row.Row;
            }
        }
        else
        {
            Flag("Sin código: se puede importar, pero no se podrá distinguir de otro elemento igual.");
        }

        var brand = CleanOrNull(row.Cells.GetValueOrDefault("brand"));
        if (brand is null)
        {
            Flag("Sin marca.");
        }
        else if (!brands.Contains(Clean(brand)))
        {
            Flag(brand.Any(char.IsDigit)
                ? $"La marca «{brand}» parece una referencia mal puesta, no una marca."
                : $"La marca «{brand}» no está en el catálogo de Brand.");
        }

        if (description.Length < 4)
        {
            Flag($"La descripción «{description}» es demasiado corta para identificar el elemento.");
        }

        return new ImportItem(description, code, brand,
            CleanOrNull(row.Cells.GetValueOrDefault("reference")),
            CleanOrNull(row.Cells.GetValueOrDefault("serial")),
            CleanOrNull(row.Cells.GetValueOrDefault("notes")),
            CleanOrNull(row.Cells.GetValueOrDefault("dimensions")),
            status, reasons, row.Row);
    }

    /// <summary>Applies the alias table, recording each correction so the user sees what changed.</summary>
    private static string NormalizeSubcategory(string subcategory, SortedSet<string> normalizations, out bool corrected)
    {
        var normalized = Normalize(subcategory);
        corrected = false;
        if (!SubcategoryAliases.TryGetValue(normalized, out var target) || target == normalized) return normalized;
        normalizations.Add($"«{normalized}» → «{target}»");
        corrected = true;
        return target;
    }

    private static bool LooksLikeSectionTitle(RawRow row)
    {
        var filled = row.Cells.Where(pair => pair.Value.Length > 0).ToList();
        if (filled.Count == 0) return true;
        if (filled.Count > 1) return false;
        var only = filled[0].Value;
        return !CodeLike.IsMatch(only);
    }

    /// <summary>
    /// Streams every sheet once, keeping the spreadsheet row number so an issue points at the row
    /// the user sees in Excel. No typed data set: the file is read as strings and nothing is guessed.
    /// </summary>
    private static List<(string Name, List<RawRow> Rows)> ReadSheets(IExcelDataReader reader)
    {
        var sheets = new List<(string, List<RawRow>)>();
        do
        {
            var name = string.IsNullOrWhiteSpace(reader.Name) ? $"Hoja {sheets.Count + 1}" : reader.Name.Trim();
            var rows = new List<RawRow>();
            var rowNumber = 0;
            while (reader.Read())
            {
                rowNumber++;
                var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var column = 0; column < reader.FieldCount; column++)
                {
                    var text = Convert.ToString(reader.GetValue(column))?.Trim() ?? string.Empty;
                    if (text.Length == 0) continue;
                    cells[$"col{column}"] = text;
                }

                if (cells.Count > 0) rows.Add(new RawRow(rowNumber, name, cells));
            }

            if (rows.Count > 0) sheets.Add((name, rows));
        } while (reader.NextResult());

        return sheets;
    }

    /// <summary>Column names the sheet may use, mapped to the one name the importer speaks.</summary>
    private static readonly Dictionary<string, string[]> HeaderAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["code"] = new[] { "CONSECUTIVO", "CODIGO", "CÓDIGO", "CODE", "ID", "PLACA" },
        ["subcategory"] = new[] { "SUB CATEGORIA", "SUB CATEGORÍA", "SUBCATEGORIA", "CATEGORIA", "GRUPO" },
        ["brand"] = new[] { "MARCA", "BRAND" },
        ["reference"] = new[] { "REFERENCIA", "REFERENCE", "MODELO" },
        ["description"] = new[] { "DESCRIPCION", "DESCRIPCIÓN", "DESCRIPTION", "NOMBRE", "ELEMENTO" },
        ["serial"] = new[] { "SERIAL", "SERIE", "S/N" },
        ["notes"] = new[] { "NOTAS", "NOTES", "OBSERVACIONES" },
    };

    private static Dictionary<string, string> HeaderMap(IReadOnlyDictionary<string, string> headerCells)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (column, text) in headerCells)
        {
            var normalized = Normalize(text);
            foreach (var (field, aliases) in HeaderAliases)
            {
                if (map.ContainsKey(field)) continue;
                if (aliases.Any(alias => Normalize(alias) == normalized))
                {
                    map[field] = column;
                    break;
                }
            }

            if (normalized.StartsWith("DIMENSION", StringComparison.Ordinal) && !map.ContainsKey("dimensions"))
                map["dimensions"] = column;
        }

        return map;
    }

    private static RawRow MapCells(RawRow row, IReadOnlyDictionary<string, string> header)
    {
        var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (field, column) in header)
        {
            if (row.Cells.TryGetValue(column, out var value)) cells[field] = value;
        }

        return row with { Cells = cells };
    }

    /// <summary>Trim, collapse inner runs of spaces and drop the BOM some exports carry.</summary>
    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var trimmed = value.Trim().Trim('\uFEFF');
        return Regex.Replace(trimmed, @"\s+", " ").ToUpperInvariant();
    }

    private static string Clean(string? value) => Normalize(value);

    private static string? CleanOrNull(string? value)
    {
        var normalized = Normalize(value);
        return normalized.Length == 0 ? null : normalized;
    }

    private async Task<HashSet<string>> KnownBrandsAsync(CancellationToken cancellationToken)
    {
        var root = await _context.Constants
            .FirstOrDefaultAsync(row => row.ParentId == null && row.Label == "Brand", cancellationToken);
        if (root is null) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var children = await _context.Constants
            .Where(row => row.ParentId == root.Id)
            .ToListAsync(cancellationToken);
        return children.Select(child => Normalize(child.Label)).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every code already in the inventory, archived rows included: a code identifies a physical
    /// element, so a deleted one still owns its code.
    /// </summary>
    private async Task<HashSet<string>> ExistingCodesAsync(CancellationToken cancellationToken)
    {
        var rows = await _context.Items.IgnoreQueryFilters()
            .Select(item => new { item.Attributes })
            .ToListAsync(cancellationToken);

        var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var code = ReadCode(row.Attributes);
            if (code is not null) codes.Add(code);
        }

        return codes;
    }

    /// <summary>Reads the code out of an item's attribute bag, tolerating anything that is not JSON.</summary>
    public static string? ReadCode(string? attributes)
    {
        if (string.IsNullOrWhiteSpace(attributes)) return null;
        try
        {
            using var document = JsonDocument.Parse(attributes);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("Code", out var code)
                && code.ValueKind == JsonValueKind.String
                ? code.GetString()?.Trim().ToUpperInvariant()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
