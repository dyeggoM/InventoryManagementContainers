using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IMCAPI.DB;
using IMCAPI.DB.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Testing;

/// <summary>
/// The five holes the audit found in the code/label round, each one a way a value the item
/// validator refuses can still be written (or a conflict answered as a database error): the
/// importer storing free brands, two simultaneous constant creates, a duplicated label, a PUT
/// without the attribute bag, and an archive restored with attributes the catalogue does not know.
/// </summary>
[NonParallelizable]
[TestFixture]
public sealed class ConstantCatalogAuditTests : ApiTestBase
{
    protected override bool FreshApiPerTest => true;

    private int BrandAttributeId()
    {
        using var scope = PersonaScope();
        return scope.ServiceProvider.GetRequiredService<ApplicationContext>().Constants.IgnoreQueryFilters()
            .Single(row => row.TenantId == OrganizationConfiguration.DefaultTenantId
                && row.Group == "Attribute" && row.Code == "Brand").Id;
    }

    [Test]
    public async Task Import_MarksAnUnknownBrandRed_AndWritesNothing()
    {
        var preview = await Preview("import-badbrand.csv", Csv);

        var invented = Items(preview).Single(item => Label(item) == "PIEZA MARCA INVENTADA");
        var valid = Items(preview).Single(item => Label(item) == "PIEZA MARCA VALIDA");
        Assert.Multiple(() =>
        {
            Assert.That(invented.GetProperty("status").GetString(), Is.EqualTo("red"),
                "a brand the catalogue does not know is a blocking verdict, not a review flag");
            Assert.That(invented.GetProperty("reasons").ToString(), Does.Contain("MARCA-QUE-NO-EXISTE"));
            Assert.That(valid.GetProperty("status").GetString(), Is.EqualTo("green"),
                "the same file's known brand stays green");
        });

        var result = await Import("import-badbrand.csv", Csv, includeYellow: false);
        var contextFactory = Factory.Services;
        using (var scope = contextFactory.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
            var createdItems = result.GetProperty("createdItems").GetInt32();
            var skippedRed = result.GetProperty("skippedRed").GetInt32();
            var inventedLanded = await context.Items.IgnoreQueryFilters()
                .AnyAsync(row => row.Label == "PIEZA MARCA INVENTADA");
            var validLanded = await context.Items.IgnoreQueryFilters()
                .AnyAsync(row => row.Label == "PIEZA MARCA VALIDA");
            Assert.Multiple(() =>
            {
                Assert.That(createdItems, Is.EqualTo(1),
                    "only the row whose brand the catalogue knows is written");
                Assert.That(skippedRed, Is.EqualTo(1), "the unknown-brand row is counted as rejected");
                Assert.That(inventedLanded, Is.False, "the row with the invented brand never lands");
                Assert.That(validLanded, Is.True);
            });
        }
    }

    [Test]
    public async Task ACorrectionNamingAnUnknownBrand_IsRedAndNeverWritten()
    {
        var baseline = await Preview("import-sample.xlsx");
        // The sample's red row ("Sin descripción") is the one a correction can repair; repairing it
        // with a brand the catalogue does not know must not smuggle it in either.
        var red = Items(baseline).Single(item =>
            item.GetProperty("reasons").ToString().Contains("Sin descripción"));
        var corrections = JsonSerializer.Serialize(new[]
        {
            new { row = red.GetProperty("row").GetInt32(), source = red.GetProperty("source").GetString(),
                label = "PIEZA MARCA INVENTADA", subcategory = "UPS", code = "MB-100",
                brand = "MARCA-QUE-NO-EXISTE" }
        });

        using var payload = new MultipartFormDataContent();
        payload.Add(new ByteArrayContent(await File.ReadAllBytesAsync(FixturePath("import-sample.xlsx"))),
            "file", "import-sample.xlsx");
        payload.Add(new StringContent(OrganizationConfiguration.DefaultTenantId.ToString()), "organizationId");
        payload.Add(new StringContent(corrections), "corrections");
        var preview = await (await Client.PostAsync("/api/import/preview", payload))
            .Content.ReadFromJsonAsync<JsonElement>(Json);

        var corrected = Items(preview).Single(item => Label(item) == "PIEZA MARCA INVENTADA");
        Assert.Multiple(() =>
        {
            Assert.That(corrected.GetProperty("status").GetString(), Is.EqualTo("red"),
                "the correction goes through the same judgement as any row");
            Assert.That(corrected.GetProperty("reasons").ToString(), Does.Contain("MARCA-QUE-NO-EXISTE"));
        });

        var before = await CountItems();
        using var write = await Client.PostAsync("/api/import", payload);
        var import = await write.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.That(write.StatusCode, Is.EqualTo(HttpStatusCode.OK), await write.Content.ReadAsStringAsync());
        using (var scope = Factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
            var createdItems = import.GetProperty("createdItems").GetInt32();
            var inventedLanded = await context.Items.IgnoreQueryFilters()
                .AnyAsync(row => row.Label == "PIEZA MARCA INVENTADA");
            var countAfter = await CountItems();
            Assert.Multiple(() =>
            {
                Assert.That(createdItems, Is.EqualTo(5),
                    "the file's six importable rows minus the one the invented brand turned red");
                Assert.That(inventedLanded, Is.False, "the corrected row did not land");
                Assert.That(countAfter, Is.GreaterThan(before), "the rest of the file landed as always");
            });
        }
    }

    [Test]
    public async Task ABrandCreatedThroughTheApi_IsKnownByPreviewAndValidator()
    {
        var created = await Client.PostAsJsonAsync("/api/constants", new
        {
            group = "Brand", label = "RACE0", icon = "", description = "",
            sortOrder = 0, parentId = BrandAttributeId(),
        });
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.OK), await created.Content.ReadAsStringAsync());

        var preview = await Preview("import-newbrand.csv", Csv);
        var row = Items(preview).Single(item => Label(item) == "PIEZA RACE0");
        Assert.Multiple(() =>
        {
            Assert.That(row.GetProperty("status").GetString(), Is.EqualTo("green"),
                "the brand the API created hangs from the Brand attribute, and the preview knows it");
            Assert.That(row.GetProperty("reasons").ToString(), Does.Not.Contain("mal puesta"));
            Assert.That(row.GetProperty("reasons").ToString(), Does.Not.Contain("no está en el catálogo"));
        });

        var result = await Import("import-newbrand.csv", Csv);
        Assert.That(result.GetProperty("createdItems").GetInt32(), Is.EqualTo(1));

        using var scope = PersonaScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var item = await context.Items.IgnoreQueryFilters().SingleAsync(item => item.Label == "PIEZA RACE0");
        // The stored brand is the code, and the item it created remains saveable.
        Assert.That(JsonDocument.Parse(item.Attributes!).RootElement.GetProperty("Brand").GetString(),
            Is.EqualTo("RACE0"));
        var put = await Client.PutAsJsonAsync($"/api/item/{item.Id}", new
        {
            id = item.Id, label = "PIEZA RACE0", icon = "", parentId = (int?)null,
            attributes = item.Attributes, children = Array.Empty<object>(), attachments = Array.Empty<object>(),
        });
        Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.NoContent),
            "an imported item whose brand the API created is not an orphan the API refuses to save");
    }

    [Test]
    public async Task TwoSimultaneousConstantCreates_NameTheSameCodeOnce()
    {
        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            using var first = Factory.CreateClient();
            using var second = Factory.CreateClient();
            await Authenticate(first, SuperadminEmail);
            await Authenticate(second, SuperadminEmail);
            var body = new
            {
                group = "Brand", label = $"Carrera {attempt}", code = $"RACE-{attempt}", icon = "",
                description = "", sortOrder = 0, parentId = BrandAttributeId(),
            };
            var race = await Task.WhenAll(
                first.PostAsJsonAsync("/api/constants", body),
                second.PostAsJsonAsync("/api/constants", body));
            statuses.AddRange(race.Select(response => response.StatusCode));
        }

        Assert.That(statuses.Where(status => status == HttpStatusCode.InternalServerError), Is.Empty,
            "the loser of the race is a conflict, never a database error");
        Assert.That(statuses.Count(status => status == HttpStatusCode.Conflict), Is.EqualTo(6),
            "each race answered one win and one Duplicate code");
        Assert.That(statuses.Count(status => status == HttpStatusCode.OK), Is.EqualTo(6));
    }

    [Test]
    public async Task TwoCodesMayNotShareALabel()
    {
        var first = await Client.PostAsJsonAsync("/api/constants", new
        {
            group = "Brand", label = "Etiqueta Doble", code = "ETIQUETA-UNO", icon = "", description = "",
            sortOrder = 0, parentId = BrandAttributeId(),
        });
        Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK), await first.Content.ReadAsStringAsync());

        var second = await Client.PostAsJsonAsync("/api/constants", new
        {
            group = "Brand", label = "Etiqueta Doble", code = "ETIQUETA-DOS", icon = "", description = "",
            sortOrder = 0, parentId = BrandAttributeId(),
        });
        Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), await second.Content.ReadAsStringAsync());
        Assert.That(await second.Content.ReadAsStringAsync(), Does.Contain("Duplicate label"));

        // The same refusal when a rename would take the label another row already carries.
        var rename = await Client.PutAsJsonAsync("/api/constants/4000", new
        {
            id = 4000, group = "Brand", label = "Etiqueta Doble", icon = "", description = "",
            sortOrder = 0, parentId = BrandAttributeId(), dataType = (string?)null, children = Array.Empty<object>(),
        });
        Assert.That(rename.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), await rename.Content.ReadAsStringAsync());
        Assert.That(await rename.Content.ReadAsStringAsync(), Does.Contain("Duplicate label"));
    }

    [Test]
    public async Task PutWithoutTheAttributeBag_KeepsTheOnesTheItemHad()
    {
        var created = await Client.PostAsJsonAsync("/api/item", new
        {
            label = "PUT SIN BOLSA", icon = "", parentId = (int?)null,
            attributes = "{\"Brand\":\"DW\",\"Type\":\"Instrument\"}",
            children = Array.Empty<object>(), attachments = Array.Empty<object>(),
        });
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.OK), await created.Content.ReadAsStringAsync());
        var itemId = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();

        var put = await Client.PutAsJsonAsync($"/api/item/{itemId}", new
        {
            id = itemId, label = "PUT SIN BOLSA RENOMBRADO", icon = "", parentId = (int?)null,
            children = Array.Empty<object>(), attachments = Array.Empty<object>(),
        });
        Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), await put.Content.ReadAsStringAsync());

        var read = await Client.GetFromJsonAsync<JsonElement>("/api/item", Json);
        Assert.That(read.GetProperty("panels").EnumerateArray()
            .Single(item => item.GetProperty("id").GetInt32() == itemId)
            .GetProperty("attributes").GetString(),
            Is.EqualTo("{\"Brand\":\"DW\",\"Type\":\"Instrument\"}"), "the bag survived the label-only update");
    }

    [Test]
    public async Task AValueSpelledAsALabel_IsStoredAsItsCode()
    {
        // A constant whose display name and code differ on purpose.
        var created = await Client.PostAsJsonAsync("/api/constants", new
        {
            group = "Brand", label = "Zeta Pro", code = "ZZ9", icon = "", description = "",
            sortOrder = 0, parentId = BrandAttributeId(),
        });
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.OK), await created.Content.ReadAsStringAsync());

        var post = await Client.PostAsJsonAsync("/api/item", new
        {
            label = "ETIQUETA GUARDADA", icon = "", parentId = (int?)null,
            attributes = "{\"Brand\":\"Zeta Pro\"}",
            children = Array.Empty<object>(), attachments = Array.Empty<object>(),
        });
        Assert.That(post.StatusCode, Is.EqualTo(HttpStatusCode.OK), await post.Content.ReadAsStringAsync());
        var itemId = (await post.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();
        var read = await Client.GetFromJsonAsync<JsonElement>("/api/item", Json);
        Assert.That(read.GetProperty("panels").EnumerateArray()
            .Single(item => item.GetProperty("id").GetInt32() == itemId)
            .GetProperty("attributes").GetString(),
            Is.EqualTo("{\"Brand\":\"ZZ9\"}"), "the label arrived, the code is what got stored");

        // The same resolution the importer applies, down to the case: the seed spells AMPEG and a
        // file writing 'ampeg' lands as AMPEG, so the API answers the same to the same value.
        var put = await Client.PutAsJsonAsync($"/api/item/{itemId}", new
        {
            id = itemId, label = "ETIQUETA GUARDADA", icon = "", parentId = (int?)null,
            attributes = "{\"Brand\":\"ampeg\"}",
            children = Array.Empty<object>(), attachments = Array.Empty<object>(),
        });
        Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), await put.Content.ReadAsStringAsync());
        read = await Client.GetFromJsonAsync<JsonElement>("/api/item", Json);
        Assert.That(read.GetProperty("panels").EnumerateArray()
            .Single(item => item.GetProperty("id").GetInt32() == itemId)
            .GetProperty("attributes").GetString(),
            Is.EqualTo("{\"Brand\":\"AMPEG\"}"));
    }

    [Test]
    public async Task DeletingAConstantInUse_IsRefusedUntilTheItemsLetItGo()
    {
        using var scope = PersonaScope();
        var dwId = await scope.ServiceProvider.GetRequiredService<ApplicationContext>()
            .Constants.IgnoreQueryFilters()
            .Where(row => row.TenantId == OrganizationConfiguration.DefaultTenantId
                && row.Group == "Brand" && row.Code == "DW")
            .Select(row => row.Id).SingleAsync();

        var first = await Client.PostAsJsonAsync("/api/item", new
        {
            label = "USO VIVO DE DW", icon = "", parentId = (int?)null,
            attributes = "{\"Brand\":\"DW\"}",
            children = Array.Empty<object>(), attachments = Array.Empty<object>(),
        });
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();
        var second = await Client.PostAsJsonAsync("/api/item", new
        {
            label = "USO ARCHIVADO DE DW", icon = "", parentId = (int?)null,
            attributes = "{\"Brand\":\"DW\"}",
            children = Array.Empty<object>(), attachments = Array.Empty<object>(),
        });
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();
        Assert.That((await Client.DeleteAsync($"/api/item/{secondId}")).StatusCode,
            Is.EqualTo(HttpStatusCode.NoContent), "one user of the brand is now in the trash");

        var delete = await Client.DeleteAsync($"/api/constants/{dwId}");
        var refusal = await delete.Content.ReadAsStringAsync();
        Assert.Multiple(() =>
        {
            Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), refusal);
            Assert.That(refusal, Does.Contain("Code in use"));
            Assert.That(refusal, Does.Contain("DW"));
            Assert.That(refusal, Does.Contain("1 item(s)"), "the live user of the brand");
            Assert.That(refusal, Does.Contain("1 archived item(s)"), "the pending restore keeps the code too");
            Assert.That(refusal, Does.Contain("'Brand'"), "and it says which attribute");
        });

        // The refusal kept everything readable: the archive restores, and the live item saves.
        var restored = await Client.PatchAsync($"/api/item/{secondId}/activate", null);
        Assert.That(restored.StatusCode, Is.EqualTo(HttpStatusCode.OK), await restored.Content.ReadAsStringAsync());
        var restoredId = (await restored.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();
        var stillThere = await Client.DeleteAsync($"/api/constants/{dwId}");
        Assert.That(stillThere.StatusCode, Is.EqualTo(HttpStatusCode.Conflict),
            "now two live items carry the code");

        // Free both usages and the code is deletable again — and free to be recreated.
        foreach (var (id, label) in new[] { (firstId, "USO VIVO DE DW"), (restoredId, "USO ARCHIVADO DE DW") })
        {
            var edit = await Client.PutAsJsonAsync($"/api/item/{id}", new
            {
                id, label, icon = "", parentId = (int?)null,
                attributes = "{\"Brand\":\"GENERICO\"}",
                children = Array.Empty<object>(), attachments = Array.Empty<object>(),
            });
            Assert.That(edit.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), await edit.Content.ReadAsStringAsync());
        }
        var freed = await Client.DeleteAsync($"/api/constants/{dwId}");
        Assert.That(freed.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), await freed.Content.ReadAsStringAsync());
        // The first delete is logical and the inactive row still owns the unique index: the second
        // reaches behind the query filter and removes it, exactly like the delete-probe flow.
        Assert.That((await Client.DeleteAsync($"/api/constants/{dwId}")).StatusCode,
            Is.EqualTo(HttpStatusCode.NoContent));
        var recreated = await Client.PostAsJsonAsync("/api/constants", new
        {
            group = "Brand", label = "DW Otra Vez", code = "DW", icon = "", description = "",
            sortOrder = 0, parentId = BrandAttributeId(),
        });
        Assert.That(recreated.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            await recreated.Content.ReadAsStringAsync() + " — the deleted row no longer owns the code");
    }

    [Test]
    public async Task DeletingAnAttributeWhoseKeyIsInUse_IsRefused()
    {
        var itemId = (await (await Client.PostAsJsonAsync("/api/item", new
        {
            label = "CLAVE EN USO", icon = "", parentId = (int?)null,
            attributes = "{\"Brand\":\"DW\"}",
            children = Array.Empty<object>(), attachments = Array.Empty<object>(),
        })).Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();

        var delete = await Client.DeleteAsync($"/api/constants/{BrandAttributeId()}");
        var refusal = await delete.Content.ReadAsStringAsync();
        Assert.Multiple(() =>
        {
            Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), refusal);
            Assert.That(refusal, Does.Contain("Code in use"));
            Assert.That(refusal, Does.Contain("'Brand'"), "the attribute key is the usage");
            Assert.That(refusal, Does.Contain("1 item(s)"));
        });

        var read = await Client.GetFromJsonAsync<JsonElement>("/api/item", Json);
        Assert.That(read.GetProperty("panels").EnumerateArray()
            .Single(item => item.GetProperty("id").GetInt32() == itemId)
            .GetProperty("attributes").GetString(),
            Is.EqualTo("{\"Brand\":\"DW\"}"), "the item that pins the attribute is untouched");
    }

    [Test]
    public async Task Activate_RefusesAnArchiveWithUnknownAttributes_AndKeepsItArchived()
    {
        var created = await Client.PostAsJsonAsync("/api/item", new
        {
            label = "ARCHIVO CON BASURA", icon = "", parentId = (int?)null,
            attributes = "{\"Brand\":\"DW\"}",
            children = Array.Empty<object>(), attachments = Array.Empty<object>(),
        });
        var itemId = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();
        var deleted = await Client.DeleteAsync($"/api/item/{itemId}");
        Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), await deleted.Content.ReadAsStringAsync());

        // Simulate an archive written before the contract knew what a valid attribute is.
        using (var scope = PersonaScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
            await context.ItemArchives.IgnoreQueryFilters()
                .Where(row => row.ItemId == itemId && row.RestoredAtUtc == null)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Attributes, "{\"Brand\":\"BASURA\"}"));
        }

        var before = await CountItems();
        var activate = await Client.PatchAsync($"/api/item/{itemId}/activate", null);
        var refusalBody = await activate.Content.ReadAsStringAsync();
        var refusedCount = await CountItems();
        Assert.Multiple(() =>
        {
            Assert.That(activate.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), refusalBody);
            Assert.That(refusalBody, Does.Contain("BASURA"));
            Assert.That(refusedCount, Is.EqualTo(before), "the refused restore wrote nothing");
        });

        // The archive is untouched: fixing the data and trying again works.
        using (var scope = PersonaScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
            await context.ItemArchives.IgnoreQueryFilters()
                .Where(row => row.ItemId == itemId && row.RestoredAtUtc == null)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Attributes, "{\"Brand\":\"DW\"}"));
        }
        var retry = await Client.PatchAsync($"/api/item/{itemId}/activate", null);
        Assert.That(retry.StatusCode, Is.EqualTo(HttpStatusCode.OK), await retry.Content.ReadAsStringAsync());
        Assert.That(await CountItems(), Is.EqualTo(before + 1), "the item came back once the archive was valid");
    }
}
