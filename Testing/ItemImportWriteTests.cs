using System.Net;
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
public sealed class ItemImportWriteTests : ApiTestBase
{
    /// <summary>
    /// Each test writes to the tree and one of them imports the same file twice, so each test needs a
    /// database nothing else has touched.
    /// </summary>
    protected override bool FreshApiPerTest => true;

    [Test]
    public async Task Import_BuildsTheTreeTheFileDescribes()
    {
        var result = await Import("import-sample.xlsx");

        Assert.Multiple(() =>
        {
            Assert.That(result.GetProperty("createdSheets").GetInt32(), Is.EqualTo(4),
                "Hardware, Amplifiers, Cymbals and Other — the categories the rows belong to");
            Assert.That(result.GetProperty("createdGroups").GetInt32(), Is.EqualTo(4),
                "BASE under Hardware, GTR-COMBO under Amplifiers, HIHAT under Cymbals and CASE PARA CONGAS "
                + "under Other: ATRIL only held the row whose code was duplicated, and a red row opens no group");
            Assert.That(result.GetProperty("createdItems").GetInt32(), Is.EqualTo(6),
                "one green and four yellow in the workbook sheet, one yellow in the case sheet");
            Assert.That(result.GetProperty("skippedRed").GetInt32(), Is.EqualTo(3));
        });

        var tree = await Client.GetFromJsonAsync<JsonElement>("/api/item", Json);
        var hardware = tree.GetProperty("tree").EnumerateArray()
            .Single(node => node.GetProperty("label").GetString() == "Hardware");
        var amplifiers = tree.GetProperty("tree").EnumerateArray()
            .Single(node => node.GetProperty("label").GetString() == "Amplifiers");

        Assert.Multiple(() =>
        {
            Assert.That(hardware.GetProperty("children").EnumerateArray().Select(child => child.GetProperty("label").GetString()),
                Is.EqualTo(new[] { "BASE" }), "the abbreviation keeps its own words, under the category that owns it");
            Assert.That(amplifiers.GetProperty("children").EnumerateArray().Select(child => child.GetProperty("label").GetString()),
                Is.EqualTo(new[] { "GTR-COMBO" }),
                "the corrected spelling is what the tree carries, not the one in the file");
        });
    }

    [Test]
    public async Task Import_KeepsTheCodeAndTheBrandInTheAttributes()
    {
        await Import("import-sample.xlsx");

        using var scope = PersonaScope();
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
        await Import("import-sample.xlsx");

        using var scope = PersonaScope();
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
        var result = await Import("import-sample.xlsx", includeYellow: false);

        Assert.Multiple(() =>
        {
            Assert.That(result.GetProperty("createdItems").GetInt32(), Is.EqualTo(1), "only the green row");
            Assert.That(result.GetProperty("skippedYellow").GetInt32(), Is.EqualTo(5));
        });
    }

    [Test]
    public async Task Import_HonoursTheCodesTheUserDeselected()
    {
        var result = await Import("import-sample.xlsx", skipCodes: new[] { "-T0002", "-T0006" });

        Assert.That(result.GetProperty("createdItems").GetInt32(), Is.EqualTo(4),
            "six importable rows minus the two the user unticked");
    }

    [Test]
    public async Task Import_Twice_CreatesNothingTheSecondTime()
    {
        var first = await Import("import-sample.xlsx");
        var afterFirst = await CountItems();
        var second = await Import("import-sample.xlsx");
        var afterSecond = await CountItems();

        Assert.Multiple(() =>
        {
            Assert.That(first.GetProperty("createdItems").GetInt32(), Is.EqualTo(6));
            Assert.That(second.GetProperty("createdItems").GetInt32(), Is.EqualTo(0), "the file is already in");
            Assert.That(second.GetProperty("createdSheets").GetInt32(), Is.EqualTo(0), "no second Hardware");
            Assert.That(second.GetProperty("createdGroups").GetInt32(), Is.EqualTo(0));
            Assert.That(afterSecond, Is.EqualTo(afterFirst), "the tree did not grow");
        });
    }

    [Test]
    public async Task Import_LeavesOneAuditRowWithTheCounts()
    {
        await Import("import-sample.xlsx");

        using var scope = PersonaScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var rows = await context.AuditLogs.Where(row => row.Action == "ItemImport").ToListAsync();

        Assert.That(rows, Has.Count.EqualTo(1), "one summary row, not one per record");
        Assert.That(rows[0].AfterJson, Does.Contain("\"createdItems\":6"));

        // The interceptor writes at the persistence layer, so each created node also leaves its own
        // Create row: the summary says what the import was, the per-entity rows say what it made.
        var created = await context.AuditLogs.CountAsync(row => row.Action == "Create" && row.Entity == "Item");
        Assert.That(created, Is.EqualTo(6 + 4 + 4), "six records, four groups and four categories were created");
    }

    [Test]
    public async Task Import_RequiresAnAdmin()
    {
        using var anonymous = Factory.CreateClient();
        using var payload = new MultipartFormDataContent();
        payload.Add(new ByteArrayContent(await File.ReadAllBytesAsync(FixturePath("import-sample.xlsx"))), "file", "sample.xlsx");

        var response = await anonymous.PostAsync("/api/import", payload);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

}
