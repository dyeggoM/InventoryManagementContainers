using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IMCAPI.DB;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Testing;

/// <summary>
/// The same feature, fed from a CSV instead of a workbook. The verdicts, the alias table, the dedupe
/// and the writer are the code under test already: what these pin is that the parse delivers the rows
/// those parts expect — the Spanish locale's `;`, RFC 4180 quoting, and a file whose only name is its
/// own.
///
/// Every CSV case is its own fixture file, so a failure names the file that broke.
/// </summary>
[NonParallelizable]
[TestFixture]
public sealed class ItemImportCsvTests
{
    private const string Password = "AuthTest!Secure123";
    private const string Csv = "text/csv";
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
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
    public async Task Preview_JudgesTheCsvRowsExactlyLikeTheEquivalentWorkbook()
    {
        // import-sample.csv carries the STOCK sheet of import-sample.xlsx: same rows, same codes,
        // same descriptions. The parse differs; the verdict must not.
        var workbook = await Preview("import-sample.xlsx", Xlsx);
        var expected = Items(workbook)
            .ToDictionary(item => Label(item)!, item => item.GetProperty("status").GetString()!);

        var csv = await Preview("import-sample.csv", Csv);
        var actual = Items(csv)
            .ToDictionary(item => Label(item)!, item => item.GetProperty("status").GetString()!);

        // The workbook also carries the CASES sheet, which the CSV does not: compare the rows both hold.
        var shared = actual.Keys.Intersect(expected.Keys).ToList();
        Assert.That(shared, Is.Not.Empty);
        foreach (var label in shared)
            Assert.That(actual[label], Is.EqualTo(expected[label]), $"«{label}» is judged the same in both files");
    }

    [Test]
    public async Task Preview_SummarisesTheCsvTheSameWayAsTheWorkbookSheetItMirrors()
    {
        var preview = await Preview("import-sample.csv", Csv);
        var summary = preview.GetProperty("summary");

        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("rows").GetInt32(), Is.EqualTo(8),
                "eight records, not nine: HARDWARE is a section title");
            Assert.That(summary.GetProperty("sections").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("green").GetInt32(), Is.EqualTo(1));
            Assert.That(summary.GetProperty("yellow").GetInt32(), Is.EqualTo(4),
                "the ambiguous BASE rows, the unknown brand, no code and a description too short");
            Assert.That(summary.GetProperty("red").GetInt32(), Is.EqualTo(3),
                "no description, and both rows sharing one code");
            Assert.That(summary.GetProperty("importable").GetInt32(), Is.EqualTo(5));
        });
    }

    [Test]
    public async Task Preview_ReportsTheSubcategoryCorrection_AndTheSectionTitle()
    {
        var preview = await Preview("import-sample.csv", Csv);

        Assert.Multiple(() =>
        {
            Assert.That(preview.GetProperty("normalizations").EnumerateArray()
                    .Select(value => value.GetString())
                    .Any(text => text!.Contains("GTAR COMBO") && text.Contains("GTR-COMBO")), Is.True,
                "the alias table is the same one, fed by the same subcategory column");

            Assert.That(preview.GetProperty("summary").GetProperty("sections").GetInt32(), Is.EqualTo(1));
            Assert.That(preview.GetProperty("sheets").EnumerateArray()
                    .SelectMany(sheet => sheet.GetProperty("groups").EnumerateArray())
                    .Select(group => group.GetProperty("name").GetString()),
                Has.No.Member("HARDWARE"), "a title row is counted apart, not imported as a group");
        });
    }

    [Test]
    public async Task Preview_ReadsAQuotedSemicolonAndCommaAsOneField()
    {
        var preview = await Preview("import-sample.csv", Csv);
        var item = Items(preview).Single(candidate => Label(candidate) == "BASE DE BRAZO DW 9000");

        Assert.Multiple(() =>
        {
            Assert.That(item.GetProperty("notes").GetString(),
                Is.EqualTo("NOTA CON COMA, Y PUNTO Y COMA; DENTRO DE UNA CELDA"),
                "the delimiters inside the quotes belong to the cell");
            // A split row would have shifted every column after it, so the code and the brand are the
            // evidence: they are in the cells the header named.
            Assert.That(item.GetProperty("code").GetString(), Is.EqualTo("-T0001"));
            Assert.That(item.GetProperty("brand").GetString(), Is.EqualTo("DW"));
        });
    }

    [Test]
    public async Task Preview_KeepsAMultilineQuotedFieldInOneRow()
    {
        var preview = await Preview("import-sample.csv", Csv);
        var item = Items(preview).Single(candidate => Label(candidate) == "GUITARRA COMBO");

        Assert.Multiple(() =>
        {
            Assert.That(item.GetProperty("notes").GetString(), Is.EqualTo("LINEA UNO LINEA DOS"),
                "the newline inside the quotes is one cell, not the end of the row");
            Assert.That(Items(preview).Count(), Is.EqualTo(8), "and it did not become an extra record");
        });
    }

    [Test]
    public async Task Preview_PlacesACsvByItsSubcategory_NotByItsFileName()
    {
        var preview = await Preview("import-cases.csv", Csv);
        var sheets = preview.GetProperty("sheets").EnumerateArray().ToList();

        Assert.That(sheets.Select(sheet => sheet.GetProperty("name").GetString()), Has.No.Member("IMPORT-CASES"),
            "a CSV has no sheet names, and its file name is not a category either");
        Assert.That(sheets, Has.Count.EqualTo(1),
            "the row's subcategory is not in the configuration, so it lands in the one category that catches those");
        Assert.That(sheets[0].GetProperty("name").GetString(), Is.EqualTo("Other"));
        Assert.That(sheets[0].GetProperty("groups").EnumerateArray().Single().GetProperty("note").GetString(),
            Does.Contain("no está en la configuración"), "and the preview says why it went there");
    }

    [Test]
    public async Task Preview_ReadsACommaExportWithItsHeaderOnTheFirstRow()
    {
        // import-cases.csv is the CASES sheet of the workbook, comma separated and without a title
        // row: if the delimiter were guessed wrong this would be one unnamed column and no records.
        var preview = await Preview("import-cases.csv", Csv);
        var sheet = preview.GetProperty("sheets").EnumerateArray().Single();
        var item = ItemsOfSheet(sheet).Single();

        Assert.Multiple(() =>
        {
            Assert.That(sheet.GetProperty("groups").EnumerateArray()
                .Select(group => group.GetProperty("name").GetString()), Is.EquivalentTo(new[] { "CASE PARA CONGAS" }));
            Assert.That(Label(item), Is.EqualTo("CASE 1M X 1M X 50CM"));
            Assert.That(item.GetProperty("code").GetString(), Is.EqualTo("-K001"));
            Assert.That(item.GetProperty("status").GetString(), Is.EqualTo("yellow"),
                "the row reads; what is not in the configuration is where it belongs, and the preview says so");
        });
    }

    [Test]
    public async Task Preview_RefusesACsvThatOnlyHasAHeader()
    {
        var response = await Send("/api/import/preview", "import-header-only.csv", Csv);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("filas de datos"),
            "an empty tree would read as a successful import of nothing");
    }

    [Test]
    public async Task Preview_RefusesAnEmptyCsv()
    {
        var response = await Send("/api/import/preview", "import-blank.csv", Csv);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    private static string FixturePath(string name) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);

    private async Task<HttpResponseMessage> Send(string route, string fixture, string contentType)
    {
        using var payload = new MultipartFormDataContent();
        var file = new ByteArrayContent(await File.ReadAllBytesAsync(FixturePath(fixture)));
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        payload.Add(file, "file", fixture);
        return await _client.PostAsync(route, payload);
    }

    private async Task<JsonElement> Preview(string fixture, string contentType)
    {
        var response = await Send("/api/import/preview", fixture, contentType);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    private static IEnumerable<JsonElement> ItemsOfSheet(JsonElement sheet) =>
        sheet.GetProperty("groups").EnumerateArray()
            .SelectMany(group => group.GetProperty("items").EnumerateArray());

    private static IEnumerable<JsonElement> Items(JsonElement preview) =>
        preview.GetProperty("sheets").EnumerateArray().SelectMany(ItemsOfSheet);

    private static string? Label(JsonElement item) => item.GetProperty("label").GetString();
}

/// <summary>
/// The second call on a CSV file: the tree it builds is the one the workbook builds, with the file's
/// name as its root. Each test gets its own database, because these tests change the tree.
/// </summary>
[NonParallelizable]
[TestFixture]
public sealed class ItemImportCsvWriteTests
{
    private const string Password = "AuthTest!Secure123";
    private const string Csv = "text/csv";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
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

    [TearDown]
    public void Stop()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task Import_BuildsTheTreeTheCsvDescribes()
    {
        var result = await Import("import-sample.csv");

        Assert.Multiple(() =>
        {
            Assert.That(result.GetProperty("createdSheets").GetInt32(), Is.EqualTo(3),
                "Hardware, Amplifiers and Cymbals — the categories the rows belong to");
            Assert.That(result.GetProperty("createdGroups").GetInt32(), Is.EqualTo(3),
                "BASE, GTR-COMBO and HIHAT: ATRIL only held rows that were red");
            Assert.That(result.GetProperty("createdItems").GetInt32(), Is.EqualTo(5),
                "one green and four yellow");
            Assert.That(result.GetProperty("skippedRed").GetInt32(), Is.EqualTo(3));
        });

        var tree = await _client.GetFromJsonAsync<JsonElement>("/api/item", Json);
        var roots = tree.GetProperty("tree").EnumerateArray()
            .Select(node => node.GetProperty("label").GetString()!)
            .ToList();
        Assert.That(roots, Is.EquivalentTo(new[] { "Hardware", "Amplifiers", "Cymbals" }),
            "the roots are the categories, not the file name");

        Assert.That(tree.GetProperty("tree").EnumerateArray()
                .Single(node => node.GetProperty("label").GetString() == "Amplifiers")
                .GetProperty("children").EnumerateArray()
                .Select(child => child.GetProperty("label").GetString()),
            Is.EqualTo(new[] { "GTR-COMBO" }),
            "the corrected spelling is what the tree carries, from a CSV as from a workbook");
    }

    [Test]
    public async Task Import_RefusesACsvWithNoDataRows_AndWritesNothing()
    {
        var response = await ImportRaw("import-header-only.csv");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await CountItems(), Is.EqualTo(0), "a refused file leaves the tree exactly as it was");
    }

    private async Task<int> CountItems()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        return await context.Items.IgnoreQueryFilters().CountAsync();
    }

    private async Task<HttpResponseMessage> ImportRaw(string fixture)
    {
        using var payload = new MultipartFormDataContent();
        var file = new ByteArrayContent(await File.ReadAllBytesAsync(
            Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", fixture)));
        file.Headers.ContentType = new MediaTypeHeaderValue(Csv);
        payload.Add(file, "file", fixture);
        payload.Add(new StringContent("true"), "includeYellow");
        return await _client.PostAsync("/api/import", payload);
    }

    private async Task<JsonElement> Import(string fixture)
    {
        var response = await ImportRaw(fixture);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }
}
