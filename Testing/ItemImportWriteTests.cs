using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IMCAPI.DB;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Testing;

/// <summary>
/// The second call: the one that writes. Each test gets its own database, because these tests change
/// the tree and one of them runs the same import twice.
/// </summary>
[NonParallelizable]
[TestFixture]
public sealed class ItemImportWriteTests
{
    private const string Password = "AuthTest!Secure123";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    private static string FixturePath => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "import-sample.xlsx");

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
    public async Task Import_BuildsTheTreeTheFileDescribes()
    {
        var result = await Import();

        Assert.Multiple(() =>
        {
            Assert.That(result.GetProperty("createdSheets").GetInt32(), Is.EqualTo(2), "STOCK and CASES");
            Assert.That(result.GetProperty("createdGroups").GetInt32(), Is.EqualTo(4),
                "BASE, GTR COMBO and HIHAT under STOCK, plus CASE PARA CONGAS under CASES: ATRIL only "
                + "held the row whose code was duplicated, and a red row opens no group");
            Assert.That(result.GetProperty("createdItems").GetInt32(), Is.EqualTo(6),
                "one green and four yellow in STOCK, one green in CASES");
            Assert.That(result.GetProperty("skippedRed").GetInt32(), Is.EqualTo(3));
        });

        var tree = await _client.GetFromJsonAsync<JsonElement>("/api/item", Json);
        var stock = tree.GetProperty("tree").EnumerateArray()
            .Single(node => node.GetProperty("label").GetString() == "STOCK");
        Assert.That(stock.GetProperty("children").EnumerateArray().Select(child => child.GetProperty("label").GetString()),
            Is.EquivalentTo(new[] { "BASE", "GTR COMBO", "HIHAT" }),
            "the corrected spelling is what the tree carries, not the one in the file");
    }

    [Test]
    public async Task Import_KeepsTheCodeAndTheBrandInTheAttributes()
    {
        await Import();

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var record = await context.Items.SingleAsync(item => item.Label == "BASE DE BRAZO DW 9000");

        var attributes = JsonDocument.Parse(record.Attributes!).RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(attributes.GetProperty("Code").GetString(), Is.EqualTo("-T0001"));
            Assert.That(attributes.GetProperty("Brand").GetString(), Is.EqualTo("DW"));
            Assert.That(attributes.GetProperty("Reference").GetString(), Is.EqualTo("9700"));
        });
    }

    [Test]
    public async Task Import_NeverWritesARedRow()
    {
        await Import();

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var labels = await context.Items.Select(item => item.Label).ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(labels, Has.No.Member("ATRIL REPETIDO"), "the row whose code was already used");
            Assert.That(labels, Has.No.Member("SIN NOMBRE"), "nothing invented for a row without a description");
            Assert.That(labels, Has.No.Member("ATRIL DE PARTITURA"),
                "the other half of a duplicated code stays out too: half a file in is worse than none");
        });
    }

    [Test]
    public async Task Import_CanLeaveTheYellowRowsOut()
    {
        var result = await Import(includeYellow: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.GetProperty("createdItems").GetInt32(), Is.EqualTo(2), "only the green rows");
            Assert.That(result.GetProperty("skippedYellow").GetInt32(), Is.EqualTo(4));
        });
    }

    [Test]
    public async Task Import_HonoursTheCodesTheUserDeselected()
    {
        var result = await Import(skipCodes: new[] { "-T0002", "-T0006" });

        Assert.That(result.GetProperty("createdItems").GetInt32(), Is.EqualTo(4),
            "six importable rows minus the two the user unticked");
    }

    [Test]
    public async Task Import_Twice_CreatesNothingTheSecondTime()
    {
        var first = await Import();
        var afterFirst = await CountItems();
        var second = await Import();
        var afterSecond = await CountItems();

        Assert.Multiple(() =>
        {
            Assert.That(first.GetProperty("createdItems").GetInt32(), Is.EqualTo(6));
            Assert.That(second.GetProperty("createdItems").GetInt32(), Is.EqualTo(0), "the file is already in");
            Assert.That(second.GetProperty("createdSheets").GetInt32(), Is.EqualTo(0), "no second STOCK");
            Assert.That(second.GetProperty("createdGroups").GetInt32(), Is.EqualTo(0));
            Assert.That(afterSecond, Is.EqualTo(afterFirst), "the tree did not grow");
        });
    }

    [Test]
    public async Task Import_LeavesOneAuditRowWithTheCounts()
    {
        await Import();

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var rows = await context.AuditLogs.Where(row => row.Action == "ItemImport").ToListAsync();

        Assert.That(rows, Has.Count.EqualTo(1), "one summary row, not one per record");
        Assert.That(rows[0].AfterJson, Does.Contain("\"createdItems\":6"));
    }

    [Test]
    public async Task Import_RequiresAnAdmin()
    {
        using var anonymous = _factory.CreateClient();
        using var payload = new MultipartFormDataContent();
        payload.Add(new ByteArrayContent(await File.ReadAllBytesAsync(FixturePath)), "file", "sample.xlsx");

        var response = await anonymous.PostAsync("/api/import", payload);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    private async Task<int> CountItems()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        return await context.Items.IgnoreQueryFilters().CountAsync();
    }

    private async Task<JsonElement> Import(bool includeYellow = true, string[]? skipCodes = null)
    {
        using var payload = new MultipartFormDataContent();
        var file = new ByteArrayContent(await File.ReadAllBytesAsync(FixturePath));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        payload.Add(file, "file", Path.GetFileName(FixturePath));
        payload.Add(new StringContent(includeYellow ? "true" : "false"), "includeYellow");
        foreach (var code in skipCodes ?? Array.Empty<string>()) payload.Add(new StringContent(code), "skipCodes");

        var response = await _client.PostAsync("/api/import", payload);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }
}
