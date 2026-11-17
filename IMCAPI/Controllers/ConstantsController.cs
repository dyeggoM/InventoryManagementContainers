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
public class ConstantsController : BaseController<Constants>
{
    IRepository<Constants> _repository;
    private readonly ApplicationContext _context;

    /// <summary>
    /// The group namespace the application is seeded with: the groups of <c>DB/Seed/seed.sql</c>
    /// plus <c>Organization</c>, which the Organizations migration adds. A request may add a value to
    /// one of these groups, never invent a group: an unknown group is a namespace no reader knows,
    /// which is how a rogue organization could have appeared in <c>GET /api/organization</c>.
    /// </summary>
    private static readonly HashSet<string> KnownGroups = new(StringComparer.Ordinal)
    {
        "Amplifiers", "AttachmentType", "Attribute", "Brand", "CONSOLA DIGITAL", "CONSOLAS", "Cables",
        "Category", "Classification", "Cymbals", "Drums", "Estructura", "FieldType", "Hardware",
        "Herramientas", "Instruments", "MEDICION", "Muebles", "Other", "Organization", "Percusion",
        "RF", "Sonido", "Toys", "Type", "Vehiculos",
    };

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
    public override async Task<IActionResult> Post(Constants data)
    {
        // The shared vocabulary is seed data with no organization of its own. Creating a value inside
        // it, or hanging one from it, is not a user choice any organization owns.
        if (string.IsNullOrWhiteSpace(data.Group) || !KnownGroups.Contains(data.Group))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Unknown group",
                Detail = "group must be one of the groups this application knows."
            });
        }

        if (data.Group == "Organization")
        {
            // Organizations are the tenant list itself: one more is a new tenant. Only the role that
            // owns the list may add, rename or deactivate one.
            if (!User.IsInRole("superadmin"))
                return Forbid();
        }
        else if (TenantScope.IsShared(data))
        {
            return SharedVocabularyProblem();
        }

        if (await ValidateParent(data.ParentId) is { } refusal) return refusal;

        return await base.Post(data);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "RequiereAdmin")]
    public override async Task<IActionResult> Delete(int id)
    {
        var persisted = await _context.Constants.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.Id == id)
            .Select(row => new { row.TenantId, row.Group })
            .SingleOrDefaultAsync();

        if (persisted is { TenantId: 0 })
        {
            if (!User.IsInRole("superadmin"))
            {
                // Deactivating an organization is a change to the tenant list; deleting a shared
                // vocabulary row is a change to the application's own definitions. Neither is an
                // organization admin's decision.
                return persisted.Group == "Organization" ? Forbid() : SharedVocabularyProblem();
            }
            // Otherwise the superadmin may clean a shared row up; the repository's lookup is widened
            // for that role, and a row that is not there answers 404 instead of the old 204.
        }

        return await base.Delete(id);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "RequiereAdmin")]
    public override async Task<IActionResult> Put(int id, Constants data)
    {
        if (id != data.Id) return BadRequest();

        var persisted = await _context.Constants.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.Id == id)
            .Select(row => new { row.TenantId, row.Group })
            .SingleOrDefaultAsync();

        // Do not disclose the existence or group of a row outside this organization's boundary.
        if (persisted is null || persisted.TenantId != 0 && persisted.TenantId != _context.CurrentTenantId)
            return NotFound();
        if (string.IsNullOrWhiteSpace(data.Group) || !KnownGroups.Contains(data.Group))
            return BadRequest("group must be one of the groups this application knows.");

        // A group change can turn tenant-owned configuration into an organization or shared seed row.
        // Its original tenant is preserved by the interceptor, so prohibit the transition entirely.
        if (persisted is not null && data.Group != persisted.Group)
            return BadRequest("Changing the group of a constant is not supported.");

        if (persisted is { TenantId: 0 })
        {
            // A persisted row at tenant 0 is seed, not user configuration, so the body cannot rename
            // it. The one exception is the organization list, which the superadmin owns.
            if (persisted.Group != "Organization")
                return SharedVocabularyProblem();
            if (!User.IsInRole("superadmin"))
                return Forbid();
        }

        if (data.ParentId is int proposedParentId)
        {
            // A parent from another organization is not a 404 the cycle walk happens to survive: it
            // is a refusal, named on the field that carries it.
            if (await ValidateParent(proposedParentId) is { } refusal) return refusal;

            var visited = new HashSet<int>();
            var currentId = proposedParentId;
            var reachedRoot = false;
            for (var depth = 0; depth < 10000 && visited.Add(currentId); depth++)
            {
                if (currentId == id)
                    return BadRequest("Un registro no puede ser padre de sí mismo ni de uno de sus descendientes.");
                var parentId = await _context.Constants.IgnoreQueryFilters()
                    .Where(x => x.Id == currentId && (x.TenantId == _context.CurrentTenantId || x.TenantId == 0))
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

    /// <summary>
    /// The proposed parent has to be reachable from this organization: its own rows or the shared
    /// vocabulary. Without this a row hung from another organization's row disappears from its own
    /// tree (the tree reads <c>ParentId == null</c>) and the cycle walk, which runs unfiltered, can
    /// step through the other organization's data.
    /// </summary>
    private async Task<IActionResult?> ValidateParent(int? parentId)
    {
        if (parentId is not int proposed) return null;
        var parent = await _context.Constants.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.Id == proposed)
            .Select(row => new { row.TenantId })
            .SingleOrDefaultAsync();
        if (parent is not null && (parent.TenantId == _context.CurrentTenantId || parent.TenantId == 0))
            return null;
        return BadRequest(new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Invalid parentId",
            Detail = "parentId must name a constant of this organization or of the shared vocabulary."
        });
    }

    private ConflictObjectResult SharedVocabularyProblem() => Conflict(new ProblemDetails
    {
        Status = StatusCodes.Status409Conflict,
        Title = "Shared vocabulary",
        Detail = "This constant is part of the application's vocabulary: it belongs to no organization and cannot be changed from a request."
    });
}
