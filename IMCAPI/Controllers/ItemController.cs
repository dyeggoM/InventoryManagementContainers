using System.Data;
using System.Text.Json;
using System.Text.Json.Serialization;
using IMCAPI.DB;
using IMCAPI.DB.Entities;
using IMCAPI.DB.Repositories;
using IMCAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;

namespace IMCAPI.Controllers;

[Route("api/[controller]")]
[ApiController]
//[Authorize]
public class ItemController : BaseController<Item>
{
    private readonly AttachmentStorage _attachments;
    private readonly ApplicationContext _context;
    private readonly int _maxDepth;
    private readonly AuditLogWriter _audit;
    private readonly IConfiguration _configuration;
    private readonly ArchiveRetention _retention;
    private readonly ILogger<ItemController> _log;

    public ItemController(IRepository<Item> repository, ILogger<BaseController<Item>> logger, AttachmentStorage attachments,
        ApplicationContext context, IConfiguration configuration, AuditLogWriter audit,
        ArchiveRetention retention, ILogger<ItemController> log)
        : base(repository, logger)
    {
        _attachments = attachments;
        _context = context;
        _audit = audit;
        _configuration = configuration;
        _retention = retention;
        _log = log;
        var environmentMaxDepth = Environment.GetEnvironmentVariable("ITEMS_MAX_DEPTH");
        _maxDepth = int.TryParse(environmentMaxDepth, out var parsedEnvironmentDepth) && parsedEnvironmentDepth > 0
            ? parsedEnvironmentDepth
            : int.TryParse(configuration["Items:MaxDepth"], out var configuredDepth) && configuredDepth > 0
                ? configuredDepth
                : 12;
    }

    [HttpPost]
    [Authorize(Policy = "RequiereEscritura")]
    public override Task<IActionResult> Post(Item data) => data.ParentId is null
        ? PostLocked(data)
        : WithHierarchyWriteLock(() => PostLocked(data));

    private async Task<IActionResult> PostLocked(Item data)
    {
        if (data.ParentId is int parentId)
        {
            var hierarchy = await AnalyzeParentChain(0, parentId);
            if (hierarchy.AlreadyLooping)
                return InvalidHierarchy("The parent hierarchy already contains a cycle.");
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
    public override Task<IActionResult> Put(int id, Item data) =>
        WithHierarchyWriteLock(() => PutLocked(id, data));

    private async Task<IActionResult> PutLocked(int id, Item data)
    {
        if (id != data.Id) return BadRequest();
        var existing = await _context.Items.IgnoreQueryFilters().SingleOrDefaultAsync(item => item.Id == id && item.TenantId == _context.CurrentTenantId);
        if (existing is null) return NotFound();
        // The repository updates scalar item fields, not the detached Attachments navigation.
        // Refuse it before writing any file; attachments are added through /api/attachment.
        if (data.Attachments is { Count: > 0 })
            return BadRequest("Attachments cannot be added through item PUT; use /api/attachment.");
        data.IsActive = existing.IsActive;
        var subtreeHeight = GetSubtreeHeight(id, await LoadChildrenByParent(), new Dictionary<int, int>(), new HashSet<int>(), _maxDepth);
        if (data.ParentId is int proposedParentId)
        {
            var hierarchy = await AnalyzeParentChain(id, proposedParentId);
            if (hierarchy.AlreadyLooping)
                return InvalidHierarchy("The parent hierarchy already contains a cycle.");
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

        return await base.Put(id, data);
    }

    public sealed record MoveChildrenRequest(int[]? Ids);

    [HttpPost("{id:int}/children")]
    [Authorize(Policy = "RequiereEscritura")]
    public Task<IActionResult> MoveChildren(int id, [FromBody] MoveChildrenRequest request) =>
        WithHierarchyWriteLock(() => MoveChildrenLocked(id, request));

    private async Task<IActionResult> MoveChildrenLocked(int id, MoveChildrenRequest request)
    {
        if (!await _context.Items.AnyAsync(item => item.Id == id)) return NotFound();
        if (request.Ids is null || request.Ids.Length == 0)
            return InvalidHierarchy("Supply at least one child id.");

        var children = await _context.Items.Where(item => request.Ids.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id);
        var childrenByParent = await LoadChildrenByParent();
        var heights = new Dictionary<int, int>();
        var visited = new HashSet<int>();
        foreach (var childId in request.Ids)
        {
            if (childId == id || !visited.Add(childId) || !children.ContainsKey(childId))
                return InvalidHierarchy($"Child item {childId} does not exist, is repeated, or is the parent.");

            var hierarchy = await AnalyzeParentChain(childId, id);
            if (hierarchy.AlreadyLooping)
                return InvalidHierarchy("The parent hierarchy already contains a cycle.");
            if (hierarchy.Cycle)
                return InvalidHierarchy($"Child item {childId} would create a cycle.");
            var attemptedLevel = hierarchy.ParentLevel + GetSubtreeHeight(childId, childrenByParent, heights, new HashSet<int>(), _maxDepth);
            if (attemptedLevel > _maxDepth)
                return DepthExceeded(attemptedLevel);
        }

        // Attaching is a move: report where every piece came from, including roots.
        var reparented = request.Ids.Select(childId => new { id = childId, @from = children[childId].ParentId }).ToArray();
        foreach (var child in children.Values)
        {
            child.ParentId = id;
            child.UpdatedAt = DateTime.UtcNow;
        }
        await _context.SaveChangesAsync();
        // The API's global JSON options omit nulls; roots must explicitly report "from": null.
        return new JsonResult(new { moved = children.Count, reparented }, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never
        });
    }

    [HttpDelete("{id:int}/children/{childId:int}")]
    [Authorize(Policy = "RequiereEscritura")]
    public async Task<IActionResult> DetachChild(int id, int childId)
    {
        if (!await _context.Items.AnyAsync(item => item.Id == id)) return NotFound();
        var child = await _context.Items.SingleOrDefaultAsync(item => item.Id == childId && item.ParentId == id);
        if (child is null) return NotFound();

        child.ParentId = null;
        child.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    private async Task<(bool Cycle, bool AlreadyLooping, bool ParentExists, int ParentLevel)> AnalyzeParentChain(int itemId, int proposedParentId)
    {
        var visited = new HashSet<int>();
        var currentId = proposedParentId;
        var parentLevel = 0;
        while (visited.Add(currentId))
        {
            if (itemId != 0 && currentId == itemId) return (true, false, true, parentLevel);
            var parent = await _context.Items.IgnoreQueryFilters().AsNoTracking()
                .Where(item => item.Id == currentId && item.TenantId == _context.CurrentTenantId)
                .Select(item => new { item.ParentId })
                .SingleOrDefaultAsync();
            if (parent is null) return (false, false, false, parentLevel);
            parentLevel++;
            if (parent.ParentId is not int nextId) return (false, false, true, parentLevel);
            currentId = nextId;
        }
        return (false, true, true, parentLevel);
    }

    private async Task<Dictionary<int, int[]>> LoadChildrenByParent()
    {
        var items = await _context.Items.AsNoTracking()
            .Select(item => new { item.Id, item.ParentId }).ToListAsync();
        return items.Where(item => item.ParentId is not null)
            .GroupBy(item => item.ParentId!.Value)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Id).ToArray());
    }

    private static int GetSubtreeHeight(int rootId, Dictionary<int, int[]> childrenByParent,
        Dictionary<int, int> heights, HashSet<int> visiting, int maxDepth)
    {
        if (heights.TryGetValue(rootId, out var cached)) return cached;
        if (!visiting.Add(rootId)) return 0; // Historical corrupt cycles cannot recurse forever.
        if (visiting.Count > maxDepth)
        {
            visiting.Remove(rootId);
            return maxDepth + 1; // A corrupted/imported chain cannot overflow the process stack.
        }
        var height = 1;
        if (childrenByParent.TryGetValue(rootId, out var children))
            foreach (var childId in children)
                height = Math.Max(height, 1 + GetSubtreeHeight(childId, childrenByParent, heights, visiting, maxDepth));
        visiting.Remove(rootId);
        return heights[rootId] = height;
    }

    private async Task<IActionResult> WithHierarchyWriteLock(Func<Task<IActionResult>> action)
    {
        // SQLite begins serializable transactions IMMEDIATE: the writer lock precedes all hierarchy reads.
        for (var attempt = 0; ; attempt++)
        {
            await _context.Database.OpenConnectionAsync();
            try
            {
                await _context.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout = 500");
                await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                var result = await action();
                if (result is not ObjectResult { StatusCode: >= 500 })
                    await tx.CommitAsync();
                return result;
            }
            catch (Exception ex) when (IsSqliteBusy(ex) && attempt < 5)
            {
                _context.ChangeTracker.Clear();
                await Task.Delay(25 * (attempt + 1));
            }
            finally
            {
                await _context.Database.CloseConnectionAsync();
            }
        }
    }

    private static bool IsSqliteBusy(Exception ex) =>
        ex is SqliteException sqlite && sqlite.SqliteErrorCode is 5 or 6
        || ex.InnerException is not null && IsSqliteBusy(ex.InnerException);

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

    /// <summary>One attachment exactly as the archive recorded it: enough to write the row again.</summary>
    private sealed record ArchivedAttachment(string? FileName, string? Type, string? FilePath);

    private static readonly System.Text.Json.JsonSerializerOptions ArchivedJson = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Reads the attachment list a deleted record left in the ledger. A row written by an older
    /// shape must not make the record unrecoverable, so a payload that cannot be read is treated
    /// as "no attachments" rather than as an error.
    /// </summary>
    private static List<ArchivedAttachment> ReadArchivedAttachments(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<ArchivedAttachment>();
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<ArchivedAttachment>>(json, ArchivedJson)
                ?? new List<ArchivedAttachment>();
        }
        catch (System.Text.Json.JsonException)
        {
            return new List<ArchivedAttachment>();
        }
    }

    /// <summary>
    /// Deleting a record archives it and moves its files aside: they wait in <c>media/trash</c>
    /// until the archive row is purged, which is what makes a restore bring the attachments
    /// back. Destroying them here would leave the ledger naming files nobody can find again.
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = "RequiereAdmin")]
    public override async Task<IActionResult> Delete(int id)
    {
        string?[]? filePaths = null;
        try
        {
            var result = await WithHierarchyWriteLock(async () =>
            {
                var (response, paths) = await DeleteLocked(id);
                filePaths = paths;
                return response;
            });
            // The database is committed before moving files; a rejected delete never touches trash.
            if (filePaths is not null) _attachments.MoveToTrash(filePaths);
            return result;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteExtendedErrorCode: 787 })
        {
            // Only an Item's children justify this 409; other FK failures remain real errors.
            _context.ChangeTracker.Clear();
            var childCount = await _context.Items.IgnoreQueryFilters()
                .CountAsync(child => child.ParentId == id && child.TenantId == _context.CurrentTenantId);
            if (childCount > 0) return ChildrenPreventDelete(childCount);
            throw;
        }
    }

    private async Task<(IActionResult Response, string?[]? FilePaths)> DeleteLocked(int id)
    {
        var item = await _context.Items.IgnoreQueryFilters().SingleOrDefaultAsync(row => row.Id == id && row.TenantId == _context.CurrentTenantId);
        if (item is null) return (NotFound(), null);

        var attachments = await _context.Attachments.IgnoreQueryFilters()
            .Where(attachment => attachment.ItemId == id && attachment.TenantId == _context.CurrentTenantId).ToListAsync();
        var childCount = await _context.Items.IgnoreQueryFilters()
            .CountAsync(child => child.ParentId == id && child.TenantId == _context.CurrentTenantId);
        if (childCount > 0) return (ChildrenPreventDelete(childCount), null);
        var actorId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        var actorEmail = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
        var now = DateTime.UtcNow;
        var archive = new ItemArchive
        {
            TenantId = _context.CurrentTenantId,
            ItemId = item.Id, Label = item.Label, Icon = item.Icon, ParentId = item.ParentId,
            Attributes = item.Attributes, IsActive = item.IsActive, CreatedAt = item.CreatedAt,
            CreatedBy = item.CreatedBy, UpdatedAt = item.UpdatedAt, UpdatedBy = item.UpdatedBy,
            DeletedAtUtc = now, DeletedById = actorId, DeletedBy = actorEmail,
            AttachmentsJson = attachments.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(
                attachments.Select(attachment => new { attachment.FileName, attachment.Type, attachment.FilePath }))
        };
        var filePaths = attachments.Select(attachment => attachment.FilePath).ToArray();

        _context.ItemArchives.Add(archive);
        _context.Attachments.RemoveRange(attachments);
        _context.Items.Remove(item);
        await _context.SaveChangesAsync();
        return (NoContent(), filePaths);
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
        // The pager needs the total once. Counting the whole table on every page makes a deep page
        // as expensive as the first, so page 1 returns it, later pages return null, and every page
        // says whether another one exists by asking the database for one row more than it shows.
        var total = page == 1 ? await query.CountAsync(cancellationToken) : (int?)null;
        // `(page - 1) * pageSize` overflows int for a large enough page and silently wrapped back to
        // the first one; compute it wide and clamp instead of trusting the arithmetic.
        var offset = Math.Min((long)(page - 1) * pageSize, int.MaxValue);
        var rows = await query.OrderByDescending(row => row.DeletedAtUtc).ThenByDescending(row => row.ArchiveId)
            .Skip((int)offset).Take(pageSize + 1).ToListAsync(cancellationToken);
        var hasMore = rows.Count > pageSize;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        return Ok(new { items = rows, page, pageSize, total, hasMore });
    }

    /// <summary>
    /// Removes archive rows older than the retention age and destroys the files those rows were
    /// holding in <c>media/trash</c>. It never touches a live record: only the ledger rows this
    /// purge deletes lose their files, which is exactly what a purge is for.
    /// </summary>
    [HttpPost("archive/purge")]
    [Authorize(Policy = "RequiereSuperadmin")]
    public async Task<IActionResult> PurgeArchive([FromQuery] int? olderThanDays, CancellationToken cancellationToken = default)
    {
        // The age comes from the request, the environment, the configuration, and finally the
        // default of one month — the resolution lives in ArchiveRetention so the daily sweep and this
        // endpoint cannot disagree about it.
        var age = ArchiveRetention.ResolveDays(olderThanDays, _configuration);
        if (age < 1)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Archive retention is disabled",
                Detail = "Pass olderThanDays, or set Archive:Retention:Days (ARCHIVE_RETENTION_DAYS) above zero."
            });
        }

        var cutoff = DateTime.UtcNow.AddDays(-age);
        var actorId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        var result = await _retention.PurgeAsync(cutoff, age, actorId ?? "superadmin", cancellationToken);

        return Ok(new { deleted = result.Deleted, cutoffUtc = result.CutoffUtc, olderThanDays = result.OlderThanDays });
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
                .AnyAsync(item => item.Id == parentId.Value && item.TenantId == _context.CurrentTenantId, cancellationToken))
            parentId = null;
        // The row was a leaf when it was archived, but its parent may have been moved deeper in the
        // meantime: restoring it must obey the same depth bound that create and update enforce.
        if (parentId is not null)
        {
            var hierarchy = await AnalyzeParentChain(archive.ItemId, parentId.Value);
            if (hierarchy.Cycle) return InvalidHierarchy("The restore would create a cycle.");
            var attemptedLevel = hierarchy.ParentLevel + 1;
            if (attemptedLevel > _maxDepth) return DepthExceeded(attemptedLevel);
        }

        var restored = new Item
        {
            TenantId = _context.CurrentTenantId,
            Id = idIsFree ? archive.ItemId : 0,
            Label = archive.Label, Icon = archive.Icon, ParentId = parentId, Attributes = archive.Attributes,
            IsActive = true, CreatedAt = archive.CreatedAt, CreatedBy = archive.CreatedBy,
            UpdatedAt = archive.UpdatedAt, UpdatedBy = archive.UpdatedBy
        };
        archive.RestoredAtUtc = DateTime.UtcNow;
        archive.RestoredById = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;
        HttpContext.Items["ItemArchiveRestore"] = true;

        // Delete removed the attachment rows and moved the files to the trash, so a restore that
        // only recreated the record would hand it back with its photos silently gone. The files
        // come back first: a row naming a file nobody could return is a row that answers 404.
        var archivedAttachments = ReadArchivedAttachments(archive.AttachmentsJson);
        var storedPaths = archivedAttachments
            .Select(attachment => attachment.FilePath)
            .Where(filePath => !string.IsNullOrWhiteSpace(filePath))
            .Select(filePath => filePath!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var missing = _attachments.RestoreFromTrash(storedPaths);
        var returned = storedPaths.Except(missing, StringComparer.Ordinal).ToArray();

        var attachments = archivedAttachments
            .Where(attachment => string.IsNullOrWhiteSpace(attachment.FilePath)
                || returned.Contains(attachment.FilePath, StringComparer.Ordinal))
            .Select(attachment => new Attachment
            {
                TenantId = _context.CurrentTenantId,
                ItemId = restored.Id,
                FileName = attachment.FileName ?? string.Empty,
                Type = attachment.Type ?? "Photo",
                FilePath = attachment.FilePath,
                IsActive = true,
            })
            .ToList();

        try
        {
            _context.Items.Add(restored);
            // The id is only known once the row is written: a restore whose original id was taken
            // gets a new one, and the attachments have to point at the record that exists.
            await _context.SaveChangesAsync(cancellationToken);
            if (attachments.Count > 0)
            {
                foreach (var attachment in attachments) attachment.ItemId = restored.Id;
                _context.Attachments.AddRange(attachments);
                await _context.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            // The record did not come back, so neither should its files: back to the trash they
            // came from, instead of leaving them in a live tree no row points at.
            _attachments.MoveToTrash(returned);
            throw;
        }

        Response.Headers["X-Restored-Attachments"] = attachments.Count.ToString();
        if (missing.Count > 0)
        {
            Response.Headers["X-Missing-Attachments"] = missing.Count.ToString();
            _log.LogWarning("Restored item {ItemId} without {MissingCount} attachment file(s): {MissingPaths}",
                restored.Id, missing.Count, string.Join(", ", missing));
        }

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
