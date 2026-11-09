using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace Testing;

/// <summary>
/// The API over HTTP, the way the app talks to it: the shapes the UI depends on (a tree with
/// children, panels with attributes, attachment counts), the write paths, and the file endpoint.
/// </summary>
[TestFixture]
public class ApiTests
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [OneTimeSetUp]
    public void StartApi()
    {
        _factory = new ApiFactory();
        _client = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public void StopApi()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private static string Unique(string prefix) => $"{prefix} {Guid.NewGuid():N}";

    private async Task<JsonElement> PostItem(string label, int? parentId = null, string? attributes = null)
    {
        var response = await _client.PostAsJsonAsync("/api/item", new
        {
            label,
            icon = "",
            parentId,
            attributes = attributes ?? "{}",
            children = Array.Empty<object>(),
            attachments = Array.Empty<object>(),
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    [Test]
    public async Task GetItems_ReturnsARootTreeAndPanels()
    {
        await PostItem(Unique("Root"));

        var payload = await _client.GetFromJsonAsync<JsonElement>("/api/item", Json);

        Assert.Multiple(() =>
        {
            Assert.That(payload.TryGetProperty("tree", out var tree), Is.True);
            Assert.That(payload.TryGetProperty("panels", out var panels), Is.True);
            Assert.That(tree.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(tree.GetArrayLength(), Is.GreaterThan(0), "the record just created is a root");
            Assert.That(panels.GetArrayLength(), Is.GreaterThan(0));
        });

        var node = payload.GetProperty("tree")[0];
        Assert.Multiple(() =>
        {
            Assert.That(node.TryGetProperty("children", out _), Is.True, "every node carries children");
            Assert.That(node.TryGetProperty("attachmentCount", out _), Is.True,
                "the list needs the count, not the thumbnails");
        });
    }

    [Test]
    public async Task PostAndGetById_ReturnTheRecordWithItsChildren()
    {
        var parentLabel = Unique("Kit");
        var parent = await PostItem(parentLabel);
        var parentId = parent.GetProperty("id").GetInt32();
        await PostItem(Unique("Part"), parentId);

        var detail = await _client.GetFromJsonAsync<JsonElement>($"/api/item/{parentId}", Json);

        Assert.Multiple(() =>
        {
            Assert.That(detail.GetProperty("label").GetString(), Is.EqualTo(parentLabel));
            Assert.That(detail.GetProperty("children").GetArrayLength(), Is.EqualTo(1),
                "GetById must load the child collection (it used to return none)");
        });
    }

    [Test]
    public async Task PutItem_UpdatesTheAttributesTheFormSends()
    {
        var item = await PostItem(Unique("Serial"));
        var id = item.GetProperty("id").GetInt32();
        var attributes = "{\"Brand\":\"DW\",\"Serial\":\"SN-0042\"}";

        var put = await _client.PutAsJsonAsync($"/api/item/{id}", new
        {
            id,
            label = "Renamed",
            icon = "",
            parentId = (int?)null,
            attributes,
            children = Array.Empty<object>(),
            attachments = Array.Empty<object>(),
        });

        Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), await put.Content.ReadAsStringAsync());

        var stored = await _client.GetFromJsonAsync<JsonElement>($"/api/item/{id}", Json);
        Assert.Multiple(() =>
        {
            Assert.That(stored.GetProperty("label").GetString(), Is.EqualTo("Renamed"));
            Assert.That(stored.GetProperty("attributes").GetString(), Is.EqualTo(attributes));
        });
    }

    [Test]
    public async Task DeleteItem_TakesItOutOfTheList()
    {
        var item = await PostItem(Unique("Temporary"));
        var id = item.GetProperty("id").GetInt32();

        var deleted = await _client.DeleteAsync($"/api/item/{id}");
        Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var list = await _client.GetFromJsonAsync<JsonElement>("/api/item", Json);
        var labels = list.GetProperty("panels").EnumerateArray()
            .Select(p => p.GetProperty("label").GetString())
            .ToList();
        Assert.That(labels, Does.Not.Contain(item.GetProperty("label").GetString()));
    }

    [Test]
    public async Task PostAttachment_StoresTheFileAndServesTheSameBytesBack()
    {
        var item = await PostItem(Unique("WithFile"));
        var itemId = item.GetProperty("id").GetInt32();
        var bytes = Png(24, 16);

        var created = await _client.PostAsJsonAsync("/api/attachment", new
        {
            itemId,
            type = "Photo",
            fileName = "proof.png",
            uploadBase64 = "data:image/png;base64," + Convert.ToBase64String(bytes),
        });

        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.OK), await created.Content.ReadAsStringAsync());
        var attachment = await created.Content.ReadFromJsonAsync<JsonElement>(Json);
        var id = attachment.GetProperty("id").GetInt32();

        Assert.Multiple(() =>
        {
            Assert.That(attachment.GetProperty("filePath").GetString(), Does.StartWith("media/items/"));
            Assert.That(attachment.GetProperty("thumbnailBase64").GetString(), Is.Not.Null.And.Not.Empty,
                "the row keeps a small preview");
        });

        var file = await _client.GetAsync($"/api/attachment/{id}/file");
        file.EnsureSuccessStatusCode();
        var served = await file.Content.ReadAsByteArrayAsync();
        Assert.Multiple(() =>
        {
            Assert.That(file.Content.Headers.ContentType?.MediaType, Is.EqualTo("image/png"));
            Assert.That(Convert.ToHexString(SHA256.HashData(served)),
                Is.EqualTo(Convert.ToHexString(SHA256.HashData(bytes))),
                "the endpoint must return the original bytes");
        });

        var onDisk = Path.Combine(_factory.MediaRoot, attachment.GetProperty("filePath").GetString()!
            .Replace('/', Path.DirectorySeparatorChar));
        Assert.That(File.Exists(onDisk), Is.True, "the original lives in the media folder, not the row");
    }

    [Test]
    public async Task AttachmentCount_FollowsTheFilesTheRecordCarries()
    {
        var item = await PostItem(Unique("Counted"));
        var itemId = item.GetProperty("id").GetInt32();

        var before = await TreeAttachmentCount(itemId);
        Assert.That(before, Is.EqualTo(0));

        var created = await _client.PostAsJsonAsync("/api/attachment", new
        {
            itemId,
            type = "Photo",
            fileName = "photo.png",
            uploadBase64 = "data:image/png;base64," + Convert.ToBase64String(Png(12, 12)),
        });
        created.EnsureSuccessStatusCode();

        Assert.That(await TreeAttachmentCount(itemId), Is.EqualTo(1));
    }

    [Test]
    public async Task DeleteAttachment_RemovesTheRowAndTheFile()
    {
        var item = await PostItem(Unique("Deleting"));
        var itemId = item.GetProperty("id").GetInt32();

        var created = await _client.PostAsJsonAsync("/api/attachment", new
        {
            itemId,
            type = "Photo",
            fileName = "gone.png",
            uploadBase64 = "data:image/png;base64," + Convert.ToBase64String(Png(10, 10)),
        });
        created.EnsureSuccessStatusCode();
        var attachment = await created.Content.ReadFromJsonAsync<JsonElement>(Json);
        var id = attachment.GetProperty("id").GetInt32();
        var path = Path.Combine(_factory.MediaRoot, attachment.GetProperty("filePath").GetString()!
            .Replace('/', Path.DirectorySeparatorChar));

        var deleted = await _client.DeleteAsync($"/api/attachment/{id}");
        Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var file = await _client.GetAsync($"/api/attachment/{id}/file");
        Assert.Multiple(() =>
        {
            Assert.That(file.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That(File.Exists(path), Is.False, "deleting the row deletes the file too");
        });
    }

    [Test]
    public async Task GetConstants_ReturnsTheSeededCatalogueWithDataTypes()
    {
        var payload = await _client.GetFromJsonAsync<JsonElement>("/api/constants", Json);
        var panels = payload.GetProperty("panels").EnumerateArray().ToList();

        Assert.That(panels, Is.Not.Empty, "a fresh database is seeded on startup");

        var attributes = panels
            .Where(p => p.TryGetProperty("group", out var g) && g.GetString() == "Attribute")
            .Where(p => p.TryGetProperty("parentId", out var parent) && parent.ValueKind == JsonValueKind.Number)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(attributes.Count, Is.GreaterThan(0));
            Assert.That(
                attributes.Single(a => a.GetProperty("label").GetString() == "BuyPrice")
                    .GetProperty("dataType").GetString(),
                Is.EqualTo("Money"));
            Assert.That(
                attributes.Single(a => a.GetProperty("label").GetString() == "Description")
                    .GetProperty("dataType").GetString(),
                Is.EqualTo("LongText"));
        });
    }

    [Test]
    public async Task PostItem_WithoutALabel_IsRejected()
    {
        var response = await _client.PostAsJsonAsync("/api/item", new
        {
            label = "",
            icon = "",
            attributes = "{}",
            children = Array.Empty<object>(),
            attachments = Array.Empty<object>(),
        });

        Assert.That((int)response.StatusCode, Is.GreaterThanOrEqualTo(400),
            "a record without a label cannot be stored");
    }

    private async Task<int> TreeAttachmentCount(int itemId)
    {
        var payload = await _client.GetFromJsonAsync<JsonElement>("/api/item", Json);
        return FindCount(payload.GetProperty("tree"), itemId);
    }

    private static int FindCount(JsonElement nodes, int itemId)
    {
        foreach (var node in nodes.EnumerateArray())
        {
            if (node.GetProperty("id").GetInt32() == itemId
                && node.TryGetProperty("attachmentCount", out var count))
            {
                return count.GetInt32();
            }

            if (node.TryGetProperty("children", out var children) && children.GetArrayLength() > 0)
            {
                var found = FindCount(children, itemId);
                if (found >= 0) return found;
            }
        }

        return -1;
    }

    /// <summary>A real PNG, so the thumbnail path has something to decode.</summary>
    private static byte[] Png(int width, int height)
    {
        using var image = new SkiaSharp.SKBitmap(width, height);
        using var canvas = new SkiaSharp.SKCanvas(image);
        canvas.Clear(SkiaSharp.SKColors.SteelBlue);
        using var encoded = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
        return encoded.ToArray();
    }
}
