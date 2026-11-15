using IMCAPI.DB;
using IMCAPI.DB.Entities;
using IMCAPI.DB.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IMCAPI.Controllers;

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
