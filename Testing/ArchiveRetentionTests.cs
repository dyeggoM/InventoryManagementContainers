using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IMCAPI.DB;
using IMCAPI.DB.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Testing;

/// <summary>
/// The archive is a ledger that only grows: these tests pin the two things that keep it usable — a
/// page does not count the whole table, and an explicit, configured purge can drop what has expired.
/// </summary>
[NonParallelizable]
[TestFixture]
public sealed class ArchiveRetentionTests
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
    public async Task ArchivePage_CountsOnceAndEveryPageSaysWhetherAnotherExists()
    {
        for (var index = 0; index < 3; index++) await AddArchiveRow($"paged-{index}", DateTime.UtcNow.AddMinutes(-index));

        var first = await GetArchive(page: 1, pageSize: 2);
        var second = await GetArchive(page: 2, pageSize: 2);

        Assert.Multiple(() =>
        {
            Assert.That(first.GetProperty("items").GetArrayLength(), Is.EqualTo(2));
            Assert.That(first.GetProperty("total").GetInt32(), Is.GreaterThanOrEqualTo(3),
                "the first page carries the total once");
            Assert.That(first.GetProperty("hasMore").GetBoolean(), Is.True);

            Assert.That(second.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
            // The serializer drops a null property, so "absent" is the wire form of "not counted here".
            Assert.That(second.TryGetProperty("total", out var laterTotal), Is.False,
                "a later page does not count the table again");
            Assert.That(laterTotal.ValueKind, Is.EqualTo(JsonValueKind.Undefined));
            Assert.That(second.GetProperty("hasMore").GetBoolean(), Is.False);
        });
    }

    [Test]
    public async Task Purge_RemovesOnlyTheRowsOlderThanTheCutoff_AndIsAudited()
    {
        await AddArchiveRow("expired", DateTime.UtcNow.AddDays(-40));
        await AddArchiveRow("recent", DateTime.UtcNow.AddDays(-2));

        var response = await _client.PostAsync("/api/item/archive/purge?olderThanDays=30", content: null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.That(payload.GetProperty("deleted").GetInt32(), Is.GreaterThanOrEqualTo(1));

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        Assert.That(await context.ItemArchives.AnyAsync(row => row.Label == "expired"), Is.False);
        Assert.That(await context.ItemArchives.AnyAsync(row => row.Label == "recent"), Is.True,
            "a row inside the window stays");
        Assert.That(await context.AuditLogs.AnyAsync(row => row.Action == "ArchivePurge"), Is.True);

        var again = await _client.PostAsync("/api/item/archive/purge?olderThanDays=30", content: null);
        Assert.That((await again.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("deleted").GetInt32(), Is.EqualTo(0),
            "a second purge finds nothing and says so");
    }

    [Test]
    public async Task Purge_WithoutAConfiguredRetentionOrAQueryValue_IsRefused()
    {
        var response = await _client.PostAsync("/api/item/archive/purge", content: null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
            "no retention configured and nothing asked for is not a licence to delete everything");
    }

    [Test]
    public async Task Purge_IsSuperadminOnly()
    {
        using var reader = _factory.CreateClient();
        var created = await _client.PostAsJsonAsync("/api/users",
            new { email = $"purge-reader-{Guid.NewGuid():N}@example.test", roles = new[] { "admin" }, password = "PurgeTest!1" });
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created), await created.Content.ReadAsStringAsync());
        var email = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("email").GetString();

        var login = await reader.PostAsJsonAsync("/api/auth/login", new { email, password = "PurgeTest!1" });
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK), await login.Content.ReadAsStringAsync());
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("access_token").GetString();
        reader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await reader.PostAsync("/api/item/archive/purge?olderThanDays=30", content: null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden),
            "an admin may restore, but the ledger is the superadmin's");
    }

    private async Task<JsonElement> GetArchive(int page, int pageSize)
    {
        var response = await _client.GetAsync($"/api/item/archive?page={page}&pageSize={pageSize}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    /// <summary>Writes a ledger row straight to the database: the API only archives by deleting an item.</summary>
    private async Task AddArchiveRow(string label, DateTime deletedAtUtc)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        context.ItemArchives.Add(new ItemArchive
        {
            ItemId = Random.Shared.Next(1000, 999999),
            Label = label,
            Icon = "",
            DeletedAtUtc = deletedAtUtc,
            DeletedBy = "test",
            IsActive = true,
        });
        await context.SaveChangesAsync();
    }
}
