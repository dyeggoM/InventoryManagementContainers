using SkiaSharp;

namespace IMCAPI.Services;

/// <summary>
/// Stores an attachment's bytes under <c>media/items/{itemId}/</c> and returns a small, compressed
/// preview to keep in the row. A phone photo is 3–6 MB; keeping it base64 in the database made
/// every attachment list carry megabytes and grow the file without limit.
/// </summary>
public sealed class AttachmentStorage
{
    /// <summary>Largest upload accepted, after base64 decoding.</summary>
    public const long MaxBytes = 20L * 1024 * 1024;

    private const int ThumbnailEdge = 320;
    private const int ThumbnailQuality = 72;

    private readonly string _contentRoot;

    public AttachmentStorage(IWebHostEnvironment environment, IConfiguration configuration)
    {
        // Media:Root keeps files out of the project folder — integration tests point it at a temp
        // directory, and a deployment can put the files on a mounted volume.
        _contentRoot = configuration["Media:Root"] is { Length: > 0 } root
            ? root
            : environment.ContentRootPath;
    }

    public sealed record StoredAttachment(string RelativePath, string? ThumbnailBase64);

    /// <summary>
    /// Writes the pending upload of an attachment to disk and leaves the row with the file path and
    /// a compressed preview. Returns an error message when the payload cannot be accepted.
    /// </summary>
    public async Task<string?> ApplyUploadAsync(IMCAPI.DB.Entities.Attachment attachment,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(attachment.UploadBase64))
        {
            return null; // already stored, or an attachment without a file
        }

        var bytes = DecodePayload(attachment.UploadBase64);
        if (bytes is null || bytes.Length == 0)
        {
            return "The attachment payload is not valid base64.";
        }

        if (bytes.Length > MaxBytes)
        {
            return $"The attachment is larger than {MaxBytes / (1024 * 1024)} MB.";
        }

        var stored = await SaveAsync(attachment.ItemId, attachment.FileName, bytes, cancellationToken);
        attachment.FilePath = stored.RelativePath;
        attachment.ThumbnailBase64 = stored.ThumbnailBase64;
        attachment.UploadBase64 = null;
        return null;
    }

    /// <summary>Writes the original file and returns its relative path plus the preview.</summary>
    public async Task<StoredAttachment> SaveAsync(int itemId, string? fileName, byte[] content,
        CancellationToken cancellationToken = default)
    {
        var relativeDirectory = Path.Combine("media", "items", itemId.ToString());
        var absoluteDirectory = Path.Combine(_contentRoot, relativeDirectory);
        Directory.CreateDirectory(absoluteDirectory);

        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (extension.Length is 0 or > 10)
        {
            extension = DetectExtension(content);
        }

        var storedName = $"{Guid.NewGuid():N}{extension}";
        await File.WriteAllBytesAsync(Path.Combine(absoluteDirectory, storedName), content, cancellationToken);

        var relativePath = Path.Combine(relativeDirectory, storedName).Replace('\\', '/');
        return new StoredAttachment(relativePath, CreateThumbnail(content));
    }

    /// <summary>Turns a data URL or a bare base64 string into bytes; null when it is not valid.</summary>
    public static byte[]? DecodePayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        var comma = payload.IndexOf(',');
        var base64 = payload.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma >= 0
            ? payload[(comma + 1)..]
            : payload;

        try
        {
            return Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public string ToAbsolute(string relativePath) =>
        Path.Combine(_contentRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Best effort: a missing file must not stop a row from being deleted.</summary>
    public void Delete(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return;
        }

        try
        {
            var absolute = ToAbsolute(relativePath);
            if (File.Exists(absolute))
            {
                File.Delete(absolute);
            }

            // A record's folder is only a container: once its last file is gone the folder goes too,
            // instead of leaving an empty directory per deleted record.
            var folder = Path.GetDirectoryName(absolute);
            if (folder is not null
                && Directory.Exists(folder)
                && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch (IOException)
        {
            // Locked or already gone: the row is what matters.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static string ContentTypeFor(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".heic" => "image/heic",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream",
        };

    /// <summary>
    /// A data-URL JPEG no larger than <see cref="ThumbnailEdge"/> on its longest side, or null when
    /// the bytes are not an image Skia can decode (a PDF manual, for instance).
    /// </summary>
    public static string? CreateThumbnail(byte[] content)
    {
        try
        {
            using var bitmap = SKBitmap.Decode(content);
            if (bitmap is null || bitmap.Width == 0 || bitmap.Height == 0)
            {
                return null;
            }

            var scale = Math.Min(1f, ThumbnailEdge / (float)Math.Max(bitmap.Width, bitmap.Height));
            var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
            var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));

            using var resized = bitmap.Resize(
                new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            if (resized is null)
            {
                return null;
            }

            using var image = SKImage.FromBitmap(resized);
            using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, ThumbnailQuality);
            return encoded is null ? null : $"data:image/jpeg;base64,{Convert.ToBase64String(encoded.ToArray())}";
        }
        catch (Exception)
        {
            // Unsupported or corrupt image: the attachment is still worth keeping.
            return null;
        }
    }

    private static string DetectExtension(byte[] content)
    {
        if (content.Length >= 3 && content[0] == 0xFF && content[1] == 0xD8)
        {
            return ".jpg";
        }

        if (content.Length >= 8 && content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4E && content[3] == 0x47)
        {
            return ".png";
        }

        if (content.Length >= 4 && content[0] == 0x25 && content[1] == 0x50 && content[2] == 0x44 && content[3] == 0x46)
        {
            return ".pdf";
        }

        return ".bin";
    }
}