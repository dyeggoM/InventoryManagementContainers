using System.Net;
using System.Net.Http.Json;
using System.IO.Compression;
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

    private async Task<HttpResponseMessage> Send(string route, string? corrections = null, byte[]? workbook = null)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(workbook ?? await File.ReadAllBytesAsync(FixturePath("import-sample.xlsx"))),
            "file", "import-sample.xlsx");
        if (corrections is not null) form.Add(new StringContent(corrections), "corrections");
        return await Client.PostAsync(route, form);
    }

    private async Task<JsonElement> PreviewWith(string? corrections = null, byte[]? workbook = null)
    {
        using var response = await Send("/api/import/preview", corrections, workbook);
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
            Assert.That(after.GetProperty("correctionIssues").GetArrayLength(), Is.Zero);
            Assert.That(after.GetProperty("summary").GetProperty("rows").GetInt32(),
                Is.EqualTo(before.GetProperty("summary").GetProperty("rows").GetInt32()));
            Assert.That(after.GetProperty("sheets").EnumerateArray().SelectMany(sheet => sheet.GetProperty("groups").EnumerateArray())
                .Any(group => group.GetProperty("name").GetString() == "GTR-COMBO" &&
                    group.GetProperty("items").EnumerateArray().Any(row => Label(row) == "ATRIL CORREGIDO")), Is.True);
        });
        using var response = await Send("/api/import", correction);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.That(result.GetProperty("correctionIssues").GetArrayLength(), Is.Zero);
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
            Assert.That(response.GetProperty("correctionIssues").ToString(),
                Does.Contain(correction == "not-json" ? "JSON inválido" : "Fila 999999: no existe"));
            Assert.That(response.GetProperty("issues").ToString(),
                Does.Not.Contain(correction == "not-json" ? "JSON inválido" : "no existe"));
        }
        using var imported = await Send("/api/import", "not-json");
        Assert.That(imported.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await imported.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.That(result.GetProperty("correctionIssues").ToString(), Does.Contain("JSON inválido"));
        Assert.That(result.GetProperty("createdItems").GetInt32(), Is.GreaterThan(0));
    }

    [Test]
    public async Task AmbiguousCorrection_ReportsRowWithoutChangingEitherSheet_AndImportsTheRest()
    {
        var workbook = TwoSheetWorkbook();
        var baseline = await PreviewWith(workbook: workbook);
        var twins = Items(baseline).GroupBy(item => item.GetProperty("row").GetInt32())
            .First(group => group.Select(item => item.GetProperty("source").GetString()).Distinct().Count() > 1)
            .ToList();
        var row = twins[0].GetProperty("row").GetInt32();
        var correction = JsonSerializer.Serialize(new[] { new { row, label = "SHOULD NOT APPLY" } });
        var preview = await PreviewWith(correction, workbook);
        var unchanged = Items(preview).Where(item => item.GetProperty("row").GetInt32() == row).ToList();
        Assert.Multiple(() => {
            Assert.That(preview.GetProperty("correctionIssues").ToString(), Does.Contain($"Fila {row}: es ambigua"));
            Assert.That(preview.GetProperty("issues").ToString(), Does.Not.Contain("es ambigua"));
            Assert.That(unchanged.Select(item => (item.GetProperty("source").GetString(), Label(item),
                item.GetProperty("status").GetString())), Is.EquivalentTo(twins.Select(item =>
                (item.GetProperty("source").GetString(), Label(item), item.GetProperty("status").GetString()))));
        });

        using var imported = await Send("/api/import", correction, workbook);
        Assert.That(imported.StatusCode, Is.EqualTo(HttpStatusCode.OK), await imported.Content.ReadAsStringAsync());
        var result = await imported.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.That(result.GetProperty("correctionIssues").ToString(), Does.Contain($"Fila {row}: es ambigua"));
        Assert.That(result.GetProperty("createdItems").GetInt32(), Is.GreaterThan(0));
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        Assert.That(await context.Items.AnyAsync(item => item.Label == "SHOULD NOT APPLY"), Is.False);
        Assert.That(await context.Items.CountAsync(), Is.GreaterThan(0));
    }

    [Test]
    public async Task SourceQualifiedCorrection_OnlyChangesItsSheet()
    {
        var workbook = TwoSheetWorkbook();
        var baseline = await PreviewWith(workbook: workbook);
        var correction = "[{\"row\":2,\"source\":\"BACKLINE\",\"label\":\"EDITED BACKLINE\"}]";
        var preview = await PreviewWith(correction, workbook);
        var rows = Items(preview).Where(item => item.GetProperty("row").GetInt32() == 2).ToList();
        Assert.Multiple(() => {
            Assert.That(preview.GetProperty("correctionIssues").GetArrayLength(), Is.Zero);
            Assert.That(Label(rows.Single(item => item.GetProperty("source").GetString() == "BACKLINE")),
                Is.EqualTo("EDITED BACKLINE"));
            Assert.That(Label(rows.Single(item => item.GetProperty("source").GetString() == "SONIDO")),
                Is.EqualTo(Label(Items(baseline).Single(item => item.GetProperty("source").GetString() == "SONIDO"))));
        });
    }

    private static byte[] TwoSheetWorkbook()
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            static void Add(ZipArchive zip, string path, string xml)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open());
                writer.Write(xml);
            }
            Add(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                  <Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                </Types>
                """);
            Add(zip, "_rels/.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            Add(zip, "xl/workbook.xml", """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets><sheet name="BACKLINE" sheetId="1" r:id="rId1"/><sheet name="SONIDO" sheetId="2" r:id="rId2"/></sheets>
                </workbook>
                """);
            Add(zip, "xl/_rels/workbook.xml.rels", """
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/>
                </Relationships>
                """);
            for (var sheet = 1; sheet <= 2; sheet++)
                Add(zip, $"xl/worksheets/sheet{sheet}.xml", $$"""
                    <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                      <sheetData>
                        <row r="1"><c r="A1" t="inlineStr"><is><t>DESCRIPCION</t></is></c></row>
                        <row r="2"><c r="A2" t="inlineStr"><is><t>ITEM {{sheet}}</t></is></c></row>
                      </sheetData>
                    </worksheet>
                    """);
        }
        return stream.ToArray();
    }

    [Test]
    public async Task InvalidField_ReportsCorrectionSeparately()
    {
        var preview = await PreviewWith("[{\"row\":3,\"notAField\":\"X\"}]");
        Assert.That(preview.GetProperty("correctionIssues").ToString(), Does.Contain("Fila 3: corrección inválida"));
        Assert.That(preview.GetProperty("issues").ToString(), Does.Not.Contain("corrección inválida"));
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

    [Test]
    public async Task ExtraAttribute_MergesWithFileAttributesOnImport()
    {
        var before = await PreviewWith();
        var original = Items(before).First(item => item.GetProperty("status").GetString() == "green" &&
            item.GetProperty("brand").GetString() is { Length: > 0 });
        var label = Label(original);
        var brand = original.GetProperty("brand").GetString();
        var correction = JsonSerializer.Serialize(new[] { new {
            row = original.GetProperty("row").GetInt32(), source = original.GetProperty("source").GetString(),
            attributes = new Dictionary<string, string?> { ["Estado"] = "Bueno" }
        } });
        using var response = await Send("/api/import", correction);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var saved = await context.Items.SingleAsync(item => item.Label == label);
        using var attributes = JsonDocument.Parse(saved.Attributes!);
        Assert.Multiple(() => {
            Assert.That(attributes.RootElement.GetProperty("Estado").GetString(), Is.EqualTo("Bueno"));
            Assert.That(attributes.RootElement.GetProperty("Brand").GetString(), Is.EqualTo(brand));
            Assert.That(attributes.RootElement.GetProperty("Description").GetString(), Is.EqualTo(label));
        });
    }

    [Test]
    public async Task AttributeDescription_RepairsMissingDescription()
    {
        var baseline = await PreviewWith();
        var red = Items(baseline).Single(item => item.GetProperty("reasons").ToString().Contains("Sin descripción"));
        var correction = JsonSerializer.Serialize(new[] { new {
            row = red.GetProperty("row").GetInt32(), source = red.GetProperty("source").GetString(),
            attributes = new Dictionary<string, string?> { ["Description"] = "BASE BOOM DW 5000", ["Brand"] = "DW" }
        } });
        var preview = await PreviewWith(correction);
        var repaired = Items(preview).Single(item => Label(item) == "BASE BOOM DW 5000");
        Assert.Multiple(() => {
            Assert.That(repaired.GetProperty("status").GetString(), Is.EqualTo("green"));
            Assert.That(repaired.GetProperty("attributes").GetProperty("Description").GetString(),
                Is.EqualTo("BASE BOOM DW 5000"));
            Assert.That(repaired.GetProperty("attributes").GetProperty("Brand").GetString(), Is.EqualTo("DW"));
        });
    }

    [TestCase(null, true)]
    [TestCase("", false)]
    public async Task NullKeepsAndEmptyRemovesFileAttribute(string? replacement, bool retained)
    {
        var baseline = await PreviewWith();
        var original = Items(baseline).First(item => item.GetProperty("status").GetString() == "green" &&
            item.GetProperty("brand").GetString() is { Length: > 0 });
        var correction = JsonSerializer.Serialize(new[] { new {
            row = original.GetProperty("row").GetInt32(), source = original.GetProperty("source").GetString(),
            attributes = new Dictionary<string, string?> { ["Brand"] = replacement }
        } });
        using var response = await Send("/api/import", correction);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var saved = await context.Items.SingleAsync(item => item.Label == Label(original));
        using var attributes = JsonDocument.Parse(saved.Attributes!);
        Assert.That(attributes.RootElement.TryGetProperty("Brand", out var brand), Is.EqualTo(retained));
        if (retained) Assert.That(brand.GetString(), Is.EqualTo(original.GetProperty("brand").GetString()));
    }

    [Test]
    public async Task ConflictingAttributeCode_ReportsIssueAndStoresExplicitCode()
    {
        var baseline = await PreviewWith();
        var original = Items(baseline).First(item => item.GetProperty("status").GetString() == "green");
        var correction = JsonSerializer.Serialize(new[] { new {
            row = original.GetProperty("row").GetInt32(), source = original.GetProperty("source").GetString(),
            code = "VALIDATED-42", attributes = new Dictionary<string, string?> { ["Code"] = "DIFFERENT-42" }
        } });
        var preview = await PreviewWith(correction);
        Assert.That(preview.GetProperty("correctionIssues").ToString(), Does.Contain("attributes.Code"));
        Assert.That(Items(preview).Single(item => Label(item) == Label(original)).GetProperty("code").GetString(),
            Is.EqualTo("VALIDATED-42"));
        using var response = await Send("/api/import", correction);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var saved = await context.Items.SingleAsync(item => item.Label == Label(original));
        using var attributes = JsonDocument.Parse(saved.Attributes!);
        Assert.That(attributes.RootElement.GetProperty("Code").GetString(), Is.EqualTo("VALIDATED-42"));
    }

    [Test]
    public async Task AttributeBounds_RejectOnBothRoutes()
    {
        foreach (var route in new[] { "/api/import/preview", "/api/import" })
        foreach (var (attributes, field) in new[] {
            (Enumerable.Range(0, 41).ToDictionary(i => $"Key{i}", _ => (string?)"x"), "attributes"),
            (new Dictionary<string, string?> { [new string('K', 65)] = "x" }, "64 characters"),
            (new Dictionary<string, string?> { ["Estado"] = new string('V', 513) }, "attributes.Estado")
        })
        {
            using var response = await Send(route, JsonSerializer.Serialize(new[] { new { row = 3, attributes } }));
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
            Assert.That(problem.GetProperty("detail").GetString(), Does.Contain(field));
        }
    }
}
