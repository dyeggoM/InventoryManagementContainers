using System.Text.RegularExpressions;
using IMCAPI.DB;
using Microsoft.EntityFrameworkCore;

namespace IMCAPI.Services;

/// <summary>Loads configured categories and decides where each imported row belongs.</summary>
public sealed class SubcategoryPlacement
{
    /// <summary>
    /// The subcategory spellings the source file uses for the same thing. Kept small and explicit:
    /// a guess that silently merges two real categories costs more than a yellow row that says why.
    /// </summary>
    private static readonly Dictionary<string, string> SubcategoryAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GTAR COMBO"] = "GTR COMBO",
        ["GTR-COMBO"] = "GTR COMBO",
        // The file writes TECALDO for TECLADO, and the configuration's label is the full one: an alias
        // that points at a name Constants does not have resolves to nothing and lands in Other.
        ["TECALDO SENCILLA"] = "BASE TECLADO SENCILLA",
        ["MICROFONOS"] = "MICROFONO",
    };

    private readonly ApplicationContext _context;

    public SubcategoryPlacement(ApplicationContext context) => _context = context;

    /// <summary>Where a row would land: the category, the group under it, and why it landed there.</summary>
    internal sealed record Placement(string Category, string Group, string? Note, bool Certain);

    /// <summary>A configuration subcategory, with the category that owns it.</summary>
    internal sealed record CategoryValue(string Category, string Label, string Normalized);

    /// <summary>The configuration as the importer needs it: what exists, and where an orphan goes.</summary>
    internal sealed record CategoryMap(IReadOnlyDictionary<string, CategoryValue> Subs,
        IReadOnlyDictionary<string, string> CategoryByLabel, string Fallback);

    private static string Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var trimmed = value.Trim().Trim('\uFEFF');
        return Regex.Replace(trimmed, @"\s+", " ").ToUpperInvariant();
    }

    /// <summary>
    /// The key two names are compared by: case, spacing and punctuation do not distinguish them here,
    /// so `GTR-COMBO` and `GTR COMBO` are the same subcategory and `BASE  TECLADO` is `BASE TECLADO`.
    /// The label itself is never rewritten — only what the comparison looks at.
    /// </summary>
    /// <summary>
    /// The letters that carry a diacritic in the names this workshop writes. The API runs with
    /// `InvariantGlobalization`, where `string.Normalize` cannot fold anything — measured: the same
    /// method folds in the test project and silently returns its input here — so the fold is a table,
    /// not a runtime call. Add a pair when a name needs one.
    /// </summary>
    private static readonly Dictionary<char, char> Diacritics = new()
    {
        ['Á'] = 'A', ['À'] = 'A', ['Ä'] = 'A', ['Â'] = 'A',
        ['É'] = 'E', ['È'] = 'E', ['Ë'] = 'E', ['Ê'] = 'E',
        ['Í'] = 'I', ['Ì'] = 'I', ['Ï'] = 'I', ['Î'] = 'I',
        ['Ó'] = 'O', ['Ò'] = 'O', ['Ö'] = 'O', ['Ô'] = 'O',
        ['Ú'] = 'U', ['Ù'] = 'U', ['Ü'] = 'U', ['Û'] = 'U',
        ['Ñ'] = 'N', ['Ç'] = 'C', ['Ý'] = 'Y',
    };

    /// <summary>
    /// The key two names are compared by: case, spacing, punctuation and accents do not distinguish
    /// them here, so `GTR-COMBO` and `GTR COMBO` are the same subcategory, and so are `SILLA BATERÍA`
    /// and `SILLA BATERIA`. The label itself is never rewritten — only what the comparison looks at.
    /// </summary>
    private static string MatchKey(string? value)
    {
        var folded = Clean(value);
        var plain = new System.Text.StringBuilder(folded.Length);
        foreach (var ch in folded) plain.Append(Diacritics.TryGetValue(ch, out var bare) ? bare : ch);

        return Regex.Replace(plain.ToString()
                .Replace('-', ' ').Replace('_', ' ').Replace('.', ' ').Replace('/', ' '),
            @"\s+", " ").Trim();
    }

    /// <summary>
    /// Reads the configuration so the importer can put a row where the application would have put it:
    /// the categories (`Group = Category`) and, under each one, the subcategories the workshop
    /// actually uses. `Other` catches what the file has and the configuration does not.
    /// </summary>
    internal async Task<CategoryMap> LoadAsync(CancellationToken cancellationToken)
    {
        var rows = await _context.Constants.AsNoTracking()
            .Where(row => row.Group != null)
            .Select(row => new { row.Group, row.Label })
            .ToListAsync(cancellationToken);

        var categoryLabels = rows
            .Where(row => string.Equals(row.Group, "Category", StringComparison.OrdinalIgnoreCase))
            .Select(row => row.Label)
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var byLabel = categoryLabels.ToDictionary(MatchKey, label => label, StringComparer.Ordinal);

        // A subcategory row belongs to the category its Group names ("Hardware", "Sonido", …). The
        // Category rows themselves are not values: they are the level above.
        var subcategories = rows
            .Where(row => !string.Equals(row.Group, "Category", StringComparison.OrdinalIgnoreCase))
            .Where(row => byLabel.ContainsKey(MatchKey(row.Group)))
            .Select(row => new CategoryValue(byLabel[MatchKey(row.Group)], Clean(row.Label), MatchKey(row.Label)))
            .Where(value => value.Normalized.Length > 0)
            .ToList();

        var subs = new Dictionary<string, CategoryValue>(StringComparer.Ordinal);
        foreach (var value in subcategories) subs.TryAdd(value.Normalized, value);

        var fallback = categoryLabels.FirstOrDefault(label => MatchKey(label) == "OTHER")
            ?? categoryLabels.FirstOrDefault()
            ?? "Other";

        return new CategoryMap(subs, byLabel, fallback);
    }

    /// <summary>
    /// Decides where a row goes, in the order the evidence deserves: the configuration spells it that
    /// way, then the known misspellings, then the value is itself a category, then the file wrote a
    /// shortened or extended form of a name the configuration has. What is left has no home and says
    /// so instead of being guessed into one.
    /// </summary>
    internal static Placement Resolve(string? subcategory, string sheet, CategoryMap map,
        SortedSet<string> normalizations)
    {
        var input = MatchKey(subcategory);
        if (input.Length == 0)
        {
            // No subcategory in the row: the sheet it came from is the best hint the file gives.
            return new Placement(map.Fallback, Clean(sheet), $"La fila no trae subcategoría: se agrupa por su hoja «{Clean(sheet)}».", false);
        }

        if (map.Subs.TryGetValue(input, out var exact))
            return new Placement(exact.Category, exact.Label, null, true);

        if (SubcategoryAliases.TryGetValue(input, out var alias) && map.Subs.TryGetValue(MatchKey(alias), out var aliased))
        {
            normalizations.Add($"«{Clean(subcategory)}» → «{aliased.Label}»");
            return new Placement(aliased.Category, aliased.Label, $"Subcategoría corregida «{Clean(subcategory)}» → «{aliased.Label}».", true);
        }

        if (map.CategoryByLabel.TryGetValue(input, out var asCategory))
            return new Placement(asCategory, asCategory, $"«{Clean(subcategory)}» es una categoría: el elemento cuelga directamente de ella.", false);

        // The file writes a name the configuration has in a shorter or longer form: "CONGA DOBLE"
        // for BASE CONGA DOBLE, "MESA" for MESA PERCUSION, "MICROFONO" for MICROFONOS, "UPS 750 AMP"
        // for UPS. Whole words only, and the order is the confidence: a name the file *completes*
        // first, then the singular/plural of the same word, then a name the file *truncates* — so
        // "CONGA DOBLE" is not read as the shorter "CONGA".
        var completes = new List<CategoryValue>();
        var plurals = new List<CategoryValue>();
        var truncates = new List<CategoryValue>();
        foreach (var candidate in map.Subs.Values)
        {
            if (candidate.Normalized == input) continue;
            if (candidate.Normalized.EndsWith(" " + input, StringComparison.Ordinal)
                || candidate.Normalized.StartsWith(input + " ", StringComparison.Ordinal)) completes.Add(candidate);
            else if (candidate.Normalized == input + "S" || input == candidate.Normalized + "S") plurals.Add(candidate);
            else if (input.EndsWith(" " + candidate.Normalized, StringComparison.Ordinal)
                || input.StartsWith(candidate.Normalized + " ", StringComparison.Ordinal)) truncates.Add(candidate);
        }

        var pool = completes.Count > 0 ? completes : plurals.Count > 0 ? plurals : truncates;
        if (pool.Count > 0 && pool.Select(candidate => candidate.Category).Distinct(StringComparer.Ordinal).Count() == 1)
        {
            var category = pool[0].Category;
            // The shortest name that still contains the row is the one the file meant; when two are
            // equally short there is no way to tell, so the group keeps the file's own words.
            var words = pool.Min(candidate => candidate.Normalized.Split(' ').Length);
            var best = pool.Where(candidate => candidate.Normalized.Split(' ').Length == words).ToList();
            if (best.Count == 1)
            {
                normalizations.Add($"«{Clean(subcategory)}» → «{best[0].Label}»");
                return new Placement(category, best[0].Label, $"«{Clean(subcategory)}» se coloca en «{best[0].Label}».", false);
            }

            return new Placement(category, Clean(subcategory),
                $"«{Clean(subcategory)}» encaja con varias subcategorías de «{category}»: se agrupa con su propio nombre.", false);
        }

        return new Placement(map.Fallback, Clean(subcategory),
            $"«{Clean(subcategory)}» no está en la configuración: se coloca en «{map.Fallback}» para revisarla.", false);
    }
}
