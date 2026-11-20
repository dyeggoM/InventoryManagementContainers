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
using Microsoft.Extensions.Options;

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
    private readonly IOptions<JsonOptions> _json;

    public ItemController(IRepository<Item> repository, ILogger<BaseController<Item>> logger, AttachmentStorage attachments,
        ApplicationContext context, IConfiguration configuration, AuditLogWriter audit,
        ArchiveRetention retention, ILogger<ItemController> log,
        IOptions<JsonOptions> json)
        : base(repository, logger)
    {
        _attachments = attachments;
        _context = context;
        _audit = audit;
        _configuration = configuration;
        _retention = retention;
        _log = log;
        _json = json;
        // Same resolution as the importer and the serializer's depth: one bound, three readers.
        _maxDepth = ItemHierarchyLimits.ResolveMaxDepth(configuration);
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

        // Attributes are catalog data: keys are constant codes and list values are codes too.
        if (await NormalizeAttributesAsync(data) is { } attributeRefusal)
            return attributeRefusal;

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
        // Attributes absent from the body are not an emptying: PUT is read-modify-write, so a
        // client that does not send the bag keeps the one the item already carries. It is also the
        // obvious way around the attribute validation — a null bag must not slip past it.
        if (data.Attributes is null) data.Attributes = existing.Attributes;
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

        // Same contract on update: an unknown code is refused before anything is written, and a
        // value spelled as a label is stored as its code.
        if (await NormalizeAttributesAsync(data) is { } attributeRefusal)
            return attributeRefusal;

        return await base.Put(id, data);
    }

    /// <summary>
    /// Attribute keys that predate the catalog contract and carry free values: the importer writes
    /// them and <see cref="Services.ItemImporter.ReadCode"/> reads the item's code back from
    /// "Code", so they are accepted beside the catalog-driven keys, never inside one.
    /// </summary>
    private static readonly HashSet<string> LegacyAttributeKeys = new(StringComparer.Ordinal)
    {
        "Code", "Reference", "Serial", "Dimensions",
    };

    /// <summary>
    /// The code/label contract on item writes: every key must be the code of an Attribute constant
    /// (or one of the legacy keys), and a list value must resolve to a constant in the value group
    /// the key names. A value spelled as a label — or in any case — is normalized to the code and
    /// stored as the code, the same resolution the importer applies, so renaming or translating a
    /// label never leaves an item unsaveable. An unknown code is a 400 that names it; the item is
    /// not written.
    /// </summary>
    private async Task<IActionResult?> NormalizeAttributesAsync(Item data)
    {
        var (refusal, normalized) = await ResolveAttributesAsync(data.Attributes);
        if (refusal is not null) return refusal;
        data.Attributes = normalized;
        return null;
    }

    /// <summary>
    /// Validates one attribute bag and answers the normalized form to store (labels resolved to
    /// codes, everything else untouched) beside the refusal, if any. Answers (null, original) when
    /// there is nothing to do.
    /// </summary>
    private async Task<(IActionResult? Refusal, string Attributes)> ResolveAttributesAsync(string? attributes)
    {
        if (string.IsNullOrWhiteSpace(attributes)) return (null, attributes ?? "");
        Dictionary<string, string> parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(attributes,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }
        catch (JsonException)
        {
            return (BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid attributes",
                Detail = "attributes must be a JSON object of string values."
            }), attributes);
        }

        var catalog = await _context.Constants.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.TenantId == _context.CurrentTenantId && row.IsActive)
            .Select(row => new { row.Code, row.Label, row.Group, row.ParentId })
            .ToListAsync();
        // Attributes are tenant-owned here: the seed copies the vocabulary into every organization.
        var attributeDefs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in catalog.Where(row => row.Group == "Attribute"))
            // Keys are codes, period: accepting the display label here would couple stored data to
            // the language of the day, which is exactly what the code/label contract removes.
            attributeDefs.TryAdd(row.Code, row.Code);
        var valuesByGroup = catalog
            // The one row whose code names its group is the group's root, the shelf the values sit
            // on — not a value itself, in the importer's catalogue either.
            .Where(row => row.Code != row.Group)
            .GroupBy(row => row.Group!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        foreach (var (key, value) in parsed)
        {
            if (!attributeDefs.TryGetValue(key, out var attributeCode))
            {
                if (LegacyAttributeKeys.Contains(key)) continue;
                return (BadRequest(new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Unknown attribute",
                    Detail = $"Attribute '{key}' does not exist in the Attribute catalog."
                }), attributes);
            }

            if (string.IsNullOrWhiteSpace(value) || !valuesByGroup.TryGetValue(attributeCode, out var group))
                continue; // a free-text, number or date attribute: the catalog does not constrain it

            // Same tolerance as the importer — code or label, without case — and always stored as
            // the code, so renaming or translating a label never leaves an item unsaveable.
            var code = group.Where(row => string.Equals(row.Code, value, StringComparison.OrdinalIgnoreCase))
                .Select(row => row.Code).FirstOrDefault()
                ?? group.Where(row => string.Equals(row.Label, value, StringComparison.OrdinalIgnoreCase))
                    .Select(row => row.Code).FirstOrDefault();
            if (code is null)
                return (BadRequest(new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Unknown value",
                    Detail = $"Value '{value}' is not a code of attribute '{attributeCode}'."
                }), attributes);
            if (!string.Equals(code, value, StringComparison.Ordinal)) parsed[key] = code;
        }

        return (null, JsonSerializer.Serialize(parsed));
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
    public Task<IActionResult> DetachChild(int id, int childId) =>
        WithHierarchyWriteLock(() => DetachChildLocked(id, childId));

    private async Task<IActionResult> DetachChildLocked(int id, int childId)
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
        //
        // This used to run `PRAGMA busy_timeout = 500` and promise a 500 ms wait. It promised nothing:
        // that pragma is SQLite's busy handler, and it is not what a `BEGIN IMMEDIATE` waits on. The
        // provider (Microsoft.Data.Sqlite) retries the statement itself, bounded by
        // SqliteConnection.DefaultTimeout — 30 s by default. Measured: an external writer held the lock
        // for 4 s while the pragma said 500 ms, and BEGIN IMMEDIATE came back after 4020 ms; the audit's
        // 11.8 s wait is the same thing on a 12 s hold. The knob that actually bounds it is the
        // connection's default timeout, so that is what is set here (and restored when the lock is
        // released) two seconds per attempt; the retry loop below multiplies that into the budget
        // that covers a longer contention instead of turning it into a 500 after half a minute.
        var sqlite = _context.Database.GetDbConnection() as SqliteConnection;
        var previousConnectionTimeout = sqlite?.DefaultTimeout;
        if (sqlite is not null) sqlite.DefaultTimeout = HierarchyLockAttemptSeconds;
        var previousCommandTimeout = _context.Database.GetCommandTimeout();
        _context.Database.SetCommandTimeout(HierarchyCommandTimeoutSeconds);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                await _context.Database.OpenConnectionAsync();
                try
                {
                    await using var tx = await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
                    var result = await action();
                    if (result is not ObjectResult { StatusCode: >= 500 })
                        await tx.CommitAsync();
                    return result;
                }
                catch (Exception ex) when (IsSqliteBusy(ex) && attempt < HierarchyLockAttempts - 1)
                {
                    _context.ChangeTracker.Clear();
                    await Task.Delay(HierarchyLockRetryDelayMilliseconds * (attempt + 1));
                }
                finally
                {
                    await _context.Database.CloseConnectionAsync();
                }
            }
        }
        finally
        {
            if (sqlite is not null && previousConnectionTimeout is int restore) sqlite.DefaultTimeout = restore;
            _context.Database.SetCommandTimeout(previousCommandTimeout);
        }
    }

    /// <summary>
    /// How long one attempt waits on another writer before giving up and retrying. It is the
    /// connection's default timeout, which is what Microsoft.Data.Sqlite uses for its own retry of a
    /// statement that hits a held lock (`BEGIN IMMEDIATE` waited 4020 ms under a 4 s hold); the
    /// retry loop below multiplies it into the total budget, it does not extend it.
    /// </summary>
    private const int HierarchyLockAttemptSeconds = 2;

    /// <summary>
    /// How many attempts the retry loop makes before giving up: 10 x 2 s plus the short back-off
    /// between attempts, so the whole budget is a bit over 20 s. It is sized to cover a short
    /// backup (or checkpoint) and a large bulk move holding SQLite's single writer lock — measured:
    /// a 5000-item move held it for ~4 s, and a checkpoint can hold it for tens of seconds. Past
    /// the budget the caller gets an honest 500 instead of hanging forever; waiting longer than
    /// this would mean the request lives as long as the longest possible backup, which no HTTP
    /// client behind a proxy tolerates anyway.
    /// </summary>
    private const int HierarchyLockAttempts = 10;

    /// <summary>Short, growing back-off between lock attempts; the per-attempt wait does the waiting.</summary>
    private const int HierarchyLockRetryDelayMilliseconds = 25;

    /// <summary>
    /// How long a single command inside the hierarchy write may execute on its own before being
    /// aborted. This is deliberately NOT the lock-wait budget and must not share its number: the
    /// lock wait is about somebody else holding the writer lock (bounded by
    /// HierarchyLockAttempts x HierarchyLockAttemptSeconds), while this bounds one statement's own
    /// execution. It used to be 1 s — the same number as the per-attempt lock wait, a coincidence
    /// that left no margin: a measured 5000-item move answered in 3973 ms with every individual
    /// command under 1 s, so any slower machine would abort a legitimate move. 15 s keeps a
    /// generous per-command ceiling without ever meaning "wait 15 s for a lock".
    /// </summary>
    private const int HierarchyCommandTimeoutSeconds = 15;

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

    private static ObjectResult ChildrenPreventDelete(int count, bool possiblyFromAnotherOrganization = false) => new(new ProblemDetails
    {
        Status = StatusCodes.Status409Conflict,
        Title = "Item has children",
        Detail = possiblyFromAnotherOrganization
            ? $"This item has {count} child item(s), possibly from another organization: the refusing constraint has no organization filter. Move or delete them before deleting this item."
            : $"This item has {count} child item(s). Move or delete them before deleting this item."
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
        // A `RESTRICT` foreign key answers SQLITE_CONSTRAINT (19) with the extended code
        // SQLITE_CONSTRAINT_TRIGGER (1811), not SQLITE_CONSTRAINT_FOREIGNKEY (787) — measured. The
        // filter used to name 787, so this handler never ran and the honest 409 the code below builds
        // was never built. Match the constraint class and let the child count decide: with children
        // this is the delete being refused, without them it is a real error and is rethrown.
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 })
        {
            // Only an Item's children justify this 409; other FK failures remain real errors.
            _context.ChangeTracker.Clear();
            // The pre-check inside the lock counts this organization's children, which is what the
            // caller may see and move. The constraint that just fired has no organization filter, so
            // the diagnosis must not have one either: a child row of another organization (only
            // corruption produces one) would be filtered out here and turn the honest 409 into a 500.
            var childCount = await _context.Items.IgnoreQueryFilters()
                .CountAsync(child => child.ParentId == id);
            if (childCount > 0) return ChildrenPreventDelete(childCount, possiblyFromAnotherOrganization: true);
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
    public Task<IActionResult> Activate(int id, CancellationToken cancellationToken) =>
        WithHierarchyWriteLock(() => ActivateLocked(id, cancellationToken));

    private async Task<IActionResult> ActivateLocked(int id, CancellationToken cancellationToken)
    {
        var archive = await _context.ItemArchives.Where(row => row.ItemId == id && row.RestoredAtUtc == null)
            .OrderByDescending(row => row.DeletedAtUtc).ThenByDescending(row => row.ArchiveId)
            .FirstOrDefaultAsync(cancellationToken);
        if (archive is null) return NotFound();

        // The writer lock and its transaction belong to WithHierarchyWriteLock, exactly as they do
        // for every other route that reads the tree before writing it.
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
            // A parent chain that already loops cannot be restored under: the tree would not paint
            // the record, which is the same state Post and Put refuse with the same words.
            if (hierarchy.AlreadyLooping)
                return InvalidHierarchy("The parent hierarchy already contains a cycle.");
            if (hierarchy.Cycle) return InvalidHierarchy("The restore would create a cycle.");
            var attemptedLevel = hierarchy.ParentLevel + 1;
            if (attemptedLevel > _maxDepth) return DepthExceeded(attemptedLevel);
        }

        // An archive written before the code/label contract could carry attributes the catalogue
        // does not know; restoring it would create an item the API then refuses to save again.
        // Refuse the restore for the same reason and with the same words, before any file or row
        // moves — the archive stays exactly as it was. A bag that validates is stored normalized
        // (labels resolved to codes), the same way a fresh write would land.
        var (restoreRefusal, restoredAttributes) = await ResolveAttributesAsync(archive.Attributes);
        if (restoreRefusal is not null) return restoreRefusal;

        var restored = new Item
        {
            TenantId = _context.CurrentTenantId,
            Id = idIsFree ? archive.ItemId : 0,
            Label = archive.Label, Icon = archive.Icon, ParentId = parentId,
            Attributes = string.IsNullOrWhiteSpace(restoredAttributes) ? archive.Attributes : restoredAttributes,
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

            // Committing is WithHierarchyWriteLock's job: an answer the caller rejected (a 400 from
            // the depth or cycle checks above) must not leave half a restore behind, and a retry
            // after a busy lock has to start over.
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

                return JsonBody(new { tree, panels });
            }
            throw new HttpRequestException();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ItemController.Get could not read the tree");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Answers with one buffered JSON body instead of letting MVC stream it. A payload deeper than
    /// the serializer's <c>MaxDepth</c> used to die halfway: the response had already started, so it
    /// arrived as <c>200</c> with a truncated document and no status could correct it. Serializing
    /// into a buffer first turns that into an honest answer — the whole body, or a 500 that says why.
    /// </summary>
    private IActionResult JsonBody(object payload)
    {
        var options = _json.Value.JsonSerializerOptions;
        try
        {
            return new ContentResult
            {
                Content = JsonSerializer.Serialize(payload, options),
                ContentType = "application/json; charset=utf-8",
                StatusCode = StatusCodes.Status200OK
            };
        }
        catch (JsonException ex)
        {
            _log.LogError(ex, "The item tree is deeper than the serializer allows (MaxDepth {MaxDepth})", options.MaxDepth);
            return new ObjectResult(new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Item tree cannot be serialized",
                Detail = $"The item tree is deeper than the JSON depth this API allows (MaxDepth {options.MaxDepth}). "
                    + "The stored hierarchy is outside the depth the write routes enforce."
            })
            { StatusCode = StatusCodes.Status500InternalServerError };
        }
    }
}
