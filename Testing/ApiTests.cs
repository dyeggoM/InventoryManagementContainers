using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using IMCAPI.DB;
using IMCAPI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Testing;

/// <summary>
/// The API over HTTP, the way the app talks to it: the shapes the UI depends on (a tree with
/// children, panels with attributes, attachment counts), the write paths, and the file endpoint.
/// </summary>
[NonParallelizable]
[TestFixture]
public class ApiTests
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [OneTimeSetUp]
    public async Task StartApi()
    {
        _factory = new ApiFactory();
        _client = _factory.CreateClient();
        var response = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "bootstrap@example.test",
            password = "AuthTest!Secure123",
        });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var auth = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", auth.GetProperty("access_token").GetString());
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
        var label = Unique("Root");
        await PostItem(label);

        var payload = await _client.GetFromJsonAsync<JsonElement>("/api/item", Json);

        Assert.Multiple(() =>
        {
            Assert.That(payload.TryGetProperty("tree", out var tree), Is.True);
            Assert.That(payload.TryGetProperty("panels", out var panels), Is.True);
            Assert.That(tree.ValueKind, Is.EqualTo(JsonValueKind.Array));
            Assert.That(
                tree.EnumerateArray().Select(n => n.GetProperty("label").GetString()),
                Does.Contain(label), "the record just created is in the tree");
            Assert.That(
                panels.EnumerateArray().Select(n => n.GetProperty("label").GetString()),
                Does.Contain(label), "and in the flat list the form uses");
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
    public async Task PutItem_CannotSetAChildAsItsParent_AndTreeRemainsReadable()
    {
        var parent = await PostItem(Unique("CycleParent"));
        var parentId = parent.GetProperty("id").GetInt32();
        var child = await PostItem(Unique("CycleChild"), parentId);
        var childId = child.GetProperty("id").GetInt32();

        var response = await _client.PutAsJsonAsync($"/api/item/{parentId}", new
        {
            id = parentId,
            label = parent.GetProperty("label").GetString(),
            icon = "",
            parentId = childId,
            attributes = "{}",
            children = Array.Empty<object>(),
            attachments = Array.Empty<object>(),
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
            await response.Content.ReadAsStringAsync());

        var get = await _client.GetAsync("/api/item");
        Assert.That(get.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var payload = await get.Content.ReadFromJsonAsync<JsonElement>(Json);
        var tree = payload.GetProperty("tree").EnumerateArray().Single(n => n.GetProperty("id").GetInt32() == parentId);
        Assert.That(tree.GetProperty("children").GetArrayLength(), Is.EqualTo(1));
        Assert.That(tree.GetProperty("children")[0].GetProperty("id").GetInt32(), Is.EqualTo(childId));
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


    [Test]
    public async Task GetItemById_UnknownId_Returns404()
    {
        var response = await _client.GetAsync("/api/item/987654");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task PutItem_WithAMismatchedId_Returns400()
    {
        var item = await PostItem(Unique("Mismatch"));
        var id = item.GetProperty("id").GetInt32();

        var response = await _client.PutAsJsonAsync($"/api/item/{id + 1}", new
        {
            id,
            label = "Should not land",
            icon = "",
            attributes = "{}",
            children = Array.Empty<object>(),
            attachments = Array.Empty<object>(),
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task DeleteItem_AlsoDeletesItsAttachmentsAndFiles()
    {
        var item = await PostItem(Unique("Cascade"));
        var itemId = item.GetProperty("id").GetInt32();

        var created = await _client.PostAsJsonAsync("/api/attachment", new
        {
            itemId,
            type = "Photo",
            fileName = "cascade.png",
            uploadBase64 = "data:image/png;base64," + Convert.ToBase64String(Png(16, 16)),
        });
        created.EnsureSuccessStatusCode();
        var attachment = await created.Content.ReadFromJsonAsync<JsonElement>(Json);
        var attachmentId = attachment.GetProperty("id").GetInt32();
        var path = Path.Combine(_factory.MediaRoot, attachment.GetProperty("filePath").GetString()!
            .Replace('/', Path.DirectorySeparatorChar));
        Assert.That(File.Exists(path), Is.True);

        var deleted = await _client.DeleteAsync($"/api/item/{itemId}");
        Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(path), Is.False, "the file goes with the record");
            Assert.That(
                _client.GetAsync($"/api/attachment/item/{itemId}").GetAwaiter().GetResult().StatusCode,
                Is.EqualTo(HttpStatusCode.NoContent), "and so does the row");
        });
    }

    [Test]
    public async Task GetAttachmentFile_SupportsARangeRequest()
    {
        var item = await PostItem(Unique("Ranged"));
        var itemId = item.GetProperty("id").GetInt32();
        var bytes = Png(20, 20);

        var created = await _client.PostAsJsonAsync("/api/attachment", new
        {
            itemId,
            type = "Photo",
            fileName = "range.png",
            uploadBase64 = "data:image/png;base64," + Convert.ToBase64String(bytes),
        });
        created.EnsureSuccessStatusCode();
        var attachment = await created.Content.ReadFromJsonAsync<JsonElement>(Json);
        var id = attachment.GetProperty("id").GetInt32();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/attachment/{id}/file");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 9);
        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsByteArrayAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.PartialContent));
            Assert.That(response.Content.Headers.ContentRange?.Length, Is.EqualTo(bytes.Length));
            Assert.That(body, Has.Length.EqualTo(10));
        });
    }

    [Test]
    public async Task GetAttachmentsByItem_ReturnsTheRows_OrNoContent()
    {
        var empty = await PostItem(Unique("NoFiles"));
        var emptyId = empty.GetProperty("id").GetInt32();
        Assert.That((await _client.GetAsync($"/api/attachment/item/{emptyId}")).StatusCode,
            Is.EqualTo(HttpStatusCode.NoContent));

        var item = await PostItem(Unique("Files"));
        var itemId = item.GetProperty("id").GetInt32();
        (await _client.PostAsJsonAsync("/api/attachment", new
        {
            itemId,
            type = "User Manual",
            fileName = "manual.pdf",
            uploadBase64 = "data:application/pdf;base64," + Convert.ToBase64String(new byte[] { 1, 2, 3 }),
        })).EnsureSuccessStatusCode();

        var list = await _client.GetFromJsonAsync<JsonElement>($"/api/attachment/item/{itemId}", Json);
        Assert.Multiple(() =>
        {
            Assert.That(list.GetArrayLength(), Is.EqualTo(1));
            Assert.That(list[0].GetProperty("fileName").GetString(), Is.EqualTo("manual.pdf"));
            Assert.That(list[0].GetProperty("itemId").GetInt32(), Is.EqualTo(itemId));
        });
    }

    [Test]
    public async Task GetConstantsByGroup_ReturnsOnlyThatGroup()
    {
        var attributes = await _client.GetFromJsonAsync<JsonElement>("/api/constants/group/Attribute", Json);

        Assert.That(attributes.GetArrayLength(), Is.GreaterThan(0));
        Assert.That(
            attributes.EnumerateArray().Select(c => c.GetProperty("group").GetString()).Distinct(),
            Is.EquivalentTo(new[] { "Attribute" }));
    }

    [Test]
    public async Task PostItem_WithALabelBeyondTheColumn_IsRejected()
    {
        var response = await _client.PostAsJsonAsync("/api/item", new
        {
            label = new string('x', 1001),
            icon = "",
            attributes = "{}",
            children = Array.Empty<object>(),
            attachments = Array.Empty<object>(),
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
            "the label column holds 1000 characters");
    }

    [Test]
    public async Task PostAttachment_WithoutAPayload_IsRejected()
    {
        var item = await PostItem(Unique("NoPayload"));
        var itemId = item.GetProperty("id").GetInt32();

        var response = await _client.PostAsJsonAsync("/api/attachment", new
        {
            itemId,
            type = "Photo",
            fileName = "nothing.png",
            uploadBase64 = "%%%not-base64%%%",
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }


    [Test]
    public async Task PostAttachmentMultipart_StoresTheFileAndServesItBack()
    {
        var item = await PostItem(Unique("Multipart"));
        var itemId = item.GetProperty("id").GetInt32();
        var bytes = Png(48, 32);

        using var form = new MultipartFormDataContent
        {
            { new StringContent(itemId.ToString()), "itemId" },
            { new StringContent("Photo"), "type" },
            { new StringContent("streamed.png"), "fileName" },
            { new ByteArrayContent(bytes), "file", "streamed.png" },
        };

        var response = await _client.PostAsync("/api/attachment/upload", form);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var attachment = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        var id = attachment.GetProperty("id").GetInt32();

        Assert.Multiple(() =>
        {
            Assert.That(attachment.GetProperty("fileName").GetString(), Is.EqualTo("streamed.png"));
            Assert.That(attachment.GetProperty("filePath").GetString(), Does.StartWith($"media/items/{itemId}/"));
            Assert.That(attachment.GetProperty("thumbnailBase64").GetString(), Is.Not.Null.And.Not.Empty);
        });

        var file = await _client.GetAsync($"/api/attachment/{id}/file");
        file.EnsureSuccessStatusCode();
        Assert.That(
            Convert.ToHexString(SHA256.HashData(await file.Content.ReadAsByteArrayAsync())),
            Is.EqualTo(Convert.ToHexString(SHA256.HashData(bytes))),
            "what was uploaded is what comes back");
    }

    [Test]
    public async Task PostAttachmentMultipart_WithoutAFile_IsRejected()
    {
        var item = await PostItem(Unique("NoFile"));
        var itemId = item.GetProperty("id").GetInt32();

        using var form = new MultipartFormDataContent
        {
            { new StringContent(itemId.ToString()), "itemId" },
            { new StringContent("Photo"), "type" },
        };

        var response = await _client.PostAsync("/api/attachment/upload", form);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task PostAttachmentMultipart_AboveTheLimit_IsRejected()
    {
        var item = await PostItem(Unique("TooBig"));
        var itemId = item.GetProperty("id").GetInt32();
        // One byte over the 20 MB cap: the API must refuse before it writes anything.
        var oversized = new byte[AttachmentStorage.MaxBytes + 1];

        using var form = new MultipartFormDataContent
        {
            { new StringContent(itemId.ToString()), "itemId" },
            { new StringContent("Photo"), "type" },
            { new ByteArrayContent(oversized), "file", "huge.png" },
        };

        var response = await _client.PostAsync("/api/attachment/upload", form);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(
              _client.GetAsync($"/api/attachment/item/{itemId}").GetAwaiter().GetResult().StatusCode,
              Is.EqualTo(HttpStatusCode.NoContent), "nothing was stored");
        });
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

    /// <summary>
    /// A cycle that already reached the database must not take the API down. The controllers refuse
    /// to create one, but an import, a maintenance UPDATE or a database written before the guard
    /// existed does not go through them, so the projection is the last line of defence. This writes
    /// a three-node cycle straight into SQLite for exactly that reason: a two-node cycle is not
    /// enough to prove anything, it is the shape the tree walk already handled by accident.
    /// </summary>
    [Test]
    public async Task GetItems_SurvivesAThreeNodeCycleThatReachedTheDatabase()
    {
        var a = (await PostItem(Unique("PoisonA"))).GetProperty("id").GetInt32();
        var b = (await PostItem(Unique("PoisonB"), a)).GetProperty("id").GetInt32();
        var c = (await PostItem(Unique("PoisonC"), b)).GetProperty("id").GetInt32();

        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
            await context.Database.ExecuteSqlRawAsync("UPDATE Items SET ParentId = {0} WHERE Id = {1}", c, a);
            await context.Database.ExecuteSqlRawAsync("UPDATE Items SET ParentId = {0} WHERE Id = {1}", a, b);
            await context.Database.ExecuteSqlRawAsync("UPDATE Items SET ParentId = {0} WHERE Id = {1}", b, c);
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var response = await _client.GetAsync("/api/item", timeout.Token);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "a cycle in the data is a projection problem, not a reason for the process to die");

        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(Json, timeout.Token);
        var ids = new List<int>();
        CollectIds(payload.GetProperty("tree"), ids);
        Assert.That(ids, Does.Not.Contain(0), "the walk still returns nodes");
        Assert.That(ids.Count, Is.EqualTo(ids.Distinct().Count()), "no record is rendered twice");
    }

    /// <summary>
    /// The tree advertises a `parent` object per node; carrying the ancestors through that second
    /// recursion is what keeps deep cycles from looping, and this holds the contract in place so the
    /// guard cannot be "fixed" by nulling the field.
    /// </summary>
    [Test]
    public async Task GetItems_RendersTheParentOfAChild()
    {
        var parentId = (await PostItem(Unique("ParentChain"))).GetProperty("id").GetInt32();
        var childLabel = Unique("ChildChain");
        await PostItem(childLabel, parentId);

        var payload = await _client.GetFromJsonAsync<JsonElement>("/api/item", Json);
        var child = payload.GetProperty("tree").EnumerateArray()
            .SelectMany(node => node.GetProperty("children").EnumerateArray())
            .Single(node => node.GetProperty("label").GetString() == childLabel);

        Assert.That(child.TryGetProperty("parent", out var parent), Is.True, "a child carries its parent");
        Assert.That(parent.ValueKind, Is.EqualTo(JsonValueKind.Object), "and it is the object, not a bare id");
        Assert.That(parent.GetProperty("id").GetInt32(), Is.EqualTo(parentId));
    }

    /// <summary>
    /// The cycle guard has to see soft-deleted rows: a record out of the tree still carries a parent,
    /// and a loop closed through it would be resurrected the moment it is reactivated. This is the
    /// only way to persist a cycle while the guards are in place, so it is also the regression test
    /// for the guard reading the same rows the tree can eventually reach.
    /// </summary>
    [Test]
    public async Task PutItem_DoesNotCreateCycleThroughHardDeletedRecord()
    {
        var a = (await PostItem(Unique("HiddenA"))).GetProperty("id").GetInt32();
        var b = (await PostItem(Unique("HiddenB"), a)).GetProperty("id").GetInt32();
        var c = (await PostItem(Unique("HiddenC"), b)).GetProperty("id").GetInt32();

        // B is hard-deleted and C is detached from it, so the former relationship cannot close a cycle.
        var deleted = await _client.DeleteAsync($"/api/item/{b}");
        Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var response = await _client.PutAsJsonAsync($"/api/item/{a}", new
        {
            id = a,
            label = Unique("HiddenA-edited"),
            icon = "",
            parentId = c,
            attributes = "{}",
            children = Array.Empty<object>(),
            attachments = Array.Empty<object>(),
        });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent),
            "the detached child is now a root and no cycle exists: " + await response.Content.ReadAsStringAsync());
    }

    private static void CollectIds(JsonElement nodes, List<int> ids)
    {
        foreach (var node in nodes.EnumerateArray())
        {
            ids.Add(node.GetProperty("id").GetInt32());
            if (node.TryGetProperty("children", out var children) && children.GetArrayLength() > 0)
            {
                CollectIds(children, ids);
            }
        }
    }
}
