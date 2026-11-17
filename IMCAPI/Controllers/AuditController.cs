using IMCAPI.DB;
using IMCAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IMCAPI.Controllers;

[ApiController]
[Route("api/audit")]
public sealed class AuditController(ApplicationContext context) : ControllerBase
{
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
            // The vocabulary the writers use, not a copy of it: a name the API cannot write is a 400,
            // and a name it can write is filterable the day it is written.
            var canonicalAction = AuditActions.Canonical(action);
            if (canonicalAction is null)
                return Problem(statusCode: 400, title: "Invalid audit query", detail: "action is not a recognized audit action.");
            action = canonicalAction;
        }

        var query = context.AuditLogs.IgnoreQueryFilters().AsNoTracking().AsQueryable();
        // The model's query filter is `TenantId == CurrentTenantId`, and an anonymous request has no
        // organization claim: Login, LoginFailed, RefreshReuse and role changes are therefore written
        // at tenant 0 and were invisible to every reader. A superadmin reads its own organization
        // plus the system tenant; the row's own `tenantId` is what names a system event, so no
        // second flag is added for the same fact. Any other reader stays inside its organization.
        query = User.IsInRole("superadmin")
            ? query.Where(row => row.TenantId == context.CurrentTenantId || row.TenantId == 0)
            : query.Where(row => row.TenantId == context.CurrentTenantId);
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
