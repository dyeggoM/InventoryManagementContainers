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
        private readonly int _maxDepth;

        public ItemController(IRepository<Item> repository, ILogger<BaseController<Item>> logger, AttachmentStorage attachments,
            ApplicationContext context, IConfiguration configuration) : base(repository, logger)
        {
            _attachments = attachments;
            _context = context;
            var environmentMaxDepth = Environment.GetEnvironmentVariable("ITEMS_MAX_DEPTH");
            _maxDepth = int.TryParse(environmentMaxDepth, out var parsedEnvironmentDepth) && parsedEnvironmentDepth > 0
                ? parsedEnvironmentDepth
                : int.TryParse(configuration["Items:MaxDepth"], out var configuredDepth) && configuredDepth > 0
                    ? configuredDepth
                    : 12;
        }

        [HttpPost]
        [Authorize(Policy = "RequiereEscritura")]
        public override async Task<IActionResult> Post(Item data)
        {
            if (data.ParentId is int parentId)
            {
                var hierarchy = await AnalyzeParentChain(0, parentId);
                if (hierarchy.Cycle)
                    return InvalidHierarchy("The parent assignment creates a cycle.");
                if (!hierarchy.ParentExists)
                    return InvalidHierarchy($"Parent item {parentId} does not exist.");
                var attemptedLevel = hierarchy.ParentLevel + 1;
                if (attemptedLevel > _maxDepth)
                    return DepthExceeded(attemptedLevel);
            }

            // The client may send IsActive, but only delete/restore may control visibility.
            data.IsActive = true;
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
            if (id != data.Id) return BadRequest();
            var existing = await _context.Items.IgnoreQueryFilters().SingleOrDefaultAsync(item => item.Id == id);
            if (existing is null) return NotFound();
            data.IsActive = existing.IsActive;
            var subtreeHeight = await GetSubtreeHeight(id);
            if (data.ParentId is int proposedParentId)
            {
                var hierarchy = await AnalyzeParentChain(id, proposedParentId);
                if (hierarchy.Cycle)
                    return InvalidHierarchy("The parent assignment creates a cycle.");
                if (!hierarchy.ParentExists)
                    return InvalidHierarchy($"Parent item {proposedParentId} does not exist.");
                var rootLevel = hierarchy.ParentLevel + 1;
                var attemptedLevel = rootLevel + subtreeHeight - 1;
                if (attemptedLevel > _maxDepth)
                    return DepthExceeded(attemptedLevel);
            }
            else if (subtreeHeight > _maxDepth)
            {
                return DepthExceeded(subtreeHeight);
            }

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

        private async Task<(bool Cycle, bool ParentExists, int ParentLevel)> AnalyzeParentChain(int itemId, int proposedParentId)
        {
            var visited = new HashSet<int>();
            var currentId = proposedParentId;
            var parentLevel = 0;
            while (visited.Add(currentId))
            {
                if (itemId != 0 && currentId == itemId) return (true, true, parentLevel);
                var parent = await _context.Items.IgnoreQueryFilters().AsNoTracking()
                    .Where(item => item.Id == currentId)
                    .Select(item => new { item.ParentId })
                    .SingleOrDefaultAsync();
                if (parent is null) return (false, false, parentLevel);
                parentLevel++;
                if (parent.ParentId is not int nextId) return (false, true, parentLevel);
                currentId = nextId;
            }
            return (true, true, parentLevel);
        }

        private async Task<int> GetSubtreeHeight(int rootId)
        {
            var items = await _context.Items.IgnoreQueryFilters().AsNoTracking()
                .Select(item => new { item.Id, item.ParentId }).ToListAsync();
            var childrenByParent = items.Where(item => item.ParentId is not null)
                .GroupBy(item => item.ParentId!.Value)
                .ToDictionary(group => group.Key, group => group.Select(item => item.Id).ToArray());
            var pending = new Queue<(int Id, int Depth)>();
            var visited = new HashSet<int> { rootId };
            pending.Enqueue((rootId, 1));
            var height = 1;
            while (pending.TryDequeue(out var current))
            {
                height = Math.Max(height, current.Depth);
                if (!childrenByParent.TryGetValue(current.Id, out var children)) continue;
                foreach (var childId in children)
                    if (visited.Add(childId)) pending.Enqueue((childId, current.Depth + 1));
            }
            return height;
        }

        private BadRequestObjectResult InvalidHierarchy(string detail) => BadRequest(new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Invalid item hierarchy",
            Detail = detail
        });

        private BadRequestObjectResult DepthExceeded(int attemptedLevel) => InvalidHierarchy(
            $"The requested hierarchy would reach level {attemptedLevel}; the maximum allowed depth is {_maxDepth}.");

        private static ObjectResult ChildrenPreventDelete(int count) => new(new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Item has children",
            Detail = $"This item has {count} child item(s). Move or delete them before deleting this item."
        }) { StatusCode = StatusCodes.Status409Conflict };

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
            var item = await _context.Items.IgnoreQueryFilters().SingleOrDefaultAsync(row => row.Id == id);
            if (item is null) return NotFound();

            var attachments = await _context.Attachments.IgnoreQueryFilters()
                .Where(attachment => attachment.ItemId == id).ToListAsync();
            var childCount = await _context.Items.IgnoreQueryFilters()
                .CountAsync(child => child.ParentId == id);
            if (childCount > 0) return ChildrenPreventDelete(childCount);
            var actorId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
            var actorEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
            var now = DateTime.UtcNow;
            var archive = new ItemArchive
            {
                ItemId = item.Id, Label = item.Label, Icon = item.Icon, ParentId = item.ParentId,
                Attributes = item.Attributes, IsActive = item.IsActive, CreatedAt = item.CreatedAt,
                CreatedBy = item.CreatedBy, UpdatedAt = item.UpdatedAt, UpdatedBy = item.UpdatedBy,
                DeletedAtUtc = now, DeletedById = actorId, DeletedBy = actorEmail,
                AttachmentsJson = attachments.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(
                    attachments.Select(attachment => new { attachment.FileName, attachment.Type, attachment.FilePath }))
            };
            var filePaths = attachments.Select(attachment => attachment.FilePath).ToArray();

            await using var transaction = await _context.Database.BeginTransactionAsync();
            _context.ItemArchives.Add(archive);
            _context.Attachments.RemoveRange(attachments);
            _context.Items.Remove(item);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            foreach (var filePath in filePaths) _attachments.Delete(filePath);
            return NoContent();
        }

        [HttpGet("archive")]
        [Authorize(Policy = "RequiereSuperadmin")]
        public async Task<IActionResult> GetArchive([FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to,
            [FromQuery] string? actor, [FromQuery] string? label, [FromQuery] int page = 1,
            [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default)
        {
            if (page < 1 || pageSize < 1 || pageSize > 100 || from > to)
                return BadRequest(new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Invalid archive filters",
                    Detail = "Use page >= 1, pageSize between 1 and 100, and a from value no later than to."
                });

            var query = _context.ItemArchives.AsNoTracking().AsQueryable();
            if (from is not null) query = query.Where(row => row.DeletedAtUtc >= from.Value.UtcDateTime);
            if (to is not null) query = query.Where(row => row.DeletedAtUtc <= to.Value.UtcDateTime);
            if (!string.IsNullOrWhiteSpace(actor))
                query = query.Where(row => row.DeletedById == actor || row.DeletedBy == actor || row.DeletedBy!.Contains(actor));
            if (!string.IsNullOrWhiteSpace(label)) query = query.Where(row => row.Label.Contains(label));
            var totalCount = await query.CountAsync(cancellationToken);
            var items = await query.OrderByDescending(row => row.DeletedAtUtc).ThenByDescending(row => row.ArchiveId)
                .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
            return Ok(new { items, page, pageSize, total = totalCount });
        }

        [HttpPatch("{id:int}/activate")]
        [Authorize(Policy = "RequiereAdmin")]
        public async Task<IActionResult> Activate(int id, CancellationToken cancellationToken)
        {
            var archive = await _context.ItemArchives.Where(row => row.ItemId == id && row.RestoredAtUtc == null)
                .OrderByDescending(row => row.DeletedAtUtc).ThenByDescending(row => row.ArchiveId)
                .FirstOrDefaultAsync(cancellationToken);
            if (archive is null) return NotFound();

            await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
            var idIsFree = !await _context.Items.IgnoreQueryFilters().AnyAsync(item => item.Id == id, cancellationToken);
            var parentId = archive.ParentId;
            if (parentId is not null && !await _context.Items.IgnoreQueryFilters()
                    .AnyAsync(item => item.Id == parentId.Value, cancellationToken))
                parentId = null;

            var restored = new Item
            {
                Id = idIsFree ? archive.ItemId : 0,
                Label = archive.Label, Icon = archive.Icon, ParentId = parentId, Attributes = archive.Attributes,
                IsActive = true, CreatedAt = archive.CreatedAt, CreatedBy = archive.CreatedBy,
                UpdatedAt = archive.UpdatedAt, UpdatedBy = archive.UpdatedBy
            };
            archive.RestoredAtUtc = DateTime.UtcNow;
            archive.RestoredById = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? User.FindFirst("sub")?.Value;
            HttpContext.Items["ItemArchiveRestore"] = true;
            _context.Items.Add(restored);
            await _context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Ok(restored);
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