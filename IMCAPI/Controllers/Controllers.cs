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
        public override async Task<IActionResult> Put(int id, Item data)
        {
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

        private static void ApplyAttachmentCount(ItemDTO node, Dictionary<int, int> counts)
        {
            node.AttachmentCount = counts.TryGetValue(node.Id, out var count) ? count : 0;
            foreach (var child in node.Children)
            {
                ApplyAttachmentCount(child, counts);
            }
        }

        /// <summary>
        /// Deleting a record deletes its files: the UI says so, and orphaned images in the media
        /// folder are impossible to find again once the row that named them is gone.
        /// </summary>
        [HttpDelete("{id:int}")]
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
        public override async Task<IActionResult> Post(Attachment data)
        {
            var error = await _storage.ApplyUploadAsync(data);
            if (error is not null) return BadRequest(error);

            return await base.Post(data);
        }

        [HttpDelete("{id:int}")]
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
        public ConstantsController(IRepository<Constants> repository, ILogger<BaseController<Constants>> logger) : base(repository, logger)
        {
            _repository = repository;
        }


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