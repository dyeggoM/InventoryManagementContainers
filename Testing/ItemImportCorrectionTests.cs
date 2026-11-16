using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IMCAPI.DB;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Testing;

[NonParallelizable]
[TestFixture]
public sealed class ItemImportCorrectionTests : ApiTestBase
{
    protected override bool FreshApiPerTest => true;

    private async Task<HttpResponseMessage> Send(string route, string? corrections = null)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(await File.ReadAllBytesAsync(FixturePath("import-sample.xlsx"))),
            "file", "import-sample.xlsx");
        if (corrections is not null) form.Add(new StringContent(corrections), "corrections");
        return await Client.PostAsync(route, form);
    }

    private async Task<JsonElement> PreviewWith(string? corrections = null)
    {
        using var response = await Send("/api/import/preview", corrections);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    [Test]
    public async Task RawAndHeaders_AreSourceSpecificAndGreenHasNoRaw()
    {
        var preview = await PreviewWith();
        var red = Items(preview).Single(item => item.GetProperty("reasons").ToString().Contains("Sin descripción"));
        var headers = preview.GetProperty("sourceHeaders").GetProperty(red.GetProperty("source").GetString()!);
        Assert.That(headers.EnumerateArray().Select(header => header.GetString()), Does.Contain("DESCRIPCION"));
        Assert.That(string.IsNullOrWhiteSpace(red.GetProperty("raw").GetProperty("DESCRIPCION").GetString()), Is.True);
        Assert.That(Items(preview).Single(item => item.GetProperty("status").GetString() == "green")
            .TryGetProperty("raw", out var greenRaw) && greenRaw.ValueKind != JsonValueKind.Null, Is.False);
    }

    [Test]
    public async Task CorrectedDescription_RejudgesAndImportsIntoCorrectGroup()
    {
        var before = await PreviewWith();
        var red = Items(before).Single(item => item.GetProperty("reasons").ToString().Contains("Sin descripción"));
        var correction = JsonSerializer.Serialize(new[] { new {
            row = red.GetProperty("row").GetInt32(), source = red.GetProperty("source").GetString(),
            label = "ATRIL CORREGIDO", subcategory = "GTR-COMBO", code = "FREE-CORRECTED", brand = "DW"
        } });
        var after = await PreviewWith(correction);
        var item = Items(after).Single(item => Label(item) == "ATRIL CORREGIDO");
        Assert.Multiple(() => {
            Assert.That(item.GetProperty("status").GetString(), Is.EqualTo("green"));
            Assert.That(after.GetProperty("summary").GetProperty("rows").GetInt32(),
                Is.EqualTo(before.GetProperty("summary").GetProperty("rows").GetInt32()));
            Assert.That(after.GetProperty("sheets").EnumerateArray().SelectMany(sheet => sheet.GetProperty("groups").EnumerateArray())
                .Any(group => group.GetProperty("name").GetString() == "GTR-COMBO" &&
                    group.GetProperty("items").EnumerateArray().Any(row => Label(row) == "ATRIL CORREGIDO")), Is.True);
        });
        using var response = await Send("/api/import", correction);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var saved = await context.Items.SingleAsync(row => row.Label == "ATRIL CORREGIDO");
        Assert.That(JsonDocument.Parse(saved.Attributes!).RootElement.GetProperty("Code").GetString(),
            Is.EqualTo("FREE-CORRECTED"));
        var audit = await context.AuditLogs.SingleAsync(row => row.Action == "ItemImport");
        Assert.That(audit.AfterJson, Does.Contain("\"corrected\":1"));
    }

    [Test]
    public async Task UnknownAndMalformedCorrections_LeaveVerdictsUntouched()
    {
        var baseline = await PreviewWith();
        foreach (var correction in new[] { "[{\"row\":999999,\"label\":\"X\"}]", "not-json" })
        {
            var response = await PreviewWith(correction);
            Assert.That(response.GetProperty("summary").GetProperty("red").GetInt32(),
                Is.EqualTo(baseline.GetProperty("summary").GetProperty("red").GetInt32()));
            Assert.That(response.GetProperty("issues").ToString(), Does.Contain(correction == "not-json" ? "JSON inválido" : "no existe"));
        }
        using var imported = await Send("/api/import", "not-json");
        Assert.That(imported.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await imported.Content.ReadAsStringAsync(), Does.Contain("JSON inválido"));
    }

    [Test]
    public async Task DuplicateCode_OnlyFreeCorrectionClearsRed()
    {
        var baseline = await PreviewWith();
        var twin = Items(baseline).First(item => item.TryGetProperty("code", out var code) && code.GetString() == "-T0004");
        var row = twin.GetProperty("row").GetInt32();
        var source = twin.GetProperty("source").GetString();
        foreach (var (code, expected) in new[] { ("-T0001", "red"), ("UNIQUE-CODE-42", "green") })
        {
            var correction = JsonSerializer.Serialize(new[] { new { row, source, code } });
            var changed = await PreviewWith(correction);
            Assert.That(Items(changed).Single(item => item.GetProperty("row").GetInt32() == row &&
                item.GetProperty("source").GetString() == source).GetProperty("status").GetString(), Is.EqualTo(expected));
            if (code == "-T0001")
                Assert.That(Items(changed).Single(item => item.TryGetProperty("code", out var current) && current.GetString() == "-T0001")
                    .GetProperty("status").GetString(), Is.EqualTo(Items(baseline)
                        .Single(item => item.TryGetProperty("code", out var original) && original.GetString() == "-T0001")
                        .GetProperty("status").GetString()));
        }
        var repaired = JsonSerializer.Serialize(new[] { new { row, source, code = "UNIQUE-CODE-42" } });
        using var response = await Send("/api/import", repaired);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var record = await context.Items.SingleAsync(item => item.Attributes!.Contains("UNIQUE-CODE-42"));
        Assert.That(JsonDocument.Parse(record.Attributes!).RootElement.GetProperty("Code").GetString(),
            Is.EqualTo("UNIQUE-CODE-42"));
    }

    [Test]
    public async Task Limits_RejectExcessEntriesAndOversizedFieldsOnBothRoutes()
    {
        foreach (var route in new[] { "/api/import/preview", "/api/import" })
        foreach (var (payload, fragment) in new[] {
            (JsonSerializer.Serialize(Enumerable.Range(1, 1001).Select(row => new { row })), "1000"),
            (JsonSerializer.Serialize(new[] { new { row = 3, label = new string('x', 513) } }), "512")
        })
        {
            using var response = await Send(route, payload);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain(fragment));
        }
    }
}
