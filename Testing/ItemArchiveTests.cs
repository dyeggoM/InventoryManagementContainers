using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IMCAPI.DB;
using IMCAPI.DB.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Testing;

[NonParallelizable]
[TestFixture]
public sealed class ItemArchiveTests
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
        var login = await _client.PostAsJsonAsync("/api/auth/login", new { email = "bootstrap@example.test", password = Password });
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
    public async Task Delete_ArchivesItemAndAttachmentsAndRemovesItFromReads()
    {
        var id = await CreateItem("archivable-" + Guid.NewGuid().ToString("N"));
        const string fileName = "archive-photo.jpg";
        const string filePath = "media/items/archive/archive-photo.jpg";
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
            context.Attachments.Add(new Attachment { ItemId = id, FileName = fileName, Type = "Photo", FilePath = filePath });
            await context.SaveChangesAsync();
        }

        var response = await _client.DeleteAsync($"/api/item/{id}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), await response.Content.ReadAsStringAsync());
        Assert.That((await _client.GetAsync($"/api/item/{id}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        var list = await _client.GetFromJsonAsync<JsonElement>("/api/item", Json);
        Assert.That(list.GetProperty("panels").EnumerateArray().Any(item => item.GetProperty("id").GetInt32() == id), Is.False);

        var archiveResponse = await _client.GetAsync($"/api/item/archive?label=archivable&page=1&pageSize=10");
        Assert.That(archiveResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), await archiveResponse.Content.ReadAsStringAsync());
        var page = await archiveResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.That(page.GetProperty("total").GetInt32(), Is.GreaterThanOrEqualTo(1));
        var archived = page.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("itemId").GetInt32() == id);
        Assert.Multiple(() =>
        {
            Assert.That(archived.GetProperty("deletedAtUtc").GetDateTime(), Is.Not.EqualTo(default(DateTime)));
            Assert.That(archived.GetProperty("deletedBy").GetString(), Is.EqualTo("bootstrap@example.test"));
            Assert.That(archived.GetProperty("attachmentsJson").GetString(), Does.Contain(fileName).And.Contain("Photo").And.Contain(filePath));
        });

        using var scopeAfter = _factory.Services.CreateScope();
        var db = scopeAfter.ServiceProvider.GetRequiredService<ApplicationContext>();
        Assert.That(await db.Items.IgnoreQueryFilters().AnyAsync(item => item.Id == id), Is.False);
        Assert.That(await db.Attachments.IgnoreQueryFilters().AnyAsync(attachment => attachment.ItemId == id), Is.False);
    }

    [Test]
    public async Task Archive_WithoutToken_ReturnsUnauthorized()
    {
        using var anonymous = _factory.CreateClient();
        var response = await anonymous.GetAsync("/api/item/archive");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Activate_RestoresMostRecentArchiveAndRecordsRestoreAudit()
    {
        var id = await CreateItem("restorable-" + Guid.NewGuid().ToString("N"));
        Assert.That((await _client.DeleteAsync($"/api/item/{id}")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var response = await _client.PatchAsync($"/api/item/{id}/activate", content: null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var restored = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Multiple(() =>
        {
            Assert.That(restored.GetProperty("id").GetInt32(), Is.EqualTo(id));
            Assert.That(restored.GetProperty("isActive").GetBoolean(), Is.True);
            Assert.That(restored.GetProperty("label").GetString(), Does.StartWith("restorable-"));
        });
        Assert.That((await _client.GetAsync($"/api/item/{id}")).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var actor = await scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>()
            .FindByEmailAsync("bootstrap@example.test");
        var archivedResponse = await _client.GetAsync($"/api/item/archive?actor={actor!.Id}&label=restorable");
        Assert.That(archivedResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var archivedPage = await archivedResponse.Content.ReadFromJsonAsync<JsonElement>(Json);
        var archive = archivedPage.GetProperty("items").EnumerateArray()
            .Single(row => row.GetProperty("itemId").GetInt32() == id);
        Assert.Multiple(() =>
        {
            Assert.That(archive.GetProperty("restoredAtUtc").GetDateTime(), Is.Not.EqualTo(default(DateTime)));
            Assert.That(archive.GetProperty("restoredById").GetString(), Is.EqualTo(actor.Id));
        });
        Assert.That(await context.AuditLogs.AnyAsync(row => row.Action == "Restore" && row.Entity == "Item" && row.EntityId == id.ToString()), Is.True);
    }

    [Test]
    public async Task Activate_WhenItemHasNoUnrestoredArchive_ReturnsNotFound()
    {
        var response = await _client.PatchAsync($"/api/item/{int.MaxValue}/activate", content: null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task ItemIsActive_IsServerOwnedOnPostAndPut()
    {
        var create = await _client.PostAsJsonAsync("/api/item", new
        {
            label = "active-on-create-" + Guid.NewGuid().ToString("N"), icon = "", parentId = (int?)null,
            attributes = "{}", isActive = false, children = Array.Empty<object>(), attachments = Array.Empty<object>()
        });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.OK), await create.Content.ReadAsStringAsync());
        var item = await create.Content.ReadFromJsonAsync<JsonElement>(Json);
        var id = item.GetProperty("id").GetInt32();
        Assert.That(item.GetProperty("isActive").GetBoolean(), Is.True);

        var update = await _client.PutAsJsonAsync($"/api/item/{id}", new
        {
            id, label = item.GetProperty("label").GetString(), icon = "", parentId = (int?)null,
            attributes = "{}", isActive = false, children = Array.Empty<object>(), attachments = Array.Empty<object>()
        });
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        var read = await _client.GetFromJsonAsync<JsonElement>($"/api/item/{id}", Json);
        Assert.That(read.GetProperty("isActive").GetBoolean(), Is.True);
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
}
