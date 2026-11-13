using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IMCAPI.DB;
using IMCAPI.DB.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Testing;

/// <summary>
/// Deleting a record used to unlink its files, so restoring the record from the archive handed it
/// back with its attachments silently gone. These tests pin the new contract: a delete moves the
/// files to a trash that mirrors the live tree, a restore is the same move in reverse and recreates
/// the rows, and only a purge of the archive row destroys anything.
/// </summary>
[NonParallelizable]
[TestFixture]
public sealed class AttachmentTrashTests
{
    private const string Password = "AuthTest!Secure123";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    [OneTimeSetUp]
    public async Task Start()
    {
        _factory = new ApiFactory();
        _client = _factory.CreateClient();
        var login = await _client.PostAsJsonAsync("/api/auth/login",
            new { email = "bootstrap@example.test", password = Password });
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("access_token").GetString();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    [OneTimeTearDown]
    public void Stop()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task Delete_MovesTheFileToTheTrashAndKeepsItInTheLedger()
    {
        var itemId = await CreateItem("trash-" + Guid.NewGuid().ToString("N"));
        var bytes = PhotoBytes();
        var uploaded = await UploadAsync(itemId, "trash-photo.png", bytes);
        var filePath = uploaded.GetProperty("filePath").GetString()!;
        var live = Live(filePath);
        var trashed = Trashed(filePath);
        Assert.That(File.Exists(live), Is.True, "the upload is on disk before the delete");

        var deleted = await _client.DeleteAsync($"/api/item/{itemId}");
        Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), await deleted.Content.ReadAsStringAsync());

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var archive = await context.ItemArchives.SingleAsync(row => row.ItemId == itemId);
        Assert.Multiple(() =>
        {
            Assert.That(archive.AttachmentsJson, Does.Contain(filePath), "the ledger keeps the file it is holding");
            Assert.That(File.Exists(live), Is.False, "the live tree no longer holds the file");
            Assert.That(File.Exists(trashed), Is.True, "it waits in the mirrored trash instead");
            Assert.That(Directory.Exists(Path.GetDirectoryName(live)), Is.False,
                "the record's folder goes with its last file");
            Assert.That(File.ReadAllBytes(trashed), Is.EqualTo(bytes), "the delete did not rewrite the bytes");
        });
        Assert.That(await context.Attachments.IgnoreQueryFilters().AnyAsync(row => row.ItemId == itemId), Is.False,
            "the attachment row is gone; the ledger is what remembers the file");
    }

    [Test]
    public async Task Restore_BringsTheFileAndTheRowBack()
    {
        var itemId = await CreateItem("restore-" + Guid.NewGuid().ToString("N"));
        var bytes = PhotoBytes();
        var uploaded = await UploadAsync(itemId, "restore-photo.png", bytes);
        var archivedPath = uploaded.GetProperty("filePath").GetString()!;
        Assert.That((await _client.DeleteAsync($"/api/item/{itemId}")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var response = await _client.PatchAsync($"/api/item/{itemId}/activate", content: null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        Assert.Multiple(() =>
        {
            Assert.That(response.Headers.TryGetValues("X-Restored-Attachments", out var restoredHeader), Is.True,
                "the caller is told what came back");
            Assert.That(restoredHeader!.Single(), Is.EqualTo("1"));
            Assert.That(response.Headers.Contains("X-Missing-Attachments"), Is.False);
        });
        Assert.That(File.Exists(Live(archivedPath)), Is.True, "the file is back where the ledger says it was");
        Assert.That(File.Exists(Trashed(archivedPath)), Is.False, "and no longer sits in the trash");

        // The tree is what the UI draws the count from.
        var list = await _client.GetFromJsonAsync<JsonElement>("/api/item", Json);
        var node = list.GetProperty("tree").EnumerateArray().Single(item => item.GetProperty("id").GetInt32() == itemId);
        Assert.That(node.GetProperty("attachmentCount").GetInt32(), Is.EqualTo(1));

        var attachments = await _client.GetFromJsonAsync<JsonElement>($"/api/attachment/item/{itemId}", Json);
        var attachment = attachments.EnumerateArray().Single();
        Assert.Multiple(() =>
        {
            Assert.That(attachment.GetProperty("fileName").GetString(), Is.EqualTo("restore-photo.png"));
            Assert.That(attachment.GetProperty("type").GetString(), Is.EqualTo("Photo"));
            Assert.That(attachment.GetProperty("filePath").GetString(), Is.EqualTo(archivedPath),
                "the archived path is used as it is");
        });

        var file = await _client.GetAsync($"/api/attachment/{attachment.GetProperty("id").GetInt32()}/file");
        Assert.That(file.StatusCode, Is.EqualTo(HttpStatusCode.OK), await file.Content.ReadAsStringAsync());
        Assert.That(await file.Content.ReadAsByteArrayAsync(), Is.EqualTo(bytes), "byte-identical to the upload");
    }

    [Test]
    public async Task Purge_DestroysTheFilesTheLedgerWasHolding()
    {
        var itemId = await CreateItem("purge-trash-" + Guid.NewGuid().ToString("N"));
        var uploaded = await UploadAsync(itemId, "purge-photo.png", PhotoBytes());
        var filePath = uploaded.GetProperty("filePath").GetString()!;
        Assert.That((await _client.DeleteAsync($"/api/item/{itemId}")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(File.Exists(Trashed(filePath)), Is.True);

        // Age the row past the retention window: the API only archives by deleting a live record.
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
            var archive = await context.ItemArchives.SingleAsync(row => row.ItemId == itemId && row.RestoredAtUtc == null);
            archive.DeletedAtUtc = DateTime.UtcNow.AddDays(-40);
            await context.SaveChangesAsync();
        }

        var response = await _client.PostAsync("/api/item/archive/purge?olderThanDays=30", content: null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.That(payload.GetProperty("deleted").GetInt32(), Is.GreaterThanOrEqualTo(1));

        Assert.That(File.Exists(Trashed(filePath)), Is.False, "a purge is where a file is finally destroyed");
        using var check = _factory.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<ApplicationContext>();
        Assert.That(await db.ItemArchives.AnyAsync(row => row.ItemId == itemId), Is.False);
    }

    [Test]
    public async Task Restore_WhenTheTrashedFileIsGone_SucceedsAndSaysHowManyAreLost()
    {
        var itemId = await CreateItem("lost-" + Guid.NewGuid().ToString("N"));
        var uploaded = await UploadAsync(itemId, "lost-photo.png", PhotoBytes());
        var filePath = uploaded.GetProperty("filePath").GetString()!;
        Assert.That((await _client.DeleteAsync($"/api/item/{itemId}")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        var trashed = Trashed(filePath);
        Assert.That(File.Exists(trashed), Is.True);
        File.Delete(trashed);

        var response = await _client.PatchAsync($"/api/item/{itemId}/activate", content: null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "a missing file is reported, not turned into a failed restore: " + await response.Content.ReadAsStringAsync());
        var restored = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Multiple(() =>
        {
            Assert.That(restored.GetProperty("id").GetInt32(), Is.EqualTo(itemId));
            Assert.That(response.Headers.TryGetValues("X-Missing-Attachments", out var missing), Is.True);
            Assert.That(missing!.Single(), Is.EqualTo("1"));
            Assert.That(response.Headers.GetValues("X-Restored-Attachments").Single(), Is.EqualTo("0"));
        });

        Assert.That((await _client.GetAsync($"/api/item/{itemId}")).StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "the record itself comes back");
        Assert.That((await _client.GetAsync($"/api/attachment/item/{itemId}")).StatusCode,
            Is.EqualTo(HttpStatusCode.NoContent),
            "a row naming a file nobody could bring back is not restored");
    }

    /// <summary>
    /// A path read back from the database is client output until proven otherwise, and every trash
    /// operation resolves it under the media root: `..`, a rooted path and a symlink pointing out of
    /// the tree are refused instead of moving or deleting something else on the disk.
    /// </summary>
    [Test]
    public void TrashOperations_RefuseAPathThatEscapesTheMediaRoot()
    {
        var storage = _factory.Services.GetRequiredService<IMCAPI.Services.AttachmentStorage>();
        var mediaRoot = _factory.MediaRoot;
        var media = Path.Combine(mediaRoot, "media");
        // The victim sits exactly where `../loot` from the media root points, so this test fails if
        // the guard lets the move through — not merely because the path looked odd.
        var outside = Path.Combine(Path.GetDirectoryName(mediaRoot)!, "loot");
        Directory.CreateDirectory(Path.Combine(media, "items"));
        Directory.CreateDirectory(outside);
        var victim = Path.Combine(outside, "keep-me.png");
        File.WriteAllBytes(victim, new byte[] { 1, 2, 3 });

        var link = Path.Combine(media, "items", "link");
        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Assert.Ignore("This platform does not let the test create a directory symlink.");
        }

        var escaped = new[] { "../loot/keep-me.png", victim, Path.Combine("media", "items", "link", "keep-me.png") };
        storage.MoveToTrash(escaped);
        storage.DeleteFromTrash(escaped);
        // The delete endpoint's hard delete obeys the same rule.
        storage.Delete(Path.Combine("..", "loot", "keep-me.png"));
        storage.Delete(victim);

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(victim), Is.True, "no operation may reach a file outside media/");
            Assert.That(File.Exists(Path.Combine(media, "items", "keep-me.png")), Is.False,
                "and the escaped path was not left inside the live tree");
            var trash = Path.Combine(media, "trash");
            Assert.That(Directory.Exists(trash) ? Directory.EnumerateFiles(trash, "keep-me.png", SearchOption.AllDirectories) : [],
                Is.Empty, "nothing was moved into the trash from a path that escapes the media root");
        });
    }

    private async Task<int> CreateItem(string label)
    {
        var response = await _client.PostAsJsonAsync("/api/item", new
        {
            label, icon = "", parentId = (int?)null, attributes = "{}",
            children = Array.Empty<object>(), attachments = Array.Empty<object>()
        });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();
    }

    private async Task<JsonElement> UploadAsync(int itemId, string fileName, byte[] bytes)
    {
        using var form = new MultipartFormDataContent
        {
            { new StringContent(itemId.ToString()), "itemId" },
            { new StringContent("Photo"), "type" },
        };
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(content, "file", fileName);

        var response = await _client.PostAsync("/api/attachment/upload", form);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    /// <summary>Where a stored relative path lives: <c>media/items/7/a.png</c> under the media root.</summary>
    private string Live(string relativePath) =>
        Path.Combine(_factory.MediaRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>The mirrored trash location: <c>media/items/7/a.png</c> is <c>media/trash/items/7/a.png</c>.</summary>
    private string Trashed(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        const string media = "media/";
        var tail = normalized.StartsWith(media, StringComparison.OrdinalIgnoreCase)
            ? normalized[media.Length..]
            : normalized;
        return Path.Combine(_factory.MediaRoot, "media", "trash", tail.Replace('/', Path.DirectorySeparatorChar));
    }

    private static byte[] PhotoBytes()
    {
        using var bitmap = new SKBitmap(48, 32);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint { Color = SKColors.DarkOrange };
            canvas.DrawCircle(24, 16, 10, paint);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }
}
