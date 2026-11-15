using IMCAPI.Services;

namespace Testing;

/// <summary>
/// The placement ladder, on its own. It used to be reachable only through the import endpoint, so a
/// rule could be wrong in a way the fixture never happened to exercise. These tests build the
/// configuration by hand and ask the ladder directly.
///
/// The ladder is the product decision: where a row goes, and whether that placement is certain enough
/// to be imported green. Every rule that fires has to leave a note, because the preview shows it to
/// the person approving the import.
/// </summary>
[TestFixture]
public sealed class SubcategoryPlacementTests
{
    private static SubcategoryPlacement.CategoryMap Map()
    {
        // The shape of the real configuration: categories, and under each one the subcategories that
        // category owns. Two categories deliberately share a word, which is what makes the resolution
        // order matter.
        // The keys are what LoadAsync produces: case and punctuation do not distinguish two names, so
        // the map has to be built the same way or a test would be exercising a different dictionary.
        static string Key(string label) =>
            new string(label.ToUpperInvariant().Select(ch => char.IsLetterOrDigit(ch) || ch == ' ' ? ch : ' ')
                .ToArray())
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Aggregate("", (left, word) => left.Length == 0 ? word : left + " " + word);

        var subs = new Dictionary<string, SubcategoryPlacement.CategoryValue>(StringComparer.Ordinal);
        void Add(string category, string label)
        {
            var key = Key(label);
            subs[key] = new SubcategoryPlacement.CategoryValue(category, label, key);
        }

        Add("Hardware", "BASE TECLADO SENCILLA");
        Add("Hardware", "BASE TECLADO SENCILLA EN Z");
        Add("Hardware", "BASE CONGA DOBLE");
        Add("Hardware", "BASE CONGA SENCILLA");
        Add("Hardware", "BASE HIHAT");
        Add("Hardware", "MESA PERCUSION");
        Add("Hardware", "BASE GUITARRA SENCILLA");
        Add("Hardware", "BASE GUITARRA SENCILLA BAJA");
        Add("Sonido", "MICROFONOS");
        Add("Sonido", "CONSOLAS");
        Add("Amplifiers", "GTR-COMBO");
        Add("Cymbals", "HIHAT");
        Add("Other", "UPS");

        var byLabel = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HARDWARE"] = "Hardware", ["SONIDO"] = "Sonido", ["AMPLIFIERS"] = "Amplifiers",
            ["CYMBALS"] = "Cymbals", ["CASES"] = "Cases", ["OTHER"] = "Other",
        };
        return new SubcategoryPlacement.CategoryMap(subs, byLabel, "Other");
    }

    private static SubcategoryPlacement.Placement Resolve(string? subcategory, out SortedSet<string> notes)
    {
        notes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        return SubcategoryPlacement.Resolve(subcategory, "BACKLINE", Map(), notes);
    }

    [Test]
    public void AnExactNameIsPlacedWithoutADoubt()
    {
        var placement = Resolve("BASE TECLADO SENCILLA", out var notes);

        Assert.Multiple(() =>
        {
            Assert.That(placement.Category, Is.EqualTo("Hardware"));
            Assert.That(placement.Group, Is.EqualTo("BASE TECLADO SENCILLA"));
            Assert.That(placement.Certain, Is.True, "the configuration spells it exactly like the file");
            Assert.That(placement.Note, Is.Null);
            Assert.That(notes, Is.Empty, "nothing had to be corrected");
        });
    }

    [Test]
    public void PunctuationAndSpacingDoNotDistinguishTwoNames()
    {
        var placement = Resolve("gtr combo", out _);

        Assert.Multiple(() =>
        {
            Assert.That(placement.Category, Is.EqualTo("Amplifiers"));
            Assert.That(placement.Group, Is.EqualTo("GTR-COMBO"),
                "the label written is the configuration's, punctuation included");
            Assert.That(placement.Certain, Is.True);
        });
    }

    [Test]
    public void AccentsDoNotDistinguishTwoNames()
    {
        // The workshop writes what it hears; the configuration writes what it decided. The same shelf
        // answers to both.
        var subs = new Dictionary<string, SubcategoryPlacement.CategoryValue>(StringComparer.Ordinal)
        {
            ["SILLA BATERIA"] = new("Hardware", "SILLA BATERIA", "SILLA BATERIA"),
        };
        var byLabel = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HARDWARE"] = "Hardware", ["OTHER"] = "Other",
        };
        var map = new SubcategoryPlacement.CategoryMap(subs, byLabel, "Other");

        var notes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var placement = SubcategoryPlacement.Resolve("SILLA BATERÍA", "BACKLINE", map, notes);

        Assert.Multiple(() =>
        {
            Assert.That(placement.Category, Is.EqualTo("Hardware"));
            Assert.That(placement.Group, Is.EqualTo("SILLA BATERIA"),
                "the label written is the configuration's, without the accent");
            Assert.That(placement.Certain, Is.True);
        });
    }

    [Test]
    public void AnAliasPointsAtALabelTheConfigurationHas()
    {
        // TECALDO is the file's typo for TECLADO; the alias has to name the configured label, not an
        // intermediate spelling that Constants does not contain.
        var notes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var placement = SubcategoryPlacement.Resolve("TECALDO SENCILLA", "OTROS", Map(), notes);

        Assert.Multiple(() =>
        {
            Assert.That(placement.Category, Is.EqualTo("Hardware"));
            Assert.That(placement.Group, Is.EqualTo("BASE TECLADO SENCILLA"));
            Assert.That(placement.Certain, Is.True, "a known misspelling is corrected, not guessed");
            Assert.That(notes, Has.Some.Contains("BASE TECLADO SENCILLA"));
        });
    }

    [Test]
    public void ANameTheFileShortensIsCompletedAndSaysSo()
    {
        var placement = Resolve("CONGA DOBLE", out var notes);

        Assert.Multiple(() =>
        {
            Assert.That(placement.Category, Is.EqualTo("Hardware"));
            Assert.That(placement.Group, Is.EqualTo("BASE CONGA DOBLE"),
                "the shortest configured name that still contains the row, not CONGA");
            Assert.That(placement.Certain, Is.False, "a rule placed it, so it is worth a look");
            Assert.That(placement.Note, Does.Contain("BASE CONGA DOBLE"));
            Assert.That(notes, Has.Some.Contains("BASE CONGA DOBLE"));
        });
    }

    [Test]
    public void TheShortestNameThatContainsTheRowWins()
    {
        // "GUITARRA SENCILLA" fits the plain model and the BAJA one; the shorter is the one the file
        // meant, and the note says which it chose.
        var placement = Resolve("GUITARRA SENCILLA", out _);

        Assert.Multiple(() =>
        {
            Assert.That(placement.Group, Is.EqualTo("BASE GUITARRA SENCILLA"));
            Assert.That(placement.Certain, Is.False);
        });
    }

    [Test]
    public void ATieBetweenEquallyShortCandidatesKeepsTheFilesOwnWords()
    {
        // Two candidates, same category, same length: there is no way to tell which one the file
        // meant, so the group is not guessed — it keeps the words the file wrote.
        var subs = new Dictionary<string, SubcategoryPlacement.CategoryValue>(StringComparer.Ordinal)
        {
            ["BASE DJEMBE"] = new("Hardware", "BASE DJEMBE", "BASE DJEMBE"),
            ["BASE TIMBAL"] = new("Hardware", "BASE TIMBAL", "BASE TIMBAL"),
            ["BASE CONGA DOBLE"] = new("Hardware", "BASE CONGA DOBLE", "BASE CONGA DOBLE"),
        };
        var byLabel = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HARDWARE"] = "Hardware", ["OTHER"] = "Other",
        };
        var map = new SubcategoryPlacement.CategoryMap(subs, byLabel, "Other");

        var notes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var placement = SubcategoryPlacement.Resolve("BASE", "BACKLINE", map, notes);

        Assert.Multiple(() =>
        {
            Assert.That(placement.Category, Is.EqualTo("Hardware"),
                "every candidate lives under Hardware, so the category is not in doubt");
            Assert.That(placement.Group, Is.EqualTo("BASE"), "the label is not guessed");
            Assert.That(placement.Certain, Is.False);
            Assert.That(placement.Note, Does.Contain("varias subcategorías"));
        });
    }

    [Test]
    public void ASingularFindsItsPlural()
    {
        var placement = Resolve("MICROFONO", out _);

        Assert.Multiple(() =>
        {
            Assert.That(placement.Category, Is.EqualTo("Sonido"));
            Assert.That(placement.Group, Is.EqualTo("MICROFONOS"));
            Assert.That(placement.Certain, Is.False);
        });
    }

    [Test]
    public void ANameTheFileExtendsFallsBackToTheShorterOne()
    {
        var placement = Resolve("UPS 750 AMP", out _);

        Assert.Multiple(() =>
        {
            Assert.That(placement.Category, Is.EqualTo("Other"));
            Assert.That(placement.Group, Is.EqualTo("UPS"));
        });
    }

    [Test]
    public void AValueThatIsItselfACategoryHangsOffTheRoot()
    {
        var placement = Resolve("CASES", out _);

        Assert.Multiple(() =>
        {
            Assert.That(placement.Category, Is.EqualTo("Cases"));
            Assert.That(placement.Group, Is.EqualTo("Cases"), "the record hangs off the category itself");
            Assert.That(placement.Note, Does.Contain("es una categoría"));
        });
    }

    [Test]
    public void AnExactMatchWinsOverACloserLookingOne()
    {
        // HIHAT exists under Cymbals; BASE HIHAT also exists under Hardware. The exact name wins.
        var placement = Resolve("HIHAT", out _);
        Assert.That(placement.Category, Is.EqualTo("Cymbals"));

        var baseHihat = Resolve("BASE HIHAT", out _);
        Assert.That(baseHihat.Category, Is.EqualTo("Hardware"));
    }

    [Test]
    public void SomethingTheConfigurationDoesNotKnowLandsInTheCatchAll_AndSaysWhy()
    {
        var placement = Resolve("INVENTO NUEVO", out _);

        Assert.Multiple(() =>
        {
            Assert.That(placement.Category, Is.EqualTo("Other"));
            Assert.That(placement.Group, Is.EqualTo("INVENTO NUEVO"), "named as it came, not renamed");
            Assert.That(placement.Certain, Is.False);
            Assert.That(placement.Note, Does.Contain("no está en la configuración"));
        });
    }

    [Test]
    public void ARowWithoutASubcategoryIsGroupedByItsSheet()
    {
        var placement = Resolve(null, out _);

        Assert.Multiple(() =>
        {
            Assert.That(placement.Category, Is.EqualTo("Other"));
            Assert.That(placement.Group, Is.EqualTo("BACKLINE"));
            Assert.That(placement.Note, Does.Contain("BACKLINE"));
        });
    }

    [Test]
    public void ACandidateSpanningTwoCategoriesIsNotGuessed()
    {
        // The same word under two categories means the parent is not knowable from the name alone.
        var subs = new Dictionary<string, SubcategoryPlacement.CategoryValue>(StringComparer.Ordinal)
        {
            ["CONGA"] = new("Percusion", "CONGA", "CONGA"),
            ["BASE CONGA DOBLE"] = new("Hardware", "BASE CONGA DOBLE", "BASE CONGA DOBLE"),
        };
        var byLabel = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["HARDWARE"] = "Hardware", ["PERCUSION"] = "Percusion", ["OTHER"] = "Other",
        };
        var map = new SubcategoryPlacement.CategoryMap(subs, byLabel, "Other");

        var notes = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var placement = SubcategoryPlacement.Resolve("CONGA DOBLE", "BACKLINE", map, notes);

        Assert.Multiple(() =>
        {
            Assert.That(placement.Category, Is.EqualTo("Hardware"),
                "CONGA is a truncation of the row, BASE CONGA DOBLE is the row completed: completes win");
            Assert.That(placement.Group, Is.EqualTo("BASE CONGA DOBLE"));
        });
    }
}
