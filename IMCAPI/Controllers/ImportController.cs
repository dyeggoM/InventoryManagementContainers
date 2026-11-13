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
    private static readonly string[] AcceptedExtensions = { ".xlsx", ".xls" };

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
                $"The importer reads {string.Join(" or ", AcceptedExtensions)}; this is {extension}."));

        try
        {
            await using var stream = file.OpenReadStream();
            var preview = await _importer.PreviewAsync(stream, cancellationToken);
            return Ok(preview);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A corrupt workbook is the user's file, not a server failure.
            _log.LogWarning(exception, "The import preview could not read {FileName}", file.FileName);
            return BadRequest(Problem("Unreadable spreadsheet",
                "The file could not be read as a spreadsheet. Save it again as .xlsx and retry."));
        }
    }

    private static ProblemDetails Problem(string title, string detail) => new()
    {
        Status = StatusCodes.Status400BadRequest,
        Title = title,
        Detail = detail
    };
}
