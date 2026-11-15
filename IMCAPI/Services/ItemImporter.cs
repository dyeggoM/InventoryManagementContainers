using System.Text.Json;
using System.Text.RegularExpressions;
using ExcelDataReader;
using IMCAPI.DB;
using IMCAPI.DB.Entities;
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
    private readonly AuditLogWriter _audit;

    public ItemImporter(ApplicationContext context, AuditLogWriter audit)
    {
        _context = context;
        _audit = audit;
    }

    /// <summary>What an import did, so the answer can be checked against the file.</summary>
    public sealed record ImportResult(int CreatedSheets, int CreatedGroups, int CreatedItems,
        int SkippedRed, int SkippedYellow, int SkippedExisting, bool IncludeYellow);

    public sealed record ImportItem(string Label, string? Code, string? Brand, string? Reference,
        string? Serial, string? Notes, string? Dimensions, string Status, IReadOnlyList<string> Reasons, int Row);

    public sealed record ImportGroup(string Name, string Status, int Green, int Yellow, int Red,
        IReadOnlyList<ImportItem> Items);

    public sealed record ImportSheet(string Name, string Status, int Green, int Yellow, int Red,
        int SkippedSections, IReadOnlyList<ImportGroup> Groups);

    public sealed record ImportSummary(int Rows, int Sections, int Green, int Yellow, int Red, int Importable, int Skipped);

    public sealed record ImportIssue(string Sheet, int Row, string Level, string Message);

    public sealed record ImportPreview(IReadOnlyList<ImportSheet> Sheets, ImportSummary Summary,
        IReadOnlyList<ImportIssue> Issues, IReadOnlyList<string> Normalizations);

    private sealed record RawRow(int Row, string Sheet, Dictionary<string, string> Cells);

    /// <param name="format">
    /// Which reader to use. The caller knows the extension, so this is told, never guessed.
    /// </param>
    /// <param name="sourceName">
    /// The file name for a CSV, which has no sheet names of its own; the tree's root takes it. Ignored
    /// for a workbook, whose sheets name themselves.
    /// </param>
    public async Task<ImportPreview> PreviewAsync(Stream stream, ImportFormat format = ImportFormat.Workbook,
        string? sourceName = null, CancellationToken cancellationToken = default) =>
        (await BuildAsync(stream, format, sourceName, cancellationToken)).Preview;

    /// <summary>The preview and the rows it is made of: one parse serves both calls.</summary>
    private async Task<(ImportPreview Preview, List<(string Sheet, string Group, ImportItem Item)> Rows)> BuildAsync(
        Stream stream, ImportFormat format, string? sourceName, CancellationToken cancellationToken)
    {
        // .xls needs the code pages; .xlsx does not, but registering once is harmless and the
        // registration is idempotent.
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        var brands = await KnownBrandsAsync(cancellationToken);
        var takenCodes = await ExistingCodesAsync(cancellationToken);

        var sheets = new List<ImportSheet>();
        var issues = new List<ImportIssue>();
        var normalizations = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        var seenCodes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (sheetName, rows) in ReadSource(stream, format, sourceName))
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
        // `Rows` is the records the file describes; a section title is counted apart, so
        // green + yellow + red adds up to it and nobody has to wonder where 18 rows went.
        var summary = new ImportSummary(
            Rows: all.Count,
            Sections: sheets.Sum(sheet => sheet.SkippedSections),
            Green: all.Count(item => item.Status == Green),
            Yellow: all.Count(item => item.Status == Yellow),
            Red: all.Count(item => item.Status == Red),
            Importable: all.Count(item => item.Status != Red),
            Skipped: all.Count(item => item.Status == Red));

        // A CSV is one sheet, so "no records" means the file holds nothing: answering with an empty
        // tree would look like a successful import of zero rows.
        if (format == ImportFormat.Csv && summary.Rows == 0)
        {
            throw new ImportFileException(
                "El CSV no tiene filas de datos: hace falta una cabecera con DESCRIPCION y al menos un elemento.");
        }

        var preview = new ImportPreview(sheets, summary, issues, normalizations.ToList());
        var planned = sheets
            .SelectMany(sheet => sheet.Groups.SelectMany(group => group.Items.Select(item => (Sheet: sheet.Name, Group: group.Name, Item: item))))
            .ToList();
        return (preview, planned);
    }

    /// <summary>
    /// Writes what the preview called green (and yellow, when asked) into the tree, in one
    /// transaction: sheets become roots, SUB CATEGORIA becomes the level under them, and each row
    /// becomes a record. Reds never go in. Running it twice on the same file creates nothing the
    /// second time — a code that already exists, or a nameless record that is already there, is
    /// skipped instead of duplicated.
    /// </summary>
    public async Task<ImportResult> ImportAsync(Stream stream, ImportFormat format = ImportFormat.Workbook,
        string? sourceName = null, bool includeYellow = true,
        IReadOnlyCollection<string>? skipCodes = null, CancellationToken cancellationToken = default)
    {
        var (preview, rows) = await BuildAsync(stream, format, sourceName, cancellationToken);

        var skip = new HashSet<string>(skipCodes ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        var skippedRed = rows.Count(row => row.Item.Status == Red);
        var skippedYellow = includeYellow ? 0 : rows.Count(row => row.Item.Status == Yellow);

        var wanted = rows
            .Where(row => row.Item.Status == Green || (includeYellow && row.Item.Status == Yellow))
            .Where(row => row.Item.Code is null || !skip.Contains(row.Item.Code))
            .ToList();

        var existingCodes = await ExistingCodesAsync(cancellationToken);
        // The pairs that already exist today, so a codeless row is not created twice across runs.
        // Rows created *during* this run are not added to this set on purpose: thirty identical
        // rows in one file are thirty units, not thirty duplicates.
        var existingPairs = await ExistingPairsAsync(cancellationToken);

        if (wanted.Count == 0)
            return new ImportResult(0, 0, 0, skippedRed, skippedYellow, 0, includeYellow);

        var createdSheets = 0;
        var createdGroups = 0;
        var createdItems = 0;
        var skippedExisting = 0;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var roots = await _context.Items.IgnoreQueryFilters().Where(item => item.ParentId == null)
            .ToListAsync(cancellationToken);
        var children = await _context.Items.IgnoreQueryFilters().Where(item => item.ParentId != null)
            .ToListAsync(cancellationToken);

        foreach (var sheet in wanted.GroupBy(row => row.Sheet, StringComparer.Ordinal))
        {
            var root = roots.FirstOrDefault(item => Same(item.Label, sheet.Key));
            if (root is null)
            {
                root = NewItem(sheet.Key, parentId: null);
                _context.Items.Add(root);
                await _context.SaveChangesAsync(cancellationToken); // the id is needed by its children
                roots.Add(root);
                createdSheets++;
            }

            foreach (var group in sheet.GroupBy(row => row.Group, StringComparer.Ordinal))
            {
                var parent = children.FirstOrDefault(item => item.ParentId == root.Id && Same(item.Label, group.Key));
                if (parent is null)
                {
                    parent = NewItem(group.Key, root.Id);
                    _context.Items.Add(parent);
                    await _context.SaveChangesAsync(cancellationToken);
                    children.Add(parent);
                    createdGroups++;
                }

                foreach (var (_, _, item) in group)
                {
                    if (item.Code is not null && existingCodes.Contains(item.Code))
                    {
                        skippedExisting++;
                        continue;
                    }

                    var pair = (parent.Id, Normalize(item.Label));
                    if (item.Code is null && existingPairs.Contains(pair))
                    {
                        skippedExisting++;
                        continue;
                    }

                    _context.Items.Add(NewItem(item.Label, parent.Id, item));
                    createdItems++;
                    if (item.Code is not null) existingCodes.Add(item.Code);
                }

                await _context.SaveChangesAsync(cancellationToken);
            }
        }

        await _audit.WriteAsync("ItemImport", "Item", null, cancellationToken: cancellationToken,
            afterJson: JsonSerializer.Serialize(new
            {
                rows = preview.Summary.Rows,
                createdSheets, createdGroups, createdItems,
                skippedRed, skippedYellow, skippedExisting,
                includeYellow
            }));

        await transaction.CommitAsync(cancellationToken);

        return new ImportResult(createdSheets, createdGroups, createdItems, skippedRed, skippedYellow, skippedExisting, includeYellow);
    }

    private static Item NewItem(string label, int? parentId, ImportItem? source = null)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (source is not null)
        {
            void Put(string key, string? value)
            {
                if (!string.IsNullOrWhiteSpace(value)) attributes[key] = value;
            }

            Put("Description", source.Label);
            Put("Brand", source.Brand);
            Put("Code", source.Code);
            Put("Reference", source.Reference);
            Put("Serial", source.Serial);
            Put("Notes", source.Notes);
            Put("Dimensions", source.Dimensions);
        }

        return new Item
        {
            Label = label,
            Icon = string.Empty,
            ParentId = parentId,
            IsActive = true,
            Attributes = attributes.Count == 0 ? "{}" : JsonSerializer.Serialize(attributes)
        };
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);

    /// <summary>Every (parent, name) already in the tree, so a nameless record is not created twice.</summary>
    private async Task<HashSet<(int?, string)>> ExistingPairsAsync(CancellationToken cancellationToken)
    {
        var rows = await _context.Items.IgnoreQueryFilters()
            .Select(item => new { item.ParentId, item.Label })
            .ToListAsync(cancellationToken);
        return rows.Select(row => (row.ParentId, Normalize(row.Label))).ToHashSet();
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
    /// Opens the file with the reader its format asks for. Both readers answer the same shape — a name
    /// and its rows of text cells — so everything downstream is shared.
    /// </summary>
    private static List<(string Name, List<RawRow> Rows)> ReadSource(Stream stream, ImportFormat format,
        string? sourceName) => format == ImportFormat.Csv ? ReadCsv(stream, sourceName) : ReadWorkbook(stream);

    private static List<(string Name, List<RawRow> Rows)> ReadWorkbook(Stream stream)
    {
        using var reader = ExcelReaderFactory.CreateReader(stream);
        return ReadSheets(reader);
    }

    /// <summary>
    /// A CSV has no sheets, so the file is the sheet: its name — the controller passes the file name
    /// without extension — becomes the root of the tree, and every row is read as text and mapped
    /// through the same header aliases the workbook path uses.
    /// </summary>
    private static List<(string Name, List<RawRow> Rows)> ReadCsv(Stream stream, string? sourceName)
    {
        var name = Normalize(sourceName);
        if (name.Length == 0) name = "HOJA 1";

        var rows = new List<RawRow>();
        var rowNumber = 0;
        foreach (var record in CsvSheetReader.Read(stream))
        {
            rowNumber++;
            // The number is the record's, not the line's: a quoted field may run over several lines,
            // and an issue should point at the row the user counts in the spreadsheet.
            if (RawRowAt(rowNumber, name, record) is { } row) rows.Add(row);
        }

        return rows.Count == 0 ? new() : new() { (name, rows) };
    }

    /// <summary>
    /// One row of text cells, keyed the way the header map reads them and numbered as the user sees it.
    /// Empty cells are absent: a row of nothing is not a record.
    /// </summary>
    private static RawRow? RawRowAt(int rowNumber, string sheet, IEnumerable<string?> values)
    {
        var cells = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var column = 0;
        foreach (var value in values)
        {
            var text = value?.Trim() ?? string.Empty;
            if (text.Length > 0) cells[$"col{column}"] = text;
            column++;
        }

        return cells.Count == 0 ? null : new RawRow(rowNumber, sheet, cells);
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
                var values = new string?[reader.FieldCount];
                for (var column = 0; column < reader.FieldCount; column++)
                    values[column] = Convert.ToString(reader.GetValue(column));
                if (RawRowAt(rowNumber, name, values) is { } row) rows.Add(row);
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
