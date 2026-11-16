using System.Net;
using System.Text.Json;
using IMCAPI.DB;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Testing;

/// <summary>
/// The import keeps the configuration's shape. A spreadsheet row knows its SUB CATEGORIA, and the
/// SUB CATEGORIA belongs to a Category in `Constants` — so the tree the import builds is
/// `Hardware → BASE TECLADO SENCILLA → elemento`, not `BACKLINE → BASE TECLADO → elemento` with the
/// sheet as an invented root. These tests pin the placement rules end to end: what the preview
/// promises, and what the import then writes.
/// </summary>
[NonParallelizable]
[TestFixture]
public sealed class ItemImportPlacementTests : ApiTestBase
{
    /// <summary>The category, then the group, then the label — the shape of the tree.</summary>
    private static Dictionary<string, Dictionary<string, string[]>> Tree(JsonElement preview) =>
        preview.GetProperty("sheets").EnumerateArray().ToDictionary(
            sheet => sheet.GetProperty("name").GetString()!,
            sheet => sheet.GetProperty("groups").EnumerateArray().ToDictionary(
                group => group.GetProperty("name").GetString()!,
                group => group.GetProperty("items").EnumerateArray()
                    .Select(item => item.GetProperty("label").GetString()!).ToArray()));

    [Test]
    [Order(1)]
    public async Task Preview_PlacesEveryRowUnderTheCategoryThatOwnsItsSubcategory()
    {
        var tree = Tree(await Preview("import-placement.csv", Csv));

        Assert.Multiple(() =>
        {
            Assert.That(tree.Keys, Is.EquivalentTo(new[] { "Hardware", "Sonido", "Cases", "Other" }),
                "the sheet names are gone: the categories of the configuration are the roots");

            Assert.That(tree["Hardware"].Keys, Is.EquivalentTo(new[] { "BASE TECLADO SENCILLA", "BASE CONGA DOBLE" }),
                "an exact subcategory keeps its name; a truncated one is completed from Constants");
            Assert.That(tree["Hardware"]["BASE TECLADO SENCILLA"], Is.EqualTo(new[] { "BASE TECLADO YAMAHA P45" }));

            Assert.That(tree["Sonido"].Keys, Is.EqualTo(new[] { "MICROFONOS" }),
                "«MICROFONO» is the singular of the configuration's MICROFONOS");

            Assert.That(tree["Cases"].Keys, Is.EqualTo(new[] { "Cases" }),
                "a subcategory that is itself a category hangs the element directly off it");

            Assert.That(tree["Other"].Keys, Is.EquivalentTo(new[] { "UPS", "INVENTO NUEVO" }),
                "an extended name lands on the configuration's UPS; an unknown one lands in Other, named as it came");
        });
    }

    [Test]
    [Order(1)]
    public async Task Preview_OnlyCallsAPlacementGreenWhenTheConfigurationNamedIt()
    {
        var preview = await Preview("import-placement.csv", Csv);

        var statuses = preview.GetProperty("sheets").EnumerateArray()
            .SelectMany(sheet => sheet.GetProperty("groups").EnumerateArray())
            .ToDictionary(
                group => group.GetProperty("name").GetString()!,
                group => group.GetProperty("status").GetString()!);

        Assert.Multiple(() =>
        {
            Assert.That(statuses["BASE TECLADO SENCILLA"], Is.EqualTo("green"),
                "the configuration spells it exactly like the file does");
            Assert.That(statuses["BASE CONGA DOBLE"], Is.EqualTo("yellow"),
                "a rule placed it: importable, but the user should see that it was a rule");
            Assert.That(statuses["MICROFONOS"], Is.EqualTo("yellow"));
            Assert.That(statuses["UPS"], Is.EqualTo("yellow"));
            Assert.That(statuses["INVENTO NUEVO"], Is.EqualTo("yellow"),
                "nothing in the configuration owns it, so it is not green");
        });

        // Every yellow row says why, in the row itself, not only in the group header.
        var reasons = preview.GetProperty("sheets").EnumerateArray()
            .SelectMany(sheet => sheet.GetProperty("groups").EnumerateArray())
            .SelectMany(group => group.GetProperty("items").EnumerateArray())
            .Where(item => item.GetProperty("status").GetString() == "yellow")
            .SelectMany(item => item.GetProperty("reasons").EnumerateArray().Select(reason => reason.GetString()!))
            .ToList();
        Assert.That(reasons.Any(reason => reason.Contains("BASE CONGA DOBLE")), Is.True,
            "the row that was truncated says where it went");
        Assert.That(reasons.Any(reason => reason.Contains("no está en la configuración")), Is.True,
            "the row with no home says so");
    }

    [Test]
    [Order(1)]
    public async Task AnAccentedSubcategoryFindsTheConfigurationEntryThatHasNoAccent()
    {
        // This has to be an integration test, not a unit one: the API runs with
        // InvariantGlobalization, so Unicode normalization folds in the test project and is a no-op
        // there — measured, after the ladder matched in a unit test while the running API parked
        // SILLA BATERÍA in Other. The fold is a table now, and this is the test that proves it.
        var tree = Tree(await Preview("import-accents.csv", Csv));

        Assert.Multiple(() =>
        {
            Assert.That(tree.ContainsKey("Other"), Is.False, "nothing here belongs in the catch-all");
            Assert.That(tree["Hardware"].ContainsKey("SILLA BATERIA"), Is.True,
                "SILLA BATERÍA is the same shelf as SILLA BATERIA");
            Assert.That(tree["Hardware"].ContainsKey("PEDAL DOBLE BATERIA"), Is.True,
                "and the accented pedal is the configured pedal");
            Assert.That(tree["Hardware"].ContainsKey("BASE TECLADO SENCILLA"), Is.True,
                "the file's TECALDO is still corrected to the configured label");
        });
    }

    [Test]
    [Order(1)]
    public async Task Preview_PlacesYellowSimilarityProposalsUnderConfiguredNames()
    {
        var preview = await Preview("import-similarity.csv", Csv);
        var groups = preview.GetProperty("sheets").EnumerateArray()
            .SelectMany(sheet => sheet.GetProperty("groups").EnumerateArray()
                .Select(group => (Category: sheet.GetProperty("name").GetString()!, Group: group)))
            .ToDictionary(entry => entry.Group.GetProperty("name").GetString()!, entry => entry);

        foreach (var (source, label, category) in new[]
        {
            ("CONGA03", "CONGA", "Percusion"),
            ("TUMBADORA03", "TUMBADORA", "Percusion"),
            ("TRIGER", "TRIGGER", "Instruments"),
            ("BAJO ELECTROACUSTICO", "BAJO ELECTRO-ACUSTICO", "Instruments"),
        })
        {
            var (destination, group) = groups[label];
            var suggestion = group.GetProperty("suggestion");
            var percent = suggestion.GetProperty("percent").GetInt32();
            Assert.Multiple(() =>
            {
                Assert.That(destination, Is.EqualTo(category), $"{source} lands under its configured category");
                Assert.That(group.GetProperty("name").GetString(), Is.EqualTo(label));
                Assert.That(group.GetProperty("status").GetString(), Is.EqualTo("yellow"));
                Assert.That(group.GetProperty("yellow").GetInt32(), Is.EqualTo(1));
                Assert.That(group.GetProperty("red").GetInt32(), Is.Zero);
                Assert.That(suggestion.GetProperty("label").GetString(), Is.EqualTo(label));
                Assert.That(suggestion.GetProperty("category").GetString(), Is.EqualTo(category));
                Assert.That(percent, Is.InRange(80, 100));
                Assert.That(group.GetProperty("note").GetString(), Does.Contain($"({percent}%)"));
                Assert.That(group.GetProperty("items")[0].GetProperty("status").GetString(), Is.EqualTo("yellow"));
            });
        }

        foreach (var source in new[] { "CASE PARA CONGAS", "BODEGA", "CASE", "GUITARRA FENDER" })
        {
            Assert.Multiple(() =>
            {
                Assert.That(groups[source].Category, Is.EqualTo("Other"));
                Assert.That(groups[source].Group.TryGetProperty("suggestion", out var suggestion)
                    ? suggestion.ValueKind == JsonValueKind.Null : true, Is.True);
            });
        }
        Assert.Multiple(() =>
        {
            var atril = groups.Values.Single(entry => entry.Group.GetProperty("items").EnumerateArray()
                .Any(item => item.GetProperty("label").GetString() == "ATRIL DE PRUEBA"));
            Assert.That(atril.Group.TryGetProperty("suggestion", out var atrilSuggestion)
                ? atrilSuggestion.ValueKind == JsonValueKind.Null : true, Is.True,
                "the competing whole-word candidates ATRIL and CONGA do not justify a proposal");
            Assert.That(groups["BASE TECLADO SENCILLA"].Category, Is.EqualTo("Hardware"));
            Assert.That(groups["BASE TECLADO SENCILLA"].Group.GetProperty("status").GetString(), Is.EqualTo("green"));
            Assert.That(groups["BASE TECLADO SENCILLA"].Group.TryGetProperty("suggestion", out var suggestion)
                ? suggestion.ValueKind == JsonValueKind.Null : true, Is.True);
        });
    }

    [Test]
    [Order(3)]
    public async Task Import_WritesProposedYellowRowsUnderTheirConfiguredGroups()
    {
        var response = await PostImport("import-similarity.csv", Csv, includeYellow: true);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var items = await context.Items.IgnoreQueryFilters().ToListAsync();
        foreach (var (label, group, category) in new[]
        {
            ("CONGA DE PRUEBA", "CONGA", "Percusion"),
            ("TUMBADORA DE PRUEBA", "TUMBADORA", "Percusion"),
            ("TRIGGER DE PRUEBA", "TRIGGER", "Instruments"),
            ("BAJO DE PRUEBA", "BAJO ELECTRO-ACUSTICO", "Instruments"),
        })
        {
            var item = items.Single(row => row.Label == label);
            var parent = items.Single(row => row.Id == item.ParentId);
            var root = items.Single(row => row.Id == parent.ParentId);
            Assert.Multiple(() =>
            {
                Assert.That(parent.Label, Is.EqualTo(group));
                Assert.That(root.Label, Is.EqualTo(category));
            });
        }
    }

    [Test]
    [Order(2)]
    public async Task Import_WritesTheConfigurationTree_NotTheSheetNames()
    {
        // includeYellow is left out of the body on purpose: what the route does with nothing said
        // about the yellow rows is part of what this test pins.
        var response = await PostImport("import-placement.csv", Csv, includeYellow: null);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());

        using var scope = Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var items = await context.Items.IgnoreQueryFilters().ToListAsync();

        string? ParentOf(string label)
        {
            var item = items.FirstOrDefault(row => row.Label == label);
            if (item?.ParentId is not { } parentId) return null;
            return items.FirstOrDefault(row => row.Id == parentId)?.Label;
        }

        Assert.Multiple(() =>
        {
            Assert.That(items.Any(item => item.Label == "Hardware" && item.ParentId == null), Is.True,
                "the category is a root");
            Assert.That(items.Any(item => item.Label == "BASE TECLADO SENCILLA" && ParentOf("BASE TECLADO SENCILLA") == "Hardware"),
                Is.True, "the subcategory hangs off its category");
            Assert.That(ParentOf("BASE TECLADO YAMAHA P45"), Is.EqualTo("BASE TECLADO SENCILLA"),
                "and the element off the subcategory");
            Assert.That(ParentOf("TORRE CONGA DOBLE"), Is.EqualTo("BASE CONGA DOBLE"));
            Assert.That(ParentOf("MICROFONO DE MANO"), Is.EqualTo("MICROFONOS"));
            Assert.That(items.Any(item => item.Label == "Cases" && item.ParentId == null), Is.True);
            // A case's category has no subcategories of its own: the record hangs off the root, not off
            // a group named like its parent.
            Assert.That(ParentOf("ESTUCHE GRANDE"), Is.EqualTo("Cases"),
                "a subcategory that is the category itself is not created twice");
            Assert.That(items.Count(item => item.Label == "Cases"), Is.EqualTo(1));

            Assert.That(items.Any(item => item.Label is "BACKLINE" or "SONIDO" or "OTROS"), Is.False,
                "the spreadsheet's sheets never become roots");
        });
    }
}
