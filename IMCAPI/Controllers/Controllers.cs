using IMCAPI.DB;
using IMCAPI.DB.Entities;
using IMCAPI.DB.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IMCAPI.Controllers
{
    #region Item

    [Route("api/[controller]")]
    [ApiController]
    //[Authorize]
    public class ItemController : BaseController<Item>
    {
        public ItemController(IRepository<Item> repository, ILogger<BaseController<Item>> logger) : base(repository, logger)
        {
        }

        public override async Task<IActionResult> Get()
        {
            try
            {
                if (await base.Get() is OkObjectResult okResult)
                {
                    var data = okResult.Value as List<Item>;
                    var dto = data?.Select(x => new ItemDTO(x));

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
        public AttachmentController(IRepository<Attachment> repository, ILogger<BaseController<Attachment>> logger) : base(repository, logger)
        {
            _repository = repository;
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
                    var panels = data?.Select(x => new ConstantsDTO(x.Id, x.Group, x.Label, x.Description, x.SortOrder, x.ParentId, null, new()));

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
