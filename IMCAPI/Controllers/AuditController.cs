using IMCAPI.DB;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IMCAPI.Controllers;

[ApiController]
[Route("api/audit")]
public sealed class AuditController(ApplicationContext context) : ControllerBase
{
    private static readonly HashSet<string> Actions = new(StringComparer.OrdinalIgnoreCase)
    {
        "Create", "Update", "Delete", "SoftDelete", "Login", "LoginFailed", "Logout", "RoleChange",
        "ImpersonateStart", "ImpersonateEnd", "RefreshReuse", "ArchivePurge"
    };

    [Authorize(Policy = "RequiereSuperadmin")]
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? actor = null, [FromQuery] string? entity = null,
        [FromQuery] string? entityId = null, [FromQuery] string? action = null,
        [FromQuery] DateTimeOffset? from = null, [FromQuery] DateTimeOffset? to = null,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize is < 1 or > 200 || (long)(page - 1) * pageSize > int.MaxValue)
            return Problem(statusCode: 400, title: "Invalid audit query", detail: "page must be positive and pageSize must be between 1 and 200.");
        if (from.HasValue && to.HasValue && from.Value > to.Value)
            return Problem(statusCode: 400, title: "Invalid audit query", detail: "from must be earlier than or equal to to.");
        if (!string.IsNullOrWhiteSpace(action))
        {
            var canonicalAction = Actions.FirstOrDefault(candidate => candidate.Equals(action, StringComparison.OrdinalIgnoreCase));
            if (canonicalAction is null)
                return Problem(statusCode: 400, title: "Invalid audit query", detail: "action is not a recognized audit action.");
            action = canonicalAction;
        }

        var query = context.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(actor))
            query = query.Where(row => row.ActorUserId == actor || row.ActorEmail == actor);
        if (!string.IsNullOrWhiteSpace(entity)) query = query.Where(row => row.Entity == entity);
        if (!string.IsNullOrWhiteSpace(entityId)) query = query.Where(row => row.EntityId == entityId);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(row => row.Action == action);
        if (from.HasValue) query = query.Where(row => row.TimestampUtc >= from.Value.UtcDateTime);
        if (to.HasValue) query = query.Where(row => row.TimestampUtc <= to.Value.UtcDateTime);

        // Counting the table on every page is what makes page 20 as expensive as page 1. The total is
        // answered once, on page 1; every page says whether another one exists by fetching one row
        // more than it returns.
        var totalCount = page == 1 ? await query.CountAsync(cancellationToken) : (int?)null;
        var offset = Math.Min((long)(page - 1) * pageSize, int.MaxValue);
        var rows = await query.OrderByDescending(row => row.TimestampUtc).ThenByDescending(row => row.Id)
            .Skip((int)offset).Take(pageSize + 1).ToListAsync(cancellationToken);
        var hasMore = rows.Count > pageSize;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        return Ok(new { items = rows, totalCount, page, pageSize, hasMore });
    }
}
