using IMCAPI.DB;
using IMCAPI.DB.Entities;
using IMCAPI.DB.Repositories;
using IMCAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IMCAPI.Controllers
{
    #region Item

    [Route("api/[controller]")]
    [ApiController]
    //[Authorize]
    public class ItemController : BaseController<Item>
    {
        private readonly AttachmentStorage _attachments;
        private readonly ApplicationContext _context;

        public ItemController(IRepository<Item> repository, ILogger<BaseController<Item>> logger, AttachmentStorage attachments, ApplicationContext context) : base(repository, logger)
        {
            _attachments = attachments;
            _context = context;
        }

        [HttpPost]
        [Authorize(Policy = "RequiereEscritura")]
        public override async Task<IActionResult> Post(Item data)
        {
            // Attachments are created with the item, so their files are stored here too.
            if (data.Attachments is { Count: > 0 })
            {
                foreach (var attachment in data.Attachments)
                {
                    var error = await _attachments.ApplyUploadAsync(attachment);
                    if (error is not null) return BadRequest(error);
                }
            }

            return await base.Post(data);
        }

        [HttpPut("{id:int}")]
        [Authorize(Policy = "RequiereEscritura")]
        public override async Task<IActionResult> Put(int id, Item data)
        {
            if (data.ParentId is int proposedParentId && await WouldCreateCycle(id, proposedParentId))
                return BadRequest("Un registro no puede ser padre de sí mismo ni de uno de sus descendientes.");

            if (data.Attachments is { Count: > 0 })
            {
                foreach (var attachment in data.Attachments)
                {
                    if (attachment.ItemId == 0)
                    {
                        attachment.ItemId = id;
                    }

                    var error = await _attachments.ApplyUploadAsync(attachment);
                    if (error is not null) return BadRequest(error);
                }
            }

            return await base.Put(id, data);
        }

        private async Task<bool> WouldCreateCycle(int id, int proposedParentId)
        {
            var visited = new HashSet<int>();
            var currentId = proposedParentId;
            for (var depth = 0; depth < 10000 && visited.Add(currentId); depth++)
            {
                if (currentId == id) return true;
                // IgnoreQueryFilters: a soft-deleted row still carries a parent, and the guard has to
                // see the same rows the tree can reach once somebody reactivates them. Otherwise a
                // cycle through an inactive record is accepted silently and lies dormant.
                var parentId = await _context.Items.IgnoreQueryFilters()
                    .Where(x => x.Id == currentId)
                    .Select(x => x.ParentId)
                    .SingleOrDefaultAsync();
                if (parentId is not int nextId) return false;
                currentId = nextId;
            }
            return true;
        }

        private static void ApplyAttachmentCount(ItemDTO node, Dictionary<int, int> counts)
        {
            ApplyAttachmentCount(node, counts, new HashSet<int>());
        }

        private static void ApplyAttachmentCount(ItemDTO node, Dictionary<int, int> counts, HashSet<int> visited)
        {
            if (!visited.Add(node.Id)) return;
            node.AttachmentCount = counts.TryGetValue(node.Id, out var count) ? count : 0;
            foreach (var child in node.Children)
            {
                ApplyAttachmentCount(child, counts, visited);
            }
        }

        /// <summary>
        /// Deleting a record deletes its files: the UI says so, and orphaned images in the media
        /// folder are impossible to find again once the row that named them is gone.
        /// </summary>
        [HttpDelete("{id:int}")]
        [Authorize(Policy = "RequiereAdmin")]
        public override async Task<IActionResult> Delete(int id)
        {
            var attachments = await _context.Attachments
                .Where(attachment => attachment.ItemId == id)
                .ToListAsync();

            foreach (var attachment in attachments)
            {
                _attachments.Delete(attachment.FilePath);
            }

            if (attachments.Count > 0)
            {
                _context.Attachments.RemoveRange(attachments);
                await _context.SaveChangesAsync();
            }

            return await base.Delete(id);
        }

        [Authorize(Policy = "RequiereLectura")]
        public override async Task<IActionResult> Get()
        {
            try
            {
                if (await base.Get() is OkObjectResult okResult)
                {
                    var data = okResult.Value as List<Item>;
                    var dto = data?.Select(x => new ItemDTO(x)).ToList();

                    // The tree lists files as a count: shipping every thumbnail would put megabytes
                    // in a response that only needs to say "this one has photos".
                    var counts = await _context.Attachments
                        .Where(x => x.IsActive)
                        .GroupBy(x => x.ItemId)
                        .Select(g => new { ItemId = g.Key, Count = g.Count() })
                        .ToDictionaryAsync(x => x.ItemId, x => x.Count);
                    foreach (var node in dto ?? new List<ItemDTO>())
                    {
                        ApplyAttachmentCount(node, counts);
                    }

                    var tree = dto?.Where(x => x.ParentId == null)?.ToList();
                    var panels = data?.Select(x => new ItemDTO(x.Id,x.Label,x.Attributes,x.ParentId, x.Attachments));

                    return Ok(new { tree, panels });
                }
                throw new HttpRequestException();
            }
            catch (Exception)
            {
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }
    }

    #endregion

    #region Attachment

    [Route("api/[controller]")]
    [ApiController]
    //[Authorize]
    public class AttachmentController : BaseController<Attachment>
    {
        IRepository<Attachment> _repository;
        private readonly AttachmentStorage _storage;

        public AttachmentController(IRepository<Attachment> repository, ILogger<BaseController<Attachment>> logger, AttachmentStorage storage) : base(repository, logger)
        {
            _repository = repository;
            _storage = storage;
        }

        [HttpPost]
        [Authorize(Policy = "RequiereEscritura")]
        public override async Task<IActionResult> Post(Attachment data)
        {
            var error = await _storage.ApplyUploadAsync(data);
            if (error is not null) return BadRequest(error);

            return await base.Post(data);
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

            byte[] bytes;
            await using (var buffer = new MemoryStream())
            {
                await file.CopyToAsync(buffer);
                bytes = buffer.ToArray();
            }

            var stored = await _storage.SaveAsync(itemId, fileName ?? file.FileName, bytes);

            var attachment = new Attachment
            {
                ItemId = itemId,
                Type = type ?? "Photo",
                FileName = fileName ?? file.FileName,
                FilePath = stored.RelativePath,
                ThumbnailBase64 = stored.ThumbnailBase64,
            };

            await _repository.Add(attachment);
            return Ok(attachment);
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
                if (!System.IO.File.Exists(absolute)) return NotFound();

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

    #endregion

    #region Constants

    [Route("api/[controller]")]
    [ApiController]
    //[Authorize]
    public class ConstantsController : BaseController<Constants>
    {
        IRepository<Constants> _repository;
        private readonly ApplicationContext _context;
        public ConstantsController(IRepository<Constants> repository, ILogger<BaseController<Constants>> logger, ApplicationContext context) : base(repository, logger)
        {
            _repository = repository;
            _context = context;
        }

        [HttpGet("{id:int}")]
        [AllowAnonymous]
        public override Task<IActionResult> Get(int id) => base.Get(id);

        [HttpPost]
        [Authorize(Policy = "RequiereAdmin")]
        public override Task<IActionResult> Post(Constants data) => base.Post(data);

        [HttpDelete("{id:int}")]
        [Authorize(Policy = "RequiereAdmin")]
        public override Task<IActionResult> Delete(int id) => base.Delete(id);

        [HttpPut("{id:int}")]
        [Authorize(Policy = "RequiereAdmin")]
        public override async Task<IActionResult> Put(int id, Constants data)
        {
            if (data.ParentId is int proposedParentId)
            {
                var visited = new HashSet<int>();
                var currentId = proposedParentId;
                var reachedRoot = false;
                for (var depth = 0; depth < 10000 && visited.Add(currentId); depth++)
                {
                    if (currentId == id)
                        return BadRequest("Un registro no puede ser padre de sí mismo ni de uno de sus descendientes.");
                    var parentId = await _context.Constants.IgnoreQueryFilters()
                        .Where(x => x.Id == currentId)
                        .Select(x => x.ParentId)
                        .SingleOrDefaultAsync();
                    if (parentId is not int nextId)
                    {
                        reachedRoot = true;
                        break;
                    }
                    currentId = nextId;
                }
                if (!reachedRoot)
                    return BadRequest("Un registro no puede ser padre de sí mismo ni de uno de sus descendientes.");
            }

            return await base.Put(id, data);
        }

        [AllowAnonymous]
        public override async Task<IActionResult> Get()
        {
            try
            {
                if (await base.Get() is OkObjectResult okResult)
                {
                    var data = okResult.Value as List<Constants>;
                    var dto = data?.Select(x => new ConstantsDTO(x));

                    var tree = dto?.Where(x => x.ParentId == null)?.ToList();
                    var panels = data?.Select(x => new ConstantsDTO(x.Id, x.Group, x.Label, x.Description, x.SortOrder, x.ParentId, null, new(), x.DataType));

                    return Ok(new { tree,panels});
                }
                throw new HttpRequestException();
            }
            catch (Exception)
            {
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }

        [HttpGet("group/{group}")]
        [AllowAnonymous]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<IActionResult> GetByConstantByGroup(string group)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(group)) return BadRequest();
                var data = await _repository.GetAllByCondition(x => x.Group == group && x.ParentId != null);
                if (data == null || data.Count == 0) return NoContent();
                List<Constants> orderedData;
                if(data.All(x=>x.SortOrder == 0))
                {
                    orderedData = data.OrderBy(x=>x.Label).ToList();
                }
                else
                {
                    orderedData = data.OrderBy(x=>x.SortOrder).ToList();
                }
                    return Ok(orderedData);
            }
            catch (Exception)
            {
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
        }
    }

    #endregion
}