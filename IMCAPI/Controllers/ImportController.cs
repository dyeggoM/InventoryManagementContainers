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
    public async Task<IActionResult> Preview(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(Problem("No file", "Attach the spreadsheet in the `file` field."));

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AcceptedExtensions.Contains(extension))
            return BadRequest(Problem("Unsupported file",
                $"The importer reads {HumanList(AcceptedExtensions)}; this is {extension}."));

        var format = FormatOf(extension);
        try
        {
            await using var stream = file.OpenReadStream();
            var preview = await _importer.PreviewAsync(stream, format, SheetName(file.FileName, format), cancellationToken);
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
        [FromForm] List<string>? skipCodes = null, CancellationToken cancellationToken = default)
    {
        if (file is null || file.Length == 0)
            return BadRequest(Problem("No file", "Attach the spreadsheet in the `file` field."));

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AcceptedExtensions.Contains(extension))
            return BadRequest(Problem("Unsupported file",
                $"The importer reads {HumanList(AcceptedExtensions)}; this is {extension}."));

        var format = FormatOf(extension);
        try
        {
            await using var stream = file.OpenReadStream();
            var result = await _importer.ImportAsync(stream, format, SheetName(file.FileName, format),
                includeYellow, skipCodes, cancellationToken);
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
