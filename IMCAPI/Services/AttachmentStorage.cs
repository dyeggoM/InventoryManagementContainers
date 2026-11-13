using SkiaSharp;

namespace IMCAPI.Services;

/// <summary>
/// Stores an attachment's bytes under <c>media/items/{itemId}/</c> and returns a small, compressed
/// preview to keep in the row. A phone photo is 3–6 MB; keeping it base64 in the database made
/// every attachment list carry megabytes and grow the file without limit.
/// <para>
/// Deleting a record does not unlink its files: they move to a <c>media/trash/</c> tree that mirrors
/// the live one, so a restore from the archive is the same move in reverse, and only a purge of the
/// archive row destroys anything.
/// </para>
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

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".heic", ".heif",
        ".pdf", ".txt", ".csv", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".zip", ".rar", ".7z",
    };

    /// <summary>Writes the original file and returns its relative path plus the preview.</summary>
    public async Task<StoredAttachment> SaveAsync(int itemId, string? fileName, byte[] content,
        CancellationToken cancellationToken = default)
    {
        var relativeDirectory = Path.Combine("media", "items", itemId.ToString());
        var absoluteDirectory = Path.Combine(_contentRoot, relativeDirectory);
        Directory.CreateDirectory(absoluteDirectory);

        // The client's extension is a suggestion, not a decision: storing `.html` or `.svg` under the
        // media root is harmless while the API streams the bytes, and stored XSS the day that folder
        // is served. Anything outside this list is re-detected from the content itself.
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension))
        {
            extension = DetectExtension(content);
        }

        // The preview is computed before anything is written: a throw from here used to escape after
        // the file existed, and the caller had no path to clean up.
        var thumbnail = CreateThumbnail(content);
        var storedName = $"{Guid.NewGuid():N}{extension}";
        await File.WriteAllBytesAsync(Path.Combine(absoluteDirectory, storedName), content, cancellationToken);

        var relativePath = Path.Combine(relativeDirectory, storedName).Replace('\\', '/');
        return new StoredAttachment(relativePath, thumbnail);
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

    /// <summary>
    /// The absolute path of a stored file, confined to the media root. A path read back from the
    /// database is still a path this process did not build, so a row claiming
    /// <c>../appsettings.json</c> must not turn the file route into a reader for the whole disk.
    /// Answers an empty string when the path escapes, which every caller already treats as missing.
    /// </summary>
    public string ToAbsolute(string relativePath) => ResolveLive(relativePath) ?? string.Empty;

    /// <summary>Best effort: a missing file must not stop a row from being deleted.</summary>
    public void Delete(string? relativePath) => DeleteFile(ResolveLive(relativePath));

    /// <summary>
    /// Moves files aside instead of destroying them: <c>media/items/7/a.png</c> becomes
    /// <c>media/trash/items/7/a.png</c>. The trash mirrors the live tree on purpose — a restore is
    /// the same move in reverse, so the paths stored in the archive stay usable exactly as they are.
    /// Best effort, like the delete it replaces: a missing file must not stop a deletion, and the
    /// record's folder still goes away with its last file.
    /// </summary>
    public void MoveToTrash(IEnumerable<string?> relativePaths)
    {
        foreach (var relativePath in relativePaths)
        {
            var source = ResolveLive(relativePath);
            var destination = ResolveTrash(relativePath);
            if (source is null || destination is null)
            {
                continue;
            }

            try
            {
                if (!File.Exists(source))
                {
                    RemoveEmptyFolder(Path.GetDirectoryName(source));
                    continue;
                }

                var folder = Path.GetDirectoryName(destination);
                if (folder is not null)
                {
                    Directory.CreateDirectory(folder);
                }

                File.Move(source, destination, overwrite: true);
                RemoveEmptyFolder(Path.GetDirectoryName(source));
            }
            catch (IOException)
            {
                // Locked or already gone: the row is what matters.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// Puts the files back where the archive says they were and returns the paths that could not
    /// come back, so the caller can say how many are lost instead of pretending the restore was
    /// whole.
    /// </summary>
    public IReadOnlyList<string> RestoreFromTrash(IEnumerable<string?> relativePaths)
    {
        var missing = new List<string>();
        foreach (var relativePath in relativePaths)
        {
            var source = ResolveTrash(relativePath);
            var destination = ResolveLive(relativePath);
            if (source is null || destination is null)
            {
                missing.Add(relativePath ?? string.Empty);
                continue;
            }

            try
            {
                if (!File.Exists(source))
                {
                    missing.Add(relativePath!);
                    continue;
                }

                var folder = Path.GetDirectoryName(destination);
                if (folder is not null)
                {
                    Directory.CreateDirectory(folder);
                }

                File.Move(source, destination, overwrite: true);
                RemoveEmptyFolder(Path.GetDirectoryName(source));
            }
            catch (IOException)
            {
                missing.Add(relativePath!);
            }
            catch (UnauthorizedAccessException)
            {
                missing.Add(relativePath!);
            }
        }

        return missing;
    }

    /// <summary>
    /// The only place a stored file is really destroyed now: a deletion moves a file to the trash,
    /// and the purge of its archive row is what removes it for good.
    /// </summary>
    public void DeleteFromTrash(IEnumerable<string?> relativePaths)
    {
        foreach (var relativePath in relativePaths)
        {
            DeleteFile(ResolveTrash(relativePath));
        }
    }

    private void DeleteFile(string? absolute)
    {
        if (absolute is null)
        {
            return;
        }

        try
        {
            if (File.Exists(absolute))
            {
                File.Delete(absolute);
            }

            RemoveEmptyFolder(Path.GetDirectoryName(absolute));
        }
        catch (IOException)
        {
            // Locked or already gone: the row is what matters.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// A record's folder is only a container: once its last file is gone the folder goes too,
    /// instead of leaving an empty directory per deleted record.
    /// </summary>
    private static void RemoveEmptyFolder(string? folder)
    {
        try
        {
            if (folder is not null
                && Directory.Exists(folder)
                && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private const string MediaFolder = "media";
    private const string TrashFolder = "trash";

    /// <summary>
    /// Where a live relative path really lives, or null when it is not a plain path inside
    /// <c>media/</c>. These paths are read back from the database, which makes them client output
    /// until proven otherwise.
    /// </summary>
    private string? ResolveLive(string? relativePath) => ResolveUnderMedia(SafeSegments(relativePath));

    /// <summary>
    /// The mirrored location of a live path under <c>media/trash/</c>:
    /// <c>media/items/7/a.png</c> is stored as <c>media/trash/items/7/a.png</c>.
    /// </summary>
    private string? ResolveTrash(string? relativePath)
    {
        var segments = SafeSegments(relativePath);
        if (segments is null)
        {
            return null;
        }

        // A path that does not start with `media/` is mirrored as it is, which keeps a legacy row
        // recoverable without letting it name anything outside the media root.
        var skip = string.Equals(segments[0], MediaFolder, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (segments.Length == skip)
        {
            return null;
        }

        var mirrored = new string[segments.Length - skip + 2];
        mirrored[0] = MediaFolder;
        mirrored[1] = TrashFolder;
        Array.Copy(segments, skip, mirrored, 2, segments.Length - skip);
        return ResolveUnderMedia(mirrored);
    }

    /// <summary>
    /// The absolute path these segments name, or null when it escapes <c>media/</c>. Every public
    /// path-taking member above goes through here, so the containment rule lives in one place.
    /// </summary>
    private string? ResolveUnderMedia(string[]? segments)
    {
        if (segments is null)
        {
            return null;
        }

        var root = Path.Combine(_contentRoot, MediaFolder);
        var absolute = Path.Combine(new[] { _contentRoot }.Concat(segments).ToArray());
        return IsInside(absolute, root) ? absolute : null;
    }

    /// <summary>
    /// True when <paramref name="candidate"/> stays inside <paramref name="root"/> with every link
    /// on the way followed, so a symlink planted in the media tree cannot redirect a move or a
    /// delete to the rest of the disk.
    /// </summary>
    private static bool IsInside(string candidate, string root)
    {
        try
        {
            var resolvedRoot = RealPath(root);
            var resolved = RealPath(candidate);
            var comparison = OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            return resolved.StartsWith(resolvedRoot + Path.DirectorySeparatorChar, comparison)
                || string.Equals(resolved, resolvedRoot, comparison);
        }
        catch (IOException)
        {
            // A link loop is not a path this class is willing to touch.
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>The path with every symlink on it followed; the missing tail is appended as given.</summary>
    private static string RealPath(string absolute)
    {
        var full = Path.GetFullPath(absolute);
        var root = Path.GetPathRoot(full) ?? string.Empty;
        var current = root.Length == 0 ? Path.DirectorySeparatorChar.ToString() : root;
        var segments = full[root.Length..].Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments)
        {
            var next = Path.Combine(current, segment);
            var info = Directory.Exists(next)
                ? (FileSystemInfo)new DirectoryInfo(next)
                : new FileInfo(next);
            var link = info.LinkTarget;
            if (link is null)
            {
                current = next;
                continue;
            }

            var target = info.ResolveLinkTarget(returnFinalTarget: true);
            current = target is null
                ? Path.GetFullPath(link, current)
                : Path.GetFullPath(target.FullName);
        }

        return current;
    }

    /// <summary>
    /// The segments of a relative path, or null when it is not a plain path: rooted, a drive or an
    /// NTFS stream, or carrying a <c>..</c> or <c>.</c> segment.
    /// </summary>
    private static string[]? SafeSegments(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return null;
        }

        var normalized = relativePath.Replace('\\', '/').Trim();
        if (normalized.Length == 0
            || normalized.StartsWith('/')
            || Path.IsPathRooted(normalized)
            || normalized.Contains(':'))
        {
            return null;
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        foreach (var segment in segments)
        {
            if (segment is "." or "..")
            {
                return null;
            }
        }

        return segments;
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