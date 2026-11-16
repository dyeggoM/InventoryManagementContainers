using System.Text.Json;
using IMCAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IMCAPI.Controllers;

/// <summary>
/// Reads a spreadsheet of stock and says what would be created, without creating anything. The
/// import itself is a second, explicit call: a user sees the tree and its colours first, and the
/// write is a decision rather than a side effect of uploading a file.
/// </summary>
[ApiController]
[Route("api/import")]
[Authorize(Policy = "RequiereAdmin")]
public sealed class ImportController : ControllerBase
{
    private static readonly string[] AcceptedExtensions = { ".xlsx", ".xls", ".csv" };

    private readonly ItemImporter _importer;
    private readonly ILogger<ImportController> _log;

    public ImportController(ItemImporter importer, ILogger<ImportController> log)
    {
        _importer = importer;
        _log = log;
    }

    /// <summary>The tree the file describes, each row green, yellow or red, and why. Writes nothing.</summary>
    [HttpPost("preview")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Preview(IFormFile? file, [FromForm] string? corrections,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(Problem("No file", "Attach the spreadsheet in the `file` field."));

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AcceptedExtensions.Contains(extension))
            return BadRequest(Problem("Unsupported file",
                $"The importer reads {HumanList(AcceptedExtensions)}; this is {extension}."));

        var format = FormatOf(extension);
        if (!ParseCorrections(corrections, out var parsed, out var messages, out var error))
            return BadRequest(Problem("Invalid corrections", error!));
        try
        {
            await using var stream = file.OpenReadStream();
            var preview = await _importer.PreviewAsync(stream, format, SheetName(file.FileName, format),
                cancellationToken, parsed, messages);
            return Ok(preview);
        }
        catch (ImportFileException exception)
        {
            // A readable file that holds nothing to import is still the user's file.
            return BadRequest(Problem("Nothing to import", exception.Message));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A corrupt workbook is the user's file, not a server failure.
            _log.LogWarning(exception, "The import preview could not read {FileName}", file.FileName);
            return BadRequest(Problem("Unreadable spreadsheet",
                "The file could not be read as a spreadsheet. Save it again as .xlsx or .csv and retry."));
        }
    }

    /// <summary>
    /// Writes the file into the tree. Green rows always go in; yellow ones do too unless the caller
    /// says otherwise, and red ones never do. One transaction, and a second run of the same file
    /// creates nothing.
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(20 * 1024 * 1024)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Import([FromForm] IFormFile? file, [FromForm] bool includeYellow = true,
        [FromForm] List<string>? skipCodes = null, [FromForm] string? corrections = null,
        CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
            return BadRequest(Problem("No file", "Attach the spreadsheet in the `file` field."));

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AcceptedExtensions.Contains(extension))
            return BadRequest(Problem("Unsupported file",
                $"The importer reads {HumanList(AcceptedExtensions)}; this is {extension}."));

        var format = FormatOf(extension);
        if (!ParseCorrections(corrections, out var parsed, out var messages, out var error))
            return BadRequest(Problem("Invalid corrections", error!));
        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _importer.ImportAsync(stream, format, SheetName(file.FileName, format),
                includeYellow, skipCodes, cancellationToken, parsed, messages);
            _log.LogInformation("Imported {Items} records from {FileName} ({Sheets} sheets, {Groups} groups)",
                result.CreatedItems, file.FileName, result.CreatedSheets, result.CreatedGroups);
            return Ok(result);
        }
        catch (ImportFileException exception)
        {
            // Thrown before anything is written, so the tree is untouched.
            return BadRequest(Problem("Nothing to import", exception.Message));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The transaction is rolled back, so a failure leaves the tree exactly as it was.
            _log.LogError(exception, "The import of {FileName} failed and was rolled back", file.FileName);
            return BadRequest(Problem("The import failed",
                "Nothing was written: the file could not be imported. The log has the reason."));
        }
    }

    private static bool ParseCorrections(string? json, out List<ItemImporter.ImportCorrection> parsed,
        out List<string> messages, out string? error)
    {
        parsed = new();
        messages = new();
        error = null;
        if (string.IsNullOrWhiteSpace(json)) return true;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                throw new JsonException("Expected an array.");
            if (document.RootElement.GetArrayLength() > 1000)
            {
                error = "The corrections array cannot exceed 1000 entries.";
                return false;
            }
            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("row", out var row) ||
                    row.ValueKind != JsonValueKind.Number || !row.TryGetInt32(out var number) || number < 1)
                {
                    messages.Add("Corrección inválida: falta un número de fila válido.");
                    continue;
                }
                var fields = new Dictionary<string, string?>(StringComparer.Ordinal);
                string? source = null;
                var valid = true;
                foreach (var property in entry.EnumerateObject())
                {
                    if (property.Name == "row") continue;
                    if (property.Value.ValueKind != JsonValueKind.String && property.Value.ValueKind != JsonValueKind.Null)
                    {
                        valid = false;
                        continue;
                    }
                    var value = property.Value.ValueKind == JsonValueKind.Null ? null : property.Value.GetString();
                    if (value?.Length > 512)
                    {
                        error = $"Correction field '{property.Name}' exceeds 512 characters.";
                        return false;
                    }
                    if (property.Name == "source") source = value;
                    else if (property.Name is "label" or "code" or "subcategory" or "brand" or
                        "reference" or "notes" or "serial" or "dimensions")
                        fields[property.Name == "label" ? "description" : property.Name] = value;
                    else valid = false;
                }
                if (valid) parsed.Add(new ItemImporter.ImportCorrection(number, fields, source));
                else messages.Add($"Fila {number}: corrección inválida; se conserva la fila original.");
            }
        }
        catch (JsonException)
        {
            parsed.Clear();
            messages.Add("JSON inválido en corrections; se conserva la fila original.");
        }
        return true;
    }

    /// <summary>The extension is the only thing that says which reader to use; the bytes never are.</summary>
    private static ImportFormat FormatOf(string extension) =>
        extension == ".csv" ? ImportFormat.Csv : ImportFormat.Workbook;

    /// <summary>
    /// A workbook names its own sheets; a CSV has only the file it arrived as, and that name becomes
    /// the root of the tree. It is a label, not a path, so only the last segment survives.
    /// </summary>
    private static string? SheetName(string fileName, ImportFormat format)
    {
        if (format != ImportFormat.Csv) return null;
        var path = fileName.Replace('\\', '/');
        return Path.GetFileNameWithoutExtension(path[(path.LastIndexOf('/') + 1)..]);
    }

    /// <summary>".xlsx, .xls or .csv", so the message reads as a sentence when a third one arrives.</summary>
    private static string HumanList(IReadOnlyList<string> values) =>
        values.Count <= 1 ? string.Concat(values)
            : $"{string.Join(", ", values.Take(values.Count - 1))} or {values[^1]}";

    private static ProblemDetails Problem(string title, string detail) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title = title,
        Detail = detail
    };
}
