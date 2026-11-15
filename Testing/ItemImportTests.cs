using System.Net;

namespace Testing;

/// <summary>
/// The import preview is the part a person reads before anything is written: these tests pin what it
/// says about a spreadsheet with every case in it — clean rows, doubtful rows, and rows that must not
/// be imported — and that saying it changes nothing.
/// </summary>
[NonParallelizable]
[TestFixture]
public sealed class ItemImportTests : ApiTestBase
{

    [Test]
    public async Task Preview_SortsEveryRowIntoGreenYellowOrRed()
    {
        var preview = await Preview("import-sample.xlsx");

        var summary = preview.GetProperty("summary");
        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("green").GetInt32(), Is.EqualTo(1),
                "only the row whose subcategory the configuration spells the same way");
            Assert.That(summary.GetProperty("yellow").GetInt32(), Is.EqualTo(5),
                "the ambiguous BASE rows, the unknown brand, the row with no code, the case with no category "
                + "and the description too short to identify anything");
            Assert.That(summary.GetProperty("red").GetInt32(), Is.EqualTo(3),
                "no description, and both rows sharing one code");
            Assert.That(summary.GetProperty("importable").GetInt32(), Is.EqualTo(6));
        });
    }

    [Test]
    public async Task Preview_ExplainsEveryYellowRow()
    {
        var preview = await Preview("import-sample.xlsx");
        var items = Items(preview);

        Assert.That(items.Single(item => Label(item) == "BASE DE BRAZO DW 5000").GetProperty("reasons")
            .EnumerateArray().Select(reason => reason.GetString()).Any(text => text!.Contains("catálogo de Brand")), Is.True,
            "a brand outside the catalogue is yellow, and says which brand");

        Assert.That(items.Single(item => Label(item) == "BASE SIN CODIGO").GetProperty("reasons")
            .EnumerateArray().Select(reason => reason.GetString()).Any(text => text!.Contains("Sin código")), Is.True,
            "a row without a code can be imported, but the user is told it will be indistinguishable");

        // The corrected spelling is not a problem with the row, it is a fact about the group: the
        // row goes in green and the group says what the file called it.
        Assert.That(items.Single(item => Label(item) == "GUITARRA COMBO").GetProperty("status").GetString(),
            Is.EqualTo("green"), "a spelling the alias table knows is not a reason to hold the row back");
        var corrected = preview.GetProperty("sheets").EnumerateArray()
            .SelectMany(sheet => sheet.GetProperty("groups").EnumerateArray())
            .Single(group => group.GetProperty("name").GetString() == "GTR-COMBO");
        Assert.That(corrected.GetProperty("note").GetString(), Does.Contain("corregida"),
            "and the group says the file wrote GTAR COMBO");

        Assert.That(items.Single(item => Label(item) == "BASE DE BRAZO DW 9000").GetProperty("reasons")
            .EnumerateArray().Select(reason => reason.GetString()).Any(text => text!.Contains("varias subcategorías")),
            Is.True, "an abbreviation that fits several subcategories says so instead of picking one");
    }

    [Test]
    public async Task Preview_MarksBothRowsOfARepeatedCode_Red()
    {
        var preview = await Preview("import-sample.xlsx");
        var twins = Items(preview)
            .Where(item => item.TryGetProperty("code", out var code) && code.GetString() == "-T0004")
            .ToList();

        Assert.That(twins, Has.Count.EqualTo(2));
        Assert.That(twins.Select(item => item.GetProperty("status").GetString()), Is.All.EqualTo("red"),
            "half of a duplicated code is not importable: neither row goes in");
    }

    [Test]
    public async Task Preview_ReportsTheSubcategoryCorrections()
    {
        var preview = await Preview("import-sample.xlsx");
        var corrections = preview.GetProperty("normalizations").EnumerateArray()
            .Select(value => value.GetString()).ToList();

        Assert.That(corrections.Any(text => text!.Contains("GTAR COMBO") && text.Contains("GTR-COMBO")), Is.True,
            "the correction names the configuration's label, punctuation included");
    }

    [Test]
    public async Task Preview_SkipsSectionTitlesInsteadOfCallingThemRecords()
    {
        var preview = await Preview("import-sample.xlsx");

        Assert.That(preview.GetProperty("summary").GetProperty("sections").GetInt32(), Is.EqualTo(1),
            "HARDWARE is a section title, not a record without a name");
        Assert.That(preview.GetProperty("sheets").EnumerateArray()
                .SelectMany(sheet => sheet.GetProperty("groups").EnumerateArray())
                .Select(group => group.GetProperty("name").GetString()),
            Has.No.Member("HARDWARE"));
    }

    [Test]
    public async Task Preview_ReadsASheetWhoseHeaderIsTheFirstRow_AndRefusesOneWithoutHeader()
    {
        var preview = await Preview("import-sample.xlsx");
        var labels = Items(preview).Select(item => Label(item)).ToList();

        Assert.That(labels, Does.Contain("CASE 1M X 1M X 50CM"),
            "a sheet without a title row above the header still reads");
        Assert.That(labels, Does.Not.Contain("ESTO NO ES UNA CABECERA"), "a sheet without a header is not a sheet of records");
        Assert.That(preview.GetProperty("issues").EnumerateArray()
            .Any(issue => issue.GetProperty("message").GetString()!.Contains("cabecera")), Is.True,
            "and it says so instead of dropping it silently");
    }

    [Test]
    public async Task Preview_WritesNothing()
    {
        var before = await Inventory();
        await Preview("import-sample.xlsx");
        Assert.That(await Inventory(), Is.EqualTo(before), "a preview is a question, not an import");
    }

    [Test]
    public async Task Preview_RequiresAnAdminAndASpreadsheet()
    {
        using var anonymous = Factory.CreateClient();
        using var payload = new MultipartFormDataContent();
        payload.Add(new ByteArrayContent(new byte[] { 1, 2, 3 }), "file", "sample.xlsx");
        var unauthenticated = await anonymous.PostAsync("/api/import/preview", payload);
        Assert.That(unauthenticated.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Preview_RefusesSomethingThatIsNotASpreadsheet()
    {
        using var payload = new MultipartFormDataContent();
        payload.Add(new ByteArrayContent("hola"u8.ToArray()), "file", "notas.txt");
        var response = await Client.PostAsync("/api/import/preview", payload);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain(".xlsx"));
    }

    [Test]
    public async Task Preview_RefusesACorruptWorkbookWithoutBlowingUp()
    {
        using var payload = new MultipartFormDataContent();
        payload.Add(new ByteArrayContent("this is not a zip"u8.ToArray()), "file", "roto.xlsx");
        var response = await Client.PostAsync("/api/import/preview", payload);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    /// <summary>The tree as the API sends it: the preview must leave this string untouched.</summary>
    private async Task<string> Inventory()
    {
        var response = await Client.GetAsync("/api/item");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return await response.Content.ReadAsStringAsync();
    }
}
