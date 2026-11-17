using IMCAPI.DB;
using IMCAPI.DB.Entities;
using Microsoft.EntityFrameworkCore;

namespace IMCAPI.Services;

/// <summary>
/// The retention policy for the delete ledger, in one place so the endpoint and the daily sweep
/// cannot drift apart: a row deleted more than <see cref="DefaultDays"/> days ago is gone, and its
/// files with it — the archive exists to undo a mistake, not to keep the workshop's stock forever.
///
/// Two callers: `POST /api/item/archive/purge` (a superadmin asking now, optionally for a different
/// age) and <see cref="ArchiveRetentionSweeper"/> (the clock asking every day).
/// </summary>
public sealed class ArchiveRetention
{
    /// <summary>One month. The number the owner picked; change it here once, not in three places.</summary>
    public const int DefaultDays = 30;

    private readonly ApplicationContext _context;
    private readonly AttachmentStorage _attachments;
    private readonly AuditLogWriter _audit;

    public ArchiveRetention(ApplicationContext context, AttachmentStorage attachments, AuditLogWriter audit)
    {
        _context = context;
        _attachments = attachments;
        _audit = audit;
    }

    /// <summary>
    /// The age to purge, in days. An explicit request wins; otherwise the environment, then the
    /// configuration, then <see cref="DefaultDays"/>. Zero or less means "not automatically" — the
    /// purge endpoint can still be asked for a specific age.
    /// </summary>
    public static int ResolveDays(int? requested, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        if (requested is { } explicitDays) return explicitDays;
        if (int.TryParse(Environment.GetEnvironmentVariable("ARCHIVE_RETENTION_DAYS"), out var fromEnvironment)
            && fromEnvironment >= 0) return fromEnvironment;
        if (int.TryParse(configuration["Archive:Retention:Days"], out var fromConfiguration)
            && fromConfiguration >= 0) return fromConfiguration;
        return DefaultDays;
    }

    /// <summary>
    /// Deletes the ledger rows older than the cutoff and the files they were the last owner of.
    /// Never touches a live record: only a purged row loses its files, which is what a purge is for.
    /// </summary>
    public async Task<PurgeResult> PurgeAsync(DateTime cutoffUtc, int ageDays, string actor,
        CancellationToken cancellationToken = default)
    {
        var expired = await _context.ItemArchives
            .Where(row => row.DeletedAtUtc < cutoffUtc)
            .ToListAsync(cancellationToken);
        if (expired.Count == 0) return new PurgeResult(0, 0, cutoffUtc, ageDays);

        var files = expired
            .SelectMany(row => ReadAttachmentPaths(row.AttachmentsJson))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();

        _context.ItemArchives.RemoveRange(expired);
        await _context.SaveChangesAsync(cancellationToken);
        // The rows are gone from the database; the bytes go with them.
        _attachments.DeleteFromTrash(files);

        await _audit.WriteAsync(AuditActions.ArchivePurge, "ItemArchive", cutoffUtc.ToString("O"),
            cancellationToken: cancellationToken,
            afterJson: System.Text.Json.JsonSerializer.Serialize(new
            {
                organizationId = _context.CurrentTenantId, deleted = expired.Count, cutoffUtc, olderThanDays = ageDays,
                destroyedAttachments = files.Length, by = actor
            }));

        return new PurgeResult(expired.Count, files.Length, cutoffUtc, ageDays);
    }

    public sealed record PurgeResult(int Deleted, int DestroyedAttachments, DateTime CutoffUtc, int OlderThanDays);

    /// <summary>
    /// The paths stored in a ledger row's attachment snapshot. Anything that is not the expected JSON
    /// is treated as "no attachments": a purge must not fail because an old row is malformed.
    /// </summary>
    private static IEnumerable<string?> ReadAttachmentPaths(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) yield break;
        List<AttachmentSnapshot>? snapshot = null;
        try
        {
            snapshot = System.Text.Json.JsonSerializer.Deserialize<List<AttachmentSnapshot>>(json,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (System.Text.Json.JsonException)
        {
            // Leave snapshot null: an unreadable row keeps its files, which is the safe side.
        }

        foreach (var attachment in snapshot ?? new List<AttachmentSnapshot>())
            yield return attachment.FilePath;
    }

    private sealed record AttachmentSnapshot(string? FilePath);
}
