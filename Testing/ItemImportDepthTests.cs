using System.Text.Json;

namespace Testing;

/// <summary>
/// The import writes the same tree the routes write, so it obeys the same depth bound. With
/// `ITEMS_MAX_DEPTH=2` the sample file's rows would land at level 3 (category → subcategory →
/// record), and the import used to write them anyway.
/// </summary>
[NonParallelizable]
[TestFixture]
public sealed class ItemImportDepthTests : ApiTestBase
{
    protected override bool FreshApiPerTest => true;

    protected override ApiFactory CreateFactory() => new(maxDepth: 2);

    private static IEnumerable<string> Reasons(JsonElement row) =>
        row.GetProperty("reasons").EnumerateArray().Select(reason => reason.GetString() ?? string.Empty);

    [Test]
    public async Task PreviewRejectsEveryRowThatWouldLandPastTheBoundAndSaysWhy()
    {
        var preview = await Preview("import-sample.xlsx");
        var rows = Items(preview).ToList();

        Assert.That(rows, Is.Not.Empty);
        Assert.That(rows.All(row => row.GetProperty("status").GetString() == "red"), Is.True,
            "under a bound of 2, every row of this file lands at level 3");
        Assert.That(rows.Count(row => Reasons(row).Any(reason =>
                reason.Contains("nivel 3") && reason.Contains("máximo permitido es 2"))),
            Is.EqualTo(rows.Count),
            "each rejected row carries the reason, not silence");

        var summary = preview.GetProperty("summary");
        Assert.Multiple(() =>
        {
            Assert.That(summary.GetProperty("skipped").GetInt32(), Is.EqualTo(rows.Count),
                "a rejected row is counted as rejected");
            Assert.That(summary.GetProperty("importable").GetInt32(), Is.Zero);
        });
    }

    [Test]
    public async Task ImportWritesNothingWhenEveryRowWouldLandPastTheBound()
    {
        var preview = await Preview("import-sample.xlsx");
        var rows = Items(preview).ToList();

        var result = await Import("import-sample.xlsx");
        TestContext.Out.WriteLine($"import under a bound of 2: {result.GetRawText()}");

        Assert.Multiple(() =>
        {
            Assert.That(result.GetProperty("createdItems").GetInt32(), Is.Zero);
            Assert.That(result.GetProperty("createdGroups").GetInt32(), Is.Zero, "no subcategory for rejected rows");
            Assert.That(result.GetProperty("createdSheets").GetInt32(), Is.Zero, "no category either");
            Assert.That(result.GetProperty("skippedRed").GetInt32(), Is.EqualTo(rows.Count));
        });
        Assert.That(await CountItems(), Is.Zero, "the database did not grow a tree the routes would refuse");
    }
}

/// <summary>
/// The control: one level of room more and the same file imports, so the rule rejects what is past
/// the bound instead of every import.
/// </summary>
[NonParallelizable]
[TestFixture]
public sealed class ItemImportDepthBoundTests : ApiTestBase
{
    protected override bool FreshApiPerTest => true;

    protected override ApiFactory CreateFactory() => new(maxDepth: 3);

    [Test]
    public async Task OneLevelOfRoomIsEnoughForTheSameFileToImport()
    {
        var result = await Import("import-sample.xlsx");

        Assert.Multiple(() =>
        {
            Assert.That(result.GetProperty("createdItems").GetInt32(), Is.EqualTo(6));
            Assert.That(result.GetProperty("createdSheets").GetInt32(), Is.EqualTo(4));
            Assert.That(result.GetProperty("createdGroups").GetInt32(), Is.EqualTo(4));
        });
    }
}
