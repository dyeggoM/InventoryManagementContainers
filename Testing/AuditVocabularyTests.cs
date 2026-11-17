using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using IMCAPI.Services;
using Microsoft.EntityFrameworkCore;

namespace Testing;

/// <summary>
/// The audit trail's vocabulary versus the reader's: the two used to be separate lists and had
/// already drifted, so <c>GET /api/audit?action=ItemImport</c> answered 400 while the importer was
/// writing that very name. These tests hold the pair together from three sides — the reader accepts
/// every declared name, the declaration holds every constant, and no writer passes a literal that the
/// declaration does not carry — so a new action cannot arrive half-wired.
///
/// The last one has to read the sources: a call-site literal is not in the compiled assembly. It is
/// the cheap equivalent of reflecting over "the strings the interceptor writes", and it covers the
/// writers in other people's files too.
/// </summary>
[NonParallelizable]
[TestFixture]
public sealed class AuditVocabularyTests : ApiTestBase
{
    /// <summary>One case per declared action, named so a failure says which name was refused.</summary>
    private static IEnumerable<TestCaseData> DeclaredActions() =>
        AuditActions.All.Select(action => new TestCaseData(action)
            .SetName($"AuditFilter_Accepts_{action}"));

    /// <summary>A writer naming its action with a literal: <c>_audit.WriteAsync("ItemImport", ...)</c>.</summary>
    private static readonly Regex WriteCall = new(
        @"(?<![\w])_?audit\w*\s*\??\.\s*WriteAsync\(\s*""(?<action>[^""]+)""", RegexOptions.IgnoreCase);

    /// <summary>A row whose action is set by hand: <c>Action = "Restore"</c>.</summary>
    private static readonly Regex ActionAssignment = new(@"\bAction\s*=\s*""(?<action>[^""]+)""");

    /// <summary>
    /// The finding, closed: every name the API writes is a filter the superadmin's reader accepts —
    /// 200 and a total, zero included, instead of "action is not a recognized audit action".
    /// </summary>
    [TestCaseSource(nameof(DeclaredActions))]
    public async Task AuditFilterAcceptsEveryDeclaredAction(string action)
    {
        var response = await Client.GetAsync($"/api/audit?action={Uri.EscapeDataString(action)}");
        var body = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"GET /api/audit?action={action}: {body}");

        var payload = JsonDocument.Parse(body).RootElement;
        Assert.Multiple(() =>
        {
            Assert.That(payload.TryGetProperty("totalCount", out var total), Is.True, "page 1 must answer a total");
            Assert.That(total.ValueKind, Is.EqualTo(JsonValueKind.Number));
            Assert.That(total.GetInt32(), Is.GreaterThanOrEqualTo(0));
            Assert.That(payload.GetProperty("items").ValueKind, Is.EqualTo(JsonValueKind.Array));
        });
    }

    /// <summary>
    /// The filter still ignores case the way the old hand-written list did: a client typing
    /// <c>itemimport</c> gets the rows instead of a 400, and what reaches the database is the spelling
    /// the writers used.
    /// </summary>
    [Test]
    public async Task AuditFilterIsCaseInsensitive()
    {
        var response = await Client.GetAsync("/api/audit?action=itemimport");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.That(payload.TryGetProperty("totalCount", out _), Is.True);
    }

    /// <summary>The other half of a vocabulary: a name nobody writes stays a 400, with the reason.</summary>
    [Test]
    public async Task AuditFilterRefusesAnActionOutsideTheVocabulary()
    {
        var response = await Client.GetAsync("/api/audit?action=NotAnAuditAction");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), body);
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
            Assert.That(body, Does.Contain("action is not a recognized audit action"));
        });
    }

    /// <summary>
    /// The declaration and its constants cannot disagree: a constant added and forgotten in
    /// <see cref="AuditActions.All"/> fails here instead of becoming a name the reader refuses, and a
    /// member of All without a constant fails too. This is the test the old list did not have.
    /// </summary>
    [Test]
    public void TheVocabularyHoldsEveryDeclaredConstant()
    {
        var declared = typeof(AuditActions)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true, IsInitOnly: false } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That(declared, Is.Not.Empty);
            Assert.That(AuditActions.All, Is.Unique, "the vocabulary repeats a name");
            Assert.That(AuditActions.All, Is.EquivalentTo(declared),
                "every public const string in AuditActions belongs in All, and All holds nothing else");
        });
    }

    /// <summary>
    /// What the interceptor names a row: every state and flag combination it can reach produces a
    /// declared name, and the states it never reaches still refuse instead of inventing one.
    /// </summary>
    [Test]
    public void TheInterceptorNamesEveryWriteWithTheVocabulary()
    {
        var reachable = new[] { EntityState.Added, EntityState.Modified, EntityState.Deleted };
        var flags = new[] { false, true };
        var produced = new List<string>();
        foreach (var state in reachable)
        foreach (var isRole in flags)
        foreach (var isSoftDelete in flags)
        foreach (var isRestore in flags)
            produced.Add(AuditActions.ForChange(state, isRoleChange: isRole, isSoftDelete: isSoftDelete,
                isRestore: isRestore));

        Assert.Multiple(() =>
        {
            Assert.That(produced.Except(AuditActions.All),
                Is.Empty, "the interceptor would name a row something the reader refuses");
            Assert.That(produced, Does.Contain(AuditActions.Create).And.Contain(AuditActions.Update)
                .And.Contain(AuditActions.Delete).And.Contain(AuditActions.SoftDelete)
                .And.Contain(AuditActions.Restore).And.Contain(AuditActions.RoleChange));
        });

        foreach (var state in new[] { EntityState.Detached, EntityState.Unchanged })
            Assert.Throws<InvalidOperationException>(() => AuditActions.ForChange(state, false, false, false),
                $"an entity state the interceptor never writes must refuse, not name ({state})");
    }

    /// <summary>
    /// The drift that produced the finding, caught where it would happen: a writer — the interceptor,
    /// a controller, a service, including the ones this round does not touch — naming an action with a
    /// string that the vocabulary does not carry.
    /// </summary>
    [Test]
    public void EveryActionLiteralInTheApiIsInTheVocabulary()
    {
        var root = ApiSourceRoot();
        var written = new SortedDictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                continue;
            var lines = File.ReadAllLines(file);
            for (var index = 0; index < lines.Length; index++)
            {
                foreach (var regex in new[] { WriteCall, ActionAssignment })
                foreach (Match match in regex.Matches(lines[index]))
                {
                    var action = match.Groups["action"].Value;
                    if (!written.TryGetValue(action, out var sites)) written[action] = sites = [];
                    sites.Add($"{Path.GetFileName(file)}:{index + 1}");
                }
            }
        }

        var unknown = written.Keys.Where(action => AuditActions.Canonical(action) != action).ToList();
        Assert.Multiple(() =>
        {
            // A scan that found nothing would pass this test without checking anything: the API does
            // write literals today (AuthController and ItemImporter, as this round left them).
            Assert.That(written, Is.Not.Empty, $"no writer literals found under {root}");
            Assert.That(unknown, Is.Empty,
                "these audit actions are written but not declared in AuditActions: "
                + string.Join("; ", unknown.Select(action => $"{action} at {string.Join(", ", written[action])}")));
        });
    }

    /// <summary>
    /// Where the API's sources are. The test binary sits under <c>Testing/bin/&lt;config&gt;/&lt;tfm&gt;</c>,
    /// so the project file is a few directories up; failing loud here is better than a scan that reads
    /// nothing and passes.
    /// </summary>
    private static string ApiSourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "IMCAPI");
            if (File.Exists(Path.Combine(candidate, "IMCAPI.csproj"))) return candidate;
        }
        Assert.Fail($"could not find IMCAPI/IMCAPI.csproj above {AppContext.BaseDirectory}");
        return "";
    }
}
