using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IMCAPI.DB;
using IMCAPI.DB.Entities;
using IMCAPI.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Testing;

[TestFixture]
public sealed class ItemSetTests : ApiTestBase
{
    protected override bool FreshApiPerTest => true;
    private const int Persona = OrganizationConfiguration.DefaultTenantId;
    private const int LiveRoom = 900002;

    private async Task<(int Id, string Json)> CreateSet()
    {
        var response = await Client.PostAsJsonAsync("/api/item", new
        {
            label = "DRUM SET", icon = "", attributes = "{\"Type\":\"Set\"}",
            children = Array.Empty<object>(), attachments = Array.Empty<object>()
        });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var json = await response.Content.ReadAsStringAsync();
        return (JsonDocument.Parse(json).RootElement.GetProperty("id").GetInt32(), json);
    }

    private async Task<HttpResponseMessage> Move(int parent, params int[] ids) =>
        await Client.PostAsJsonAsync($"/api/item/{parent}/children", new { ids });

    private async Task<(int? ParentId, bool IsActive, int TenantId, string? Attributes,
        string? UpdatedBy, DateTime? UpdatedAt)> Row(int id)
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var row = await db.Items.IgnoreQueryFilters().AsNoTracking().SingleAsync(item => item.Id == id);
        return (row.ParentId, row.IsActive, row.TenantId, row.Attributes, row.UpdatedBy, row.UpdatedAt);
    }

    [Test]
    public async Task MoveThreeAndDetachOne_ReadsBackTreeAndKeepsAllPieces()
    {
        var (set, createdJson) = await CreateSet();
        var ids = new[] { await CreateItem("KICK"), await CreateItem("SNARE"), await CreateItem("TOM") };
        var moved = await Move(set, ids);
        Assert.That(moved.StatusCode, Is.EqualTo(HttpStatusCode.OK), await moved.Content.ReadAsStringAsync());
        var movedJson = await moved.Content.ReadAsStringAsync();
        Assert.That(JsonDocument.Parse(movedJson).RootElement.GetProperty("moved").GetInt32(), Is.EqualTo(3));

        var detail = await Client.GetAsync($"/api/item/{set}");
        Assert.That(detail.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var treeJson = await detail.Content.ReadAsStringAsync();
        var children = JsonDocument.Parse(treeJson).RootElement.GetProperty("children");
        Assert.That(children.GetArrayLength(), Is.EqualTo(3));
        Assert.That(children.EnumerateArray().Select(row => row.GetProperty("id").GetInt32()), Is.EquivalentTo(ids));
        foreach (var id in ids)
            Assert.That((await Row(id)).ParentId, Is.EqualTo(set));

        var detached = await Client.DeleteAsync($"/api/item/{set}/children/{ids[0]}");
        Assert.That(detached.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        var detachedRow = await Row(ids[0]);
        Assert.That(detachedRow.ParentId, Is.Null);
        Assert.That(detachedRow.IsActive, Is.True);
        Assert.That(detachedRow.TenantId, Is.EqualTo(Persona));
        Assert.That((await Client.GetAsync($"/api/item/{ids[0]}")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var after = await Client.GetFromJsonAsync<JsonElement>($"/api/item/{set}", Json);
        Assert.That(after.GetProperty("children").GetArrayLength(), Is.EqualTo(2));
        TestContext.Out.WriteLine("set-create HTTP 200 " + createdJson);
        TestContext.Out.WriteLine("set-attach HTTP 200 " + movedJson);
        TestContext.Out.WriteLine("set-tree HTTP 200 " + treeJson);
        TestContext.Out.WriteLine("set-detach HTTP 204 " + JsonSerializer.Serialize(new { body = "", detachedId = ids[0] }));
        TestContext.Out.WriteLine("set-after HTTP 200 " + after.GetRawText());
    }

    [Test]
    public async Task CycleRefusesWholeBatchWithoutMovingAnyRow()
    {
        var ancestor = await CreateItem("ANCESTOR");
        var (set, _) = await CreateSet();
        var attached = await Move(ancestor, set);
        Assert.That(attached.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var valid = await CreateItem("VALID");
        var refused = await Move(set, valid, ancestor);
        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await refused.Content.ReadAsStringAsync(), Does.Contain(ancestor.ToString()));
        Assert.That((await Row(valid)).ParentId, Is.Null);
        Assert.That((await Row(ancestor)).ParentId, Is.Null);
        Assert.That((await Row(set)).ParentId, Is.EqualTo(ancestor));
    }

    [Test]
    public async Task ForeignChildRefusesWholeBatchAndNamesTheId()
    {
        var (set, _) = await CreateSet();
        var valid = await CreateItem("VALID");
        int foreign;
        using (var scope = Factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantScope>().SelectSystemTenant(LiveRoom);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
            var item = new Item { Label = "OTHER TENANT" };
            db.Items.Add(item);
            await db.SaveChangesAsync();
            foreign = item.Id;
        }
        var refused = await Move(set, valid, foreign);
        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await refused.Content.ReadAsStringAsync(), Does.Contain(foreign.ToString()));
        Assert.That((await Row(valid)).ParentId, Is.Null);
        Assert.That((await Row(foreign)).ParentId, Is.Null);
        Assert.That((await Row(foreign)).TenantId, Is.EqualTo(LiveRoom));
    }

    [Test]
    public async Task SelfMissingAndDuplicateIdsRefuseWholeBatch()
    {
        var (set, _) = await CreateSet();
        var piece = await CreateItem("VALID PIECE");
        foreach (var invalid in new[] { set, 999999, piece })
        {
            var refused = await Move(set, piece, invalid);
            Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await refused.Content.ReadAsStringAsync(), Does.Contain(invalid.ToString()));
            Assert.That((await Row(piece)).ParentId, Is.Null);
        }
    }

    [Test]
    public async Task MoveRefusesToExceedConfiguredHierarchyDepth()
    {
        var parent = await CreateItem("ROOT");
        for (var level = 1; level < 12; level++)
            parent = await CreateItem($"LEVEL {level}", parent);
        var (set, _) = await CreateSet();
        var piece = await CreateItem("SUBTREE PIECE", set);
        var refused = await Move(parent, set);
        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await refused.Content.ReadAsStringAsync(), Does.Contain("maximum allowed depth"));
        Assert.That((await Row(set)).ParentId, Is.Null);
        Assert.That((await Row(piece)).ParentId, Is.EqualTo(set));
    }

    [Test]
    public async Task DetachWrongParentReturnsNotFoundAndDoesNotChangePiece()
    {
        var (set, _) = await CreateSet();
        var other = await CreateItem("OTHER PARENT");
        var piece = await CreateItem("PIECE", set);
        var refused = await Client.DeleteAsync($"/api/item/{other}/children/{piece}");
        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await Row(piece)).ParentId, Is.EqualTo(set));
    }

    [Test]
    public async Task ForeignParentIsNotFoundForBothRoutes()
    {
        var ownPiece = await CreateItem("OWN PIECE");
        int foreignParent;
        using (var scope = Factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantScope>().SelectSystemTenant(LiveRoom);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
            var item = new Item { Label = "FOREIGN SET" };
            db.Items.Add(item);
            await db.SaveChangesAsync();
            foreignParent = item.Id;
        }
        Assert.That((await Move(foreignParent, ownPiece)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await Client.DeleteAsync($"/api/item/{foreignParent}/children/{ownPiece}")).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await Row(ownPiece)).ParentId, Is.Null);
    }

    [Test]
    public async Task LectorCannotAttachOrDetach()
    {
        var (set, _) = await CreateSet();
        var piece = await CreateItem("PIECE", set);
        using var lector = await CreateAccountAndLogin("sets-reader@example.test", "lector");
        var attach = await lector.PostAsJsonAsync($"/api/item/{set}/children", new { ids = new[] { piece } });
        var detach = await lector.DeleteAsync($"/api/item/{set}/children/{piece}");
        Assert.That((attach.StatusCode, detach.StatusCode), Is.EqualTo((HttpStatusCode.Forbidden, HttpStatusCode.Forbidden)));
        Assert.That((await Row(piece)).ParentId, Is.EqualTo(set));
    }

    [Test]
    public async Task MoveAndDetachStampTheActorWithoutChangingInventoryFields()
    {
        var (set, _) = await CreateSet();
        var response = await Client.PostAsJsonAsync("/api/item", new
        {
            label = "STRAP ACCESSORY", attributes = "{\"Type\":\"Accessory\"}", icon = "",
            children = Array.Empty<object>(), attachments = Array.Empty<object>()
        });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var piece = (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();
        var before = await Row(piece);
        using var writer = await CreateAccountAndLogin("sets-writer@example.test", "admin");
        var moved = await writer.PostAsJsonAsync($"/api/item/{set}/children", new { ids = new[] { piece } });
        Assert.That(moved.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var after = await Row(piece);
        Assert.That((after.IsActive, after.TenantId, after.Attributes),
            Is.EqualTo((before.IsActive, before.TenantId, before.Attributes)));
        Assert.That(after.ParentId, Is.EqualTo(set));
        Assert.That(after.UpdatedAt, Is.GreaterThan(before.UpdatedAt));
        Assert.That(after.UpdatedBy, Is.EqualTo("sets-writer@example.test"));
        using var detacher = await CreateAccountAndLogin("sets-detacher@example.test", "admin");
        var detached = await detacher.DeleteAsync($"/api/item/{set}/children/{piece}");
        Assert.That(detached.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        var final = await Row(piece);
        Assert.That(final.ParentId, Is.Null);
        Assert.That((final.IsActive, final.TenantId, final.Attributes),
            Is.EqualTo((before.IsActive, before.TenantId, before.Attributes)));
        Assert.That(final.UpdatedAt, Is.GreaterThan(after.UpdatedAt));
        Assert.That(final.UpdatedBy, Is.EqualTo("sets-detacher@example.test"));
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var audit = await db.AuditLogs.IgnoreQueryFilters().Where(row => row.Entity == "Item"
            && row.EntityId == piece.ToString() && row.Action == "Update")
            .OrderBy(row => row.Id).ToListAsync();
        Assert.That(audit.Select(row => row.ActorEmail), Is.EqualTo(new[]
        {
            "sets-writer@example.test", "sets-detacher@example.test"
        }));
    }

    [Test]
    public async Task FreshHostHasSetAndAccessoryWithTenantSpecificTypeParents()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var roots = await db.Constants.IgnoreQueryFilters().Where(row => row.Group == "Type" && row.Label == "Type")
            .ToDictionaryAsync(row => row.TenantId, row => row.Id);
        var kinds = await db.Constants.IgnoreQueryFilters().Where(row => row.Group == "Type"
            && (row.Label == "Set" || row.Label == "Accessory"))
            .OrderBy(row => row.TenantId).ThenBy(row => row.Label).ToListAsync();
        foreach (var tenant in new[] { Persona, LiveRoom, 900003 })
        {
            Assert.That(kinds.Where(row => row.TenantId == tenant).Select(row => row.Label),
                Is.EquivalentTo(new[] { "Set", "Accessory" }));
            Assert.That(kinds.Where(row => row.TenantId == tenant).Select(row => row.ParentId),
                Is.All.EqualTo(roots[tenant]));
            TestContext.Out.WriteLine($"fresh startup SELECT Label FROM Constants WHERE \"Group\"='Type' AND TenantId={tenant}: " +
                JsonSerializer.Serialize(await db.Constants.IgnoreQueryFilters().Where(row => row.Group == "Type"
                    && row.TenantId == tenant).OrderBy(row => row.Label).Select(row => row.Label).ToArrayAsync()));
        }
        Assert.That(kinds, Has.Count.EqualTo(6));
        TestContext.Out.WriteLine("SELECT Label FROM Constants WHERE \"Group\"='Type' AND TenantId=900002: " +
            JsonSerializer.Serialize(await db.Constants.IgnoreQueryFilters()
                .Where(row => row.Group == "Type" && row.TenantId == LiveRoom)
                .OrderBy(row => row.Label).Select(row => row.Label).ToArrayAsync()));
    }

    [Test]
    public void FreshMigrationLeavesCatalogForStartupSeeder()
    {
        var root = Path.Combine(Path.GetTempPath(), "item-set-fresh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var options = new DbContextOptionsBuilder<ApplicationContext>()
                .UseSqlite($"Data Source={Path.Combine(root, "test.db")}").Options;
            using var db = new ApplicationContext(options, new TenantScope(new Microsoft.AspNetCore.Http.HttpContextAccessor()));
            db.Database.Migrate();
            Assert.That(db.Constants.IgnoreQueryFilters().Count(row => row.Group == "Type"), Is.Zero,
                "a fresh migration must not create provisional Type rows");
            var contentRoot = Factory.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath;
            Assert.That(DatabaseSeeder.SeedConfiguration(db, contentRoot), Is.True);
            foreach (var tenant in new[] { Persona, LiveRoom, 900003 })
                Assert.That(db.Constants.IgnoreQueryFilters().Count(row => row.TenantId == tenant
                    && row.Group == "Type" && (row.Label == "Set" || row.Label == "Accessory")), Is.EqualTo(2));
            Assert.That(DatabaseSeeder.SeedConfiguration(db, contentRoot), Is.False);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void ExistingCatalogMigrationAddsAndDownRemovesBothKinds()
    {
        var root = Path.Combine(Path.GetTempPath(), "item-set-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var options = new DbContextOptionsBuilder<ApplicationContext>()
                .UseSqlite($"Data Source={Path.Combine(root, "test.db")}").Options;
            using var db = new ApplicationContext(options, new TenantScope(new Microsoft.AspNetCore.Http.HttpContextAccessor()));
            var migrator = db.Database.GetService<IMigrator>();
            const string previous = "20261117022147_Organizations";
            migrator.Migrate(previous);
            var contentRoot = Factory.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath;
            Assert.That(DatabaseSeeder.SeedConfiguration(db, contentRoot), Is.True);
            Assert.That(db.Constants.IgnoreQueryFilters().Count(row => row.Group == "Type" && row.Label == "Set"), Is.EqualTo(3),
                "the seed already knows Set on an upgraded catalog; remove them to simulate a pre-feature database");
            db.Database.ExecuteSqlRaw("DELETE FROM Constants WHERE \"Group\"='Type' AND Label IN ('Set','Accessory')");
            migrator.Migrate();
            foreach (var tenant in new[] { Persona, LiveRoom, 900003 })
            {
                var labels = db.Constants.IgnoreQueryFilters().Where(row => row.TenantId == tenant
                    && row.Group == "Type" && (row.Label == "Set" || row.Label == "Accessory"))
                    .OrderBy(row => row.Label).Select(row => row.Label).ToArray();
                TestContext.Out.WriteLine($"existing catalog + migration SELECT Label FROM Constants WHERE \"Group\"='Type' AND TenantId={tenant} AND Label IN ('Set','Accessory'): " + JsonSerializer.Serialize(labels));
                Assert.That(db.Constants.IgnoreQueryFilters().Count(row => row.TenantId == tenant
                    && row.Group == "Type" && (row.Label == "Set" || row.Label == "Accessory")), Is.EqualTo(2));
            }
            var duplicates = db.Database.SqlQueryRaw<int>("""
                SELECT COUNT(*) AS Value FROM Constants WHERE "Group" = 'Type'
                GROUP BY TenantId, Label HAVING COUNT(*) > 1
                """).ToArray();
            TestContext.Out.WriteLine("existing catalog + migration SELECT TenantId, Label FROM Constants WHERE \"Group\"='Type' GROUP BY TenantId, Label HAVING COUNT(*) > 1: " + JsonSerializer.Serialize(duplicates));
            Assert.That(duplicates, Is.Empty);
            migrator.Migrate(previous);
            Assert.That(db.Constants.IgnoreQueryFilters().Count(row => row.Group == "Type"
                && (row.Label == "Set" || row.Label == "Accessory")), Is.Zero);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }
}
