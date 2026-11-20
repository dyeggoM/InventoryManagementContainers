using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IMCAPI.DB;
using IMCAPI.DB.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using IMCAPI.Services;

namespace Testing;

/// <summary>
/// The code/label contract end to end: <c>Constants.Code</c> is the stable identity the stored item
/// data and the API key on; <c>Label</c> is display text and may be renamed or translated without
/// touching a single stored item. Renaming a label, refusing an unknown code and the two migration
/// routes (fresh seed and upgraded database) are what prove the design.
/// </summary>
[NonParallelizable]
[TestFixture]
public sealed class ConstantCatalogTests : ApiTestBase
{
    private const int Persona = OrganizationConfiguration.DefaultTenantId;

    private int BrandAttributeId()
    {
        using var scope = PersonaScope();
        return scope.ServiceProvider.GetRequiredService<ApplicationContext>().Constants.IgnoreQueryFilters()
            .Single(row => row.TenantId == Persona && row.Group == "Attribute" && row.Code == "Brand").Id;
    }

    [Test]
    public async Task RenamingTheLabelDoesNotBreakTheStoredItems()
    {
        // The item is created while the label still reads "Marca": the request speaks codes.
        var created = await Client.PostAsJsonAsync("/api/item", new
        {
            label = "BOSCH DE PRUEBA", icon = "", parentId = (int?)null,
            attributes = "{\"Brand\":\"DW\",\"Type\":\"Instrument\"}",
            children = Array.Empty<object>(), attachments = Array.Empty<object>(),
        });
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.OK), await created.Content.ReadAsStringAsync());
        var itemId = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();

        var rename = await Client.PutAsJsonAsync($"/api/constants/{BrandAttributeId()}", new
        {
            id = BrandAttributeId(), group = "Attribute", label = "Marca de fábrica", icon = "",
            description = "", sortOrder = 0, parentId = (int?)null, dataType = (string?)null,
            children = Array.Empty<object>(),
        });
        Assert.That(rename.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), await rename.Content.ReadAsStringAsync());

        // The stored attributes still carry the code as the key, and the item reads back untouched.
        var read = await Client.GetFromJsonAsync<JsonElement>("/api/item", Json);
        var panel = read.GetProperty("panels").EnumerateArray()
            .Single(item => item.GetProperty("id").GetInt32() == itemId);
        Assert.That(panel.GetProperty("attributes").GetString(), Is.EqualTo("{\"Brand\":\"DW\",\"Type\":\"Instrument\"}"));

        // The catalogue answers code and label: the identity survived the rename, the display text is the new one.
        var catalogue = await Client.GetFromJsonAsync<JsonElement>("/api/constants", Json);
        var brand = catalogue.GetProperty("panels").EnumerateArray()
            .Single(row => row.GetProperty("code").GetString() == "Brand" && row.GetProperty("group").GetString() == "Attribute");
        Assert.That(brand.GetProperty("label").GetString(), Is.EqualTo("Marca de fábrica"));
    }

    [Test]
    public async Task AnUnknownCodeIsRefusedAndTheItemDoesNotChange()
    {
        var before = await CountItems();

        var post = await Client.PostAsJsonAsync("/api/item", new
        {
            label = "CODIGO FANTASMA", icon = "", parentId = (int?)null,
            attributes = "{\"Type\":\"NoExiste\"}",
            children = Array.Empty<object>(), attachments = Array.Empty<object>(),
        });
        Assert.That(post.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await post.Content.ReadAsStringAsync(), Does.Contain("NoExiste"));
        Assert.That(await CountItems(), Is.EqualTo(before), "the refused item was not written");

        // A free value where the catalogue rules is the same refusal on update.
        var itemId = await CreateItem("ITEM CON ATRIBUTO");
        var read = await Client.GetFromJsonAsync<JsonElement>("/api/item", Json);
        var before_ = read.GetProperty("panels").EnumerateArray()
            .Single(item => item.GetProperty("id").GetInt32() == itemId)
            .GetProperty("attributes").GetString();
        var put = await Client.PutAsJsonAsync($"/api/item/{itemId}", new
        {
            id = itemId, label = "ITEM CON ATRIBUTO", icon = "", parentId = (int?)null,
            attributes = "{\"Type\":\"NoExiste\"}",
            children = Array.Empty<object>(), attachments = Array.Empty<object>(),
        });
        Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), await put.Content.ReadAsStringAsync());
        read = await Client.GetFromJsonAsync<JsonElement>("/api/item", Json);
        Assert.That(read.GetProperty("panels").EnumerateArray()
            .Single(item => item.GetProperty("id").GetInt32() == itemId)
            .GetProperty("attributes").GetString(), Is.EqualTo(before_), "the refused update wrote nothing");
    }

    [Test]
    public async Task AnUnknownAttributeKeyIsRefused()
    {
        var post = await Client.PostAsJsonAsync("/api/item", new
        {
            label = "CLAVE FANTASMA", icon = "", parentId = (int?)null,
            attributes = "{\"Marca\":\"x\"}",
            children = Array.Empty<object>(), attachments = Array.Empty<object>(),
        });
        Assert.That(post.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), await post.Content.ReadAsStringAsync());
        Assert.That(await post.Content.ReadAsStringAsync(), Does.Contain("Marca"));
    }

    [Test]
    public async Task TheLegacyImporterKeysStillCarryFreeValues()
    {
        var post = await Client.PostAsJsonAsync("/api/item", new
        {
            label = "LEGADO CON CLAVES", icon = "", parentId = (int?)null,
            attributes = "{\"Code\":\"L-1\",\"Reference\":\"9700\",\"Serial\":\"S/N 1\",\"Dimensions\":\"10x20\"}",
            children = Array.Empty<object>(), attachments = Array.Empty<object>(),
        });
        Assert.That(post.StatusCode, Is.EqualTo(HttpStatusCode.OK), await post.Content.ReadAsStringAsync());
        var itemId = (await post.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();
        var read = await Client.GetFromJsonAsync<JsonElement>("/api/item", Json);
        Assert.That(read.GetProperty("panels").EnumerateArray()
            .Single(item => item.GetProperty("id").GetInt32() == itemId)
            .GetProperty("attributes").GetString(),
            Is.EqualTo("{\"Code\":\"L-1\",\"Reference\":\"9700\",\"Serial\":\"S/N 1\",\"Dimensions\":\"10x20\"}"));
    }

    [Test]
    public async Task CreatingAConstantGeneratesAStableCode()
    {
        var post = await Client.PostAsJsonAsync("/api/constants", new
        {
            group = "Brand", label = "Fabricante de Prueba", icon = "", description = "",
            sortOrder = 0, parentId = BrandAttributeId(),
        });
        Assert.That(post.StatusCode, Is.EqualTo(HttpStatusCode.OK), await post.Content.ReadAsStringAsync());
        var created = await post.Content.ReadFromJsonAsync<JsonElement>(Json);
        var id = created.GetProperty("id").GetInt32();
        Assert.That(created.GetProperty("code").GetString(), Is.EqualTo("Fabricante de Prueba"));

        // A rename never moves the code.
        var rename = await Client.PutAsJsonAsync($"/api/constants/{id}", new
        {
            id, group = "Brand", label = "Marca Renombrada", icon = "", description = "",
            sortOrder = 0, parentId = BrandAttributeId(), dataType = (string?)null,
            children = Array.Empty<object>(),
        });
        Assert.That(rename.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), await rename.Content.ReadAsStringAsync());
        var catalogue = await Client.GetFromJsonAsync<JsonElement>("/api/constants", Json);
        var row = catalogue.GetProperty("panels").EnumerateArray()
            .Single(row => row.GetProperty("id").GetInt32() == id);
        Assert.That(row.GetProperty("code").GetString(), Is.EqualTo("Fabricante de Prueba"));
        Assert.That(row.GetProperty("label").GetString(), Is.EqualTo("Marca Renombrada"));
    }

    [Test]
    public async Task AnExplicitDuplicateCodeAnswersConflict()
    {
        var post = await Client.PostAsJsonAsync("/api/constants", new
        {
            group = "Brand", label = "Duplicado De Codigo", code = "DW", icon = "", description = "",
            sortOrder = 0, parentId = BrandAttributeId(),
        });
        Assert.That(post.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), await post.Content.ReadAsStringAsync());
        Assert.That(await post.Content.ReadAsStringAsync(), Does.Contain("DW"));
    }

    [Test]
    public void FreshMigrationLeavesEveryCodeEqualToTheFormerLabelAndSpanishLabels()
    {
        var root = Path.Combine(Path.GetTempPath(), "constant-codes-fresh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var options = new DbContextOptionsBuilder<ApplicationContext>()
                .UseSqlite($"Data Source={Path.Combine(root, "test.db")}").Options;
            using var db = new ApplicationContext(options, new TenantScope(new Microsoft.AspNetCore.Http.HttpContextAccessor()));
            db.Database.Migrate();
            var contentRoot = Factory.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath;
            Assert.That(DatabaseSeeder.SeedConfiguration(db, contentRoot), Is.True);

            foreach (var tenant in new[] { Persona, 900002, 900003 })
            {
                // Codes stay in their former form; the label is the Spanish display text.
                var marshall = db.Constants.IgnoreQueryFilters().Single(row => row.TenantId == tenant
                    && row.Group == "Attribute" && row.Code == "Brand");
                Assert.That(marshall.Label, Is.EqualTo("Marca"));
                Assert.That(db.Constants.IgnoreQueryFilters().Single(row => row.TenantId == tenant
                    && row.Group == "Type" && row.Code == "Part").Label, Is.EqualTo("Pieza"));
                Assert.That(db.Constants.IgnoreQueryFilters().Single(row => row.TenantId == tenant
                    && row.Group == "Category" && row.Code == "Drums").Label, Is.EqualTo("Baterías"));
                Assert.That(db.Constants.IgnoreQueryFilters().Single(row => row.TenantId == tenant
                    && row.Group == "Type" && row.Label == "Conjunto").Code, Is.EqualTo("Set"));
            }

            Assert.That(db.Database.SqlQueryRaw<int>("""
                SELECT COUNT(*) AS Value FROM (SELECT TenantId, "Group", "Code" FROM Constants
                WHERE "Code" != '' GROUP BY TenantId, "Group", "Code" HAVING COUNT(*) > 1)
                """).Single(), Is.Zero, "no two rows share an identity within a group");
            Assert.That(db.Constants.IgnoreQueryFilters().Count(row => row.Code == ""), Is.Zero,
                "the backfill left no row without its identity");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void AnUpgradedDatabaseEndsUpIdenticalToAFreshOne()
    {
        var root = Path.Combine(Path.GetTempPath(), "constant-codes-upgrade-" + Guid.NewGuid().ToString("N"));
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
            Assert.That(DatabaseSeeder.SeedConfiguration(db, contentRoot), Is.True,
                "the pre-code schema still seeds: seed.sql writes no Code column");
            // Counted, not materialized: the pre-migration schema has no Code column, so any query
            // that asks EF for the entity's columns would reach for one the table does not have.
            Assert.That(db.Constants.IgnoreQueryFilters().Count(row => row.Group == "Attribute" && row.Label == "Brand"),
                Is.EqualTo(3), "the pre-code database carries the former labels, one per organization");

            migrator.Migrate();

            foreach (var tenant in new[] { Persona, 900002, 900003 })
            {
                Assert.That(db.Constants.IgnoreQueryFilters().Single(row => row.TenantId == tenant
                    && row.Group == "Attribute" && row.Code == "Brand").Label, Is.EqualTo("Marca"));
                Assert.That(db.Constants.IgnoreQueryFilters().Single(row => row.TenantId == tenant
                    && row.Group == "Category" && row.Code == "Cymbals").Label, Is.EqualTo("Platillos"));
            }
            Assert.That(db.Database.SqlQueryRaw<int>("""
                SELECT COUNT(*) AS Value FROM (SELECT TenantId, "Group", "Code" FROM Constants
                WHERE "Code" != '' GROUP BY TenantId, "Group", "Code" HAVING COUNT(*) > 1)
                """).Single(), Is.Zero, "no two rows share an identity within a group");

            // The rewind leaves the labels the earlier migrations know: the Down un-translates.
            migrator.Migrate(previous);
            Assert.That(db.Constants.IgnoreQueryFilters().Count(row => row.Group == "Attribute" && row.Label == "Brand"),
                Is.EqualTo(3));
            Assert.That(db.Constants.IgnoreQueryFilters().Count(row => row.Group == "Type" && row.Label == "Set"), Is.Zero,
                "ItemSetKinds.Down still finds its rows by label after the rewind and removes them");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }
}
