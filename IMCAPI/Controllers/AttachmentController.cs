using IMCAPI.DB;
using IMCAPI.DB.Entities;
using IMCAPI.DB.Repositories;
using IMCAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IMCAPI.Controllers;

[Route("api/[controller]")]
[ApiController]
//[Authorize]
public class AttachmentController : BaseController<Attachment>
{
    IRepository<Attachment> _repository;
    private readonly AttachmentStorage _storage;
    private readonly ApplicationContext _context;

    public AttachmentController(IRepository<Attachment> repository, ILogger<BaseController<Attachment>> logger,
        AttachmentStorage storage, ApplicationContext context) : base(repository, logger)
    {
        _repository = repository;
        _storage = storage;
        _context = context;
    }

    [HttpPost]
    [Authorize(Policy = "RequiereEscritura")]
    public override async Task<IActionResult> Post(Attachment data)
    {
        // The JSON route writes the bytes before the row for the same reason the multipart one
        // did, so it needs the same two guards: the record has to exist, and a failed insert has
        // to take its file with it.
        if (data.ItemId <= 0) return BadRequest("An attachment needs the record it belongs to");
        if (!await _context.Items.AnyAsync(item => item.Id == data.ItemId))
            return BadRequest($"Record {data.ItemId} does not exist.");
        data.Type ??= "Photo";

        var error = await _storage.ApplyUploadAsync(data);
        if (error is not null) return BadRequest(error);

        try
        {
            // Not `base.Post`: that helper catches the exception and answers 500, which made the
            // cleanup unreachable and left the file behind. The insert is done here so a failure
            // can remove what it just wrote before the 500 goes out.
            await _repository.Add(data);
            return Ok(data);
        }
        catch
        {
            _storage.Delete(data.FilePath);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// The same upload as a multipart form: the bytes travel as bytes instead of base64 inside
    /// JSON, which is a third smaller and does not hold the whole photo twice in memory.
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(25 * 1024 * 1024)]
    [Authorize(Policy = "RequiereEscritura")]
    public async Task<IActionResult> Upload([FromForm] int itemId, [FromForm] string? type,
        [FromForm] string? fileName, IFormFile? file)
    {
        if (itemId <= 0) return BadRequest("An attachment needs the record it belongs to");
        if (file is null || file.Length == 0) return BadRequest("Choose a file to upload");
        if (file.Length > AttachmentStorage.MaxBytes)
            return BadRequest($"The file is larger than {AttachmentStorage.MaxBytes / (1024 * 1024)} MB");

        // The bytes are written to disk before the row exists, so a record id that does not exist
        // used to answer 500 and leave a file behind that no route could name or delete. Look the
        // record up first, and clean the file up if the insert still fails.
        if (!await _context.Items.AnyAsync(item => item.Id == itemId))
            return BadRequest($"Record {itemId} does not exist.");

        byte[] bytes;
        await using (var buffer = new MemoryStream())
        {
            await file.CopyToAsync(buffer);
            bytes = buffer.ToArray();
        }

        var stored = await _storage.SaveAsync(itemId, fileName ?? file.FileName, bytes);

        var attachment = new Attachment
        {
            TenantId = _context.CurrentTenantId,
            ItemId = itemId,
            Type = type ?? "Photo",
            FileName = fileName ?? file.FileName,
            FilePath = stored.RelativePath,
            ThumbnailBase64 = stored.ThumbnailBase64,
        };

        try
        {
            await _repository.Add(attachment);
        }
        catch
        {
            _storage.Delete(stored.RelativePath);
            throw;
        }

        return Ok(attachment);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "RequiereEscritura")]
    public override async Task<IActionResult> Put(int id, Attachment data)
    {
        if (id != data.Id) return BadRequest();
        // A foreign item and a missing item are both outside this request's tenant-filtered tree.
        if (data.ItemId <= 0 || !await _context.Items.AnyAsync(item => item.Id == data.ItemId))
            return BadRequest("The attachment needs a record in this organization.");
        return await base.Put(id, data);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "RequiereEscritura")]
    public override async Task<IActionResult> Delete(int id)
    {
        var existing = await _repository.GetById(id);
        var result = await base.Delete(id);
        if (result is NoContentResult or OkResult)
        {
            _storage.Delete(existing?.FilePath);
        }

        return result;
    }

    /// <summary>The original file, as stored on disk.</summary>
    [HttpGet("{id:int}/file")]
    [Authorize(Policy = "RequiereLectura")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetFile(int id)
    {
        try
        {
            var attachment = await _repository.GetById(id);
            if (attachment?.FilePath is null) return NotFound();

            var absolute = _storage.ToAbsolute(attachment.FilePath);
            if (string.IsNullOrEmpty(absolute) || !System.IO.File.Exists(absolute)) return NotFound();

            return PhysicalFile(absolute, AttachmentStorage.ContentTypeFor(attachment.FilePath),
                enableRangeProcessing: true);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet("item/{id:int}")]
    [Authorize(Policy = "RequiereLectura")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetByItemId(int id)
    {
        try
        {
            if(id == 0) return BadRequest();
            var data = await _repository.GetAllByCondition(x=>x.ItemId == id);
            if(data == null || data.Count ==0) return NoContent();
            return Ok(data);
        }
        catch (Exception)
        {
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}
