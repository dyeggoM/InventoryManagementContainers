using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IMCAPI.DB;
using IMCAPI.DB.Entities;
using IMCAPI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Testing;

/// <summary>
/// The archive is a ledger that only grows: these tests pin the two things that keep it usable — a
/// page does not count the whole table, and an explicit, configured purge can drop what has expired.
/// </summary>
[NonParallelizable]
[TestFixture]
public sealed class ArchiveRetentionTests : ApiTestBase
{

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

        var response = await Client.PostAsync("/api/item/archive/purge?olderThanDays=30", content: null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.That(payload.GetProperty("deleted").GetInt32(), Is.GreaterThanOrEqualTo(1));

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        Assert.That(await context.ItemArchives.AnyAsync(row => row.Label == "expired"), Is.False);
        Assert.That(await context.ItemArchives.AnyAsync(row => row.Label == "recent"), Is.True,
            "a row inside the window stays");
        Assert.That(await context.AuditLogs.AnyAsync(row => row.Action == "ArchivePurge"), Is.True);

        var again = await Client.PostAsync("/api/item/archive/purge?olderThanDays=30", content: null);
        Assert.That((await again.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("deleted").GetInt32(), Is.EqualTo(0),
            "a second purge finds nothing and says so");
    }

    [Test]
    public async Task Purge_WithNoQueryValueUsesTheOneMonthDefault()
    {
        await AddArchiveRow("expired-default", DateTime.UtcNow.AddDays(-40));
        await AddArchiveRow("inside-default", DateTime.UtcNow.AddDays(-5));

        var response = await Client.PostAsync("/api/item/archive/purge", content: null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(Json);

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var expiredGone = !await context.ItemArchives.AnyAsync(row => row.Label == "expired-default");
        var recentKept = await context.ItemArchives.AnyAsync(row => row.Label == "inside-default");

        Assert.Multiple(() =>
        {
            Assert.That(payload.GetProperty("olderThanDays").GetInt32(), Is.EqualTo(30),
                "the owner's policy is one month, so nobody has to remember to pass it");
            Assert.That(expiredGone, Is.True);
            Assert.That(recentKept, Is.True);
        });
    }

    [Test]
    public async Task TheSweeperPurgesWhatTheEndpointWould()
    {
        // The endpoint only runs when somebody calls it, which makes a retention policy a document.
        // The sweeper is the clock: same rule, same audit row, no request.
        await AddArchiveRow("swept-away", DateTime.UtcNow.AddDays(-45));
        await AddArchiveRow("swept-past", DateTime.UtcNow.AddDays(-1));

        var deleted = await Factory.Services.GetRequiredService<ArchiveRetentionSweeper>()
            .SweepOnceAsync();

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var sweptAway = await context.ItemArchives.AnyAsync(row => row.Label == "swept-away");
        var sweptPast = await context.ItemArchives.AnyAsync(row => row.Label == "swept-past");
        var audited = await context.AuditLogs.AnyAsync(row => row.Action == "ArchivePurge"
            && row.AfterJson!.Contains("archive-retention"));

        Assert.Multiple(() =>
        {
            Assert.That(deleted, Is.GreaterThanOrEqualTo(1));
            Assert.That(sweptAway, Is.False);
            Assert.That(sweptPast, Is.True);
            Assert.That(audited, Is.True, "the sweep says who purged, so it is not an anonymous delete");
        });
    }

    [Test]
    public void TheAgeResolvesInTheDocumentedOrder()
    {
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();

        Assert.Multiple(() =>
        {
            Assert.That(ArchiveRetention.ResolveDays(null, configuration), Is.EqualTo(30),
                "no configuration means one month, not 'never'");
            Assert.That(ArchiveRetention.ResolveDays(7, configuration), Is.EqualTo(7),
                "an explicit request wins");
            Assert.That(ArchiveRetention.ResolveDays(0, configuration), Is.EqualTo(0),
                "zero is a request too, and the endpoint refuses it");
        });
    }

    [Test]
    public async Task Purge_IsSuperadminOnly()
    {
        var created = await Client.PostAsJsonAsync("/api/users",
            new { email = $"purge-reader-{Guid.NewGuid():N}@example.test", roles = new[] { "admin" }, password = "PurgeTest!1" });
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created), await created.Content.ReadAsStringAsync());
        var email = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("email").GetString();

        using var reader = await LoginAs(email!, "PurgeTest!1");

        var response = await reader.PostAsync("/api/item/archive/purge?olderThanDays=30", content: null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden),
            "an admin may restore, but the ledger is the superadmin's");
    }

    private async Task<JsonElement> GetArchive(int page, int pageSize)
    {
        var response = await Client.GetAsync($"/api/item/archive?page={page}&pageSize={pageSize}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    /// <summary>Writes a ledger row straight to the database: the API only archives by deleting an item.</summary>
    private async Task AddArchiveRow(string label, DateTime deletedAtUtc)
    {
        using var scope = Factory.Services.CreateScope();
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
