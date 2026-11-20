using IMCAPI.DB;
using IMCAPI.DB.Entities;
using IMCAPI.DB.Repositories;
using IMCAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

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

        // The tenant the repository will stamp: the shared vocabulary lives at 0, everything else
        // in the caller's organization (BaseRepository.Add).
        var tenant = TenantScope.IsShared(data) ? 0 : _context.CurrentTenantId;
        var existing = await _context.Constants.IgnoreQueryFilters()
            .Where(row => row.TenantId == tenant && row.Group == data.Group)
            .Select(row => new { row.Code, row.Label })
            .ToListAsync();
        var taken = existing.Select(row => row.Code).ToList();

        // Identity, assigned once: the client may name the code on creation, otherwise the server
        // derives it from the label. Either way it never changes afterwards — a rename of Label
        // leaves every stored attribute key and list value readable.
        if (string.IsNullOrWhiteSpace(data.Code))
        {
            // A generated code is stable: it is written once and never recomputed, so a suffix
            // added here stays with the row for good.
            var seed = data.Label.Trim();
            if (seed.Length == 0) seed = "Constant";
            var used = new HashSet<string>(taken, StringComparer.Ordinal);
            data.Code = seed;
            if (!used.Add(seed))
            {
                for (var suffix = 2; ; suffix++)
                {
                    var candidate = $"{seed} {suffix}";
                    if (used.Add(candidate))
                    {
                        data.Code = candidate;
                        break;
                    }
                }
            }
        }
        else
        {
            data.Code = data.Code.Trim();
            if (taken.Contains(data.Code, StringComparer.Ordinal))
                return Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Duplicate code",
                    Detail = $"A constant with code '{data.Code}' already exists in group '{data.Group}'."
                });
        }

        // With code and label independent, two codes may not share a label either: the
        // (TenantId, Group, Label) index enforces it, so the refusal comes before the write.
        if (existing.Any(row => row.Label == data.Label))
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Duplicate label",
                Detail = $"A constant with label '{data.Label}' already exists in group '{data.Group}'."
            });

        // Check-then-write is a race window: a second request naming the same code lands on the
        // unique index. Only those two catalog indexes translate into the conflict the sequential
        // path answers; every other database failure stays what it always was.
        try
        {
            await _repository.Add(data);
            return Ok(data);
        }
        catch (DbUpdateException e) when (CatalogIndex(e) is { } index)
        {
            var label = index == CatalogIndexKind.Code;
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = label ? "Duplicate code" : "Duplicate label",
                Detail = label
                    ? $"A constant with code '{data.Code}' already exists in group '{data.Group}'."
                    : $"A constant with label '{data.Label}' already exists in group '{data.Group}'."
            });
        }
    }

    private enum CatalogIndexKind { Code, Label }

    /// <summary>The two unique indexes the catalog owns, and only those.</summary>
    private static CatalogIndexKind? CatalogIndex(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message ?? exception.Message;
        if (!message.Contains("UNIQUE constraint failed", StringComparison.Ordinal)) return null;
        if (message.Contains("IX_Constants_TenantId_Group_Code", StringComparison.Ordinal)
            || message.Contains("Constants.TenantId", StringComparison.Ordinal)
                && message.Contains("Constants.Group", StringComparison.Ordinal)
                && message.Contains("Constants.Code", StringComparison.Ordinal))
            return CatalogIndexKind.Code;
        if (message.Contains("IX_Constants_TenantId_Group_Label", StringComparison.Ordinal)
            || message.Contains("Constants.TenantId", StringComparison.Ordinal)
                && message.Contains("Constants.Group", StringComparison.Ordinal)
                && message.Contains("Constants.Label", StringComparison.Ordinal))
            return CatalogIndexKind.Label;
        return null;
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "RequiereAdmin")]
    public override async Task<IActionResult> Delete(int id)
    {
        var persisted = await _context.Constants.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.Id == id)
            .Select(row => new { row.TenantId, row.Group, row.Code, row.Label })
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
        else if (persisted is not null)
        {
            // Deleting a code some stored item still names leaves every item carrying it unable to
            // save again and no way back through the API (recreating the row collides with the
            // indexes this row still occupies). Refuse the delete while anything that can come
            // back names the code: live items, and archives still pending a restore. A trashed row
            // does not count by itself — its recoverable form is the pending archive, which does;
            // an inactive row whose archive is already gone is dead data and must not keep the
            // code hostage for ever.
            var usage = await CountCodeUsageAsync(persisted.TenantId, persisted.Code, persisted.Label);
            if (usage is not null && (usage.Value.Item1 > 0 || usage.Value.Item2 > 0))
            {
                var parts = new List<string>();
                if (usage.Value.Item1 > 0) parts.Add($"{usage.Value.Item1} item(s) via attribute '{usage.Value.Item3}'");
                if (usage.Value.Item2 > 0) parts.Add($"{usage.Value.Item2} archived item(s) pending restore via attribute '{usage.Value.Item3}'");
                return Conflict(new ProblemDetails
                {
                    Status = StatusCodes.Status409Conflict,
                    Title = "Code in use",
                    Detail = $"The code '{persisted.Code}' is still used by {string.Join(" and ", parts)}. " +
                        "Delete or edit those items first."
                });
            }
        }

        return await base.Delete(id);
    }

    /// <summary>
    /// Where a constant's code is still written and could still come back: live items of the same
    /// organization whose attribute bag names it — as a key, when the constant is an attribute, or
    /// as a list value — and archives pending a restore, which would bring the same bag back. The
    /// prefilter is a substring over <c>Attributes</c>; the verdict comes from parsing the JSON,
    /// comparing code and label without case (the same tolerance the item validator and the
    /// importer use), so a substring hit that is not the value does not block anything. Answers
    /// (items, pending archives, last attribute seen) or null when nothing names the code.
    /// </summary>
    private async Task<(int Items, int Archives, string Attribute)?> CountCodeUsageAsync(
        int tenantId, string code, string label)
    {
        var needles = new[] { code, label }
            .Where(needle => !string.IsNullOrWhiteSpace(needle))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (needles.Length == 0) return null;
        var attribute = code;

        var itemRows = await _context.Items.IgnoreQueryFilters().AsNoTracking()
            .Where(item => item.TenantId == tenantId && item.IsActive && item.Attributes != null
                && needles.Any(needle => item.Attributes.Contains(needle)))
            .Select(item => item.Attributes)
            .ToListAsync();
        var archiveRows = await _context.ItemArchives.AsNoTracking()
            .Where(archive => archive.TenantId == tenantId && archive.RestoredAtUtc == null
                && archive.Attributes != null
                && needles.Any(needle => archive.Attributes.Contains(needle)))
            .Select(archive => archive.Attributes)
            .ToListAsync();

        var usedByItems = 0;
        var usedByArchives = 0;
        foreach (var attributes in itemRows)
            if (NamesConstant(attributes!, needles, out var attributeName))
            {
                usedByItems++;
                attribute = attributeName;
            }
        foreach (var attributes in archiveRows)
            if (NamesConstant(attributes!, needles, out var archiveAttribute))
            {
                usedByArchives++;
                attribute = archiveAttribute;
            }

        return usedByItems + usedByArchives > 0 ? (usedByItems, usedByArchives, attribute) : null;
    }

    /// <summary>Does an attribute bag name one of the needles, and under which attribute?</summary>
    private static bool NamesConstant(string attributes, string[] needles, out string attribute)
    {
        attribute = needles[0];
        try
        {
            using var document = JsonDocument.Parse(attributes);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String) continue;
                var value = property.Value.GetString() ?? string.Empty;
                if (needles.Any(needle => string.Equals(needle, value, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(needle, property.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    attribute = property.Name;
                    return true;
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON, not a name the validator could read either: it does not block the delete.
        }
        return false;
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "RequiereAdmin")]
    public override async Task<IActionResult> Put(int id, Constants data)
    {
        if (id != data.Id) return BadRequest();

        var persisted = await _context.Constants.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.Id == id)
            .Select(row => new { row.TenantId, row.Group, row.Code })
            .SingleOrDefaultAsync();

        // Do not disclose the existence or group of a row outside this organization's boundary.
        if (persisted is null || persisted.TenantId != 0 && persisted.TenantId != _context.CurrentTenantId)
            return NotFound();
        if (string.IsNullOrWhiteSpace(data.Group) || !KnownGroups.Contains(data.Group))
            return BadRequest("group must be one of the groups this application knows.");

        // The code is the identity this row was created with: a rename changes Label only, so every
        // stored attribute key and list value keeps resolving. A client that echoes or changes the
        // code cannot move it.
        data.Code = persisted.Code;

        // A rename may not take a label the group already carries: the (TenantId, Group, Label)
        // index would refuse it at the database, so the conflict comes before the write.
        if (await _context.Constants.IgnoreQueryFilters()
                .AnyAsync(row => row.TenantId == persisted.TenantId && row.Group == data.Group
                    && row.Label == data.Label && row.Id != id))
            return Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Duplicate label",
                Detail = $"A constant with label '{data.Label}' already exists in group '{data.Group}'."
            });

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
                var panels = data?.Select(x => new ConstantsDTO(x.Id, x.Group, x.Label, x.Description, x.SortOrder, x.ParentId, null, new(), x.DataType, x.Code));

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
