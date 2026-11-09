using IMCAPI.Services;
using Microsoft.EntityFrameworkCore;

namespace IMCAPI.DB;

/// <summary>
/// Seeds the configuration tree a fresh database needs. The database itself is not in source
/// control any more, so the app has to be able to build one from nothing.
/// </summary>
public static class DatabaseSeeder
{
    /// <summary>
    /// Runs <c>DB/Seed/seed.sql</c> when the constant table is empty. Only configuration is
    /// seeded — items are created by the user, so seeding never invents inventory.
    /// </summary>
    /// <returns>true when the seed script was executed.</returns>
    public static bool SeedConfiguration(ApplicationContext context, string contentRootPath)
    {
        if (context.Constants.Any())
        {
            return false;
        }

        var seedPath = Path.Combine(contentRootPath, "DB", "Seed", "seed.sql");
        if (!File.Exists(seedPath))
        {
            return false;
        }

        context.Database.ExecuteSqlRaw(File.ReadAllText(seedPath));
        return true;
    }

    /// <summary>
    /// Attribute definitions written before typed values existed have no data type. Prices are
    /// money, prose is long text, everything else is text — set once, then the user can change it
    /// in Configuration.
    /// </summary>
    /// <returns>How many definitions were given a type.</returns>
    public static async Task<int> BackfillAttributeTypesAsync(ApplicationContext context,
        CancellationToken cancellationToken = default)
    {
        var pending = await context.Constants
            .Where(c => c.Group == "Attribute" && c.ParentId != null && c.DataType == null)
            .ToListAsync(cancellationToken);

        foreach (var attribute in pending)
        {
            attribute.DataType = attribute.Label switch
            {
                "BuyPrice" or "RentPrice" => "Money",
                "Description" or "Notes" => "LongText",
                _ => "Text",
            };
        }

        if (pending.Count > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return pending.Count;
    }

    /// <summary>
    /// Rows written before attachments were stored on disk keep the whole image in the row.
    /// Moves those bytes to disk and leaves a thumbnail behind, once.
    /// </summary>
    /// <returns>How many rows were converted.</returns>
    public static async Task<int> BackfillAttachmentsAsync(ApplicationContext context,
        AttachmentStorage storage, CancellationToken cancellationToken = default)
    {
        var legacy = await context.Attachments
            .Where(attachment => attachment.FilePath == null && attachment.ThumbnailBase64 != null)
            .ToListAsync(cancellationToken);

        foreach (var attachment in legacy)
        {
            var bytes = AttachmentStorage.DecodePayload(attachment.ThumbnailBase64);
            if (bytes is null || bytes.Length == 0)
            {
                continue;
            }

            var stored = await storage.SaveAsync(attachment.ItemId, attachment.FileName, bytes, cancellationToken);
            attachment.FilePath = stored.RelativePath;
            attachment.ThumbnailBase64 = stored.ThumbnailBase64;
        }

        if (legacy.Count > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        return legacy.Count;
    }
}
