using System.Net;
using System.Text.Json;
using IMCAPI.DB;
using IMCAPI.DB.Entities;
using Microsoft.Extensions.DependencyInjection;

namespace Testing;

/// <summary>
/// A deep tree has to arrive whole. It used to arrive as `200` with half a document, because the
/// serializer ran out of depth after the response had already started and no status could repair it.
/// This fixture stores a legal 200-level chain (the bound the routes are told to enforce) and asks
/// for it.
/// </summary>
[TestFixture]
public sealed class ItemPayloadDepthTests : ApiTestBase
{
    private const int Levels = 200;

    protected override bool FreshApiPerTest => true;

    protected override ApiFactory CreateFactory() => new(maxDepth: Levels);

    /// <summary>One chain, written in two passes: the parent of level n is level n-1.</summary>
    private async Task SeedChain()
    {
        using var scope = PersonaScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var rows = Enumerable.Range(1, Levels)
            .Select(level => new Item { Label = $"depth-{level}", Icon = "", Attributes = "{}" })
            .ToList();
        db.Items.AddRange(rows);
        await db.SaveChangesAsync();
        for (var index = 1; index < rows.Count; index++) rows[index].ParentId = rows[index - 1].Id;
        await db.SaveChangesAsync();
    }

    [Test]
    public async Task ADeepButLegalTreeComesBackAsOneCompleteJsonDocument()
    {
        await SeedChain();

        var response = await Client.GetAsync("/api/item");
        var body = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
        Assert.That(response.Content.Headers.ContentLength, Is.Not.Null,
            "a buffered answer declares its length; a truncated one arrives chunked");
        Assert.That(body, Does.Contain($"depth-{Levels}"), "the deepest level is in the body at all");

        using var document = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 4096 });
        var node = document.RootElement.GetProperty("tree").EnumerateArray()
            .Single(root => root.GetProperty("label").GetString() == "depth-1");
        var depth = 1;
        while (node.TryGetProperty("children", out var children) && children.GetArrayLength() > 0)
        {
            depth++;
            node = children[0];
        }

        TestContext.Out.WriteLine($"GET /api/item with a {Levels}-level chain: {(int)response.StatusCode}, "
            + $"{body.Length} chars, tree depth {depth}, content-length {response.Content.Headers.ContentLength}");
        Assert.That(depth, Is.EqualTo(Levels), "every level of the chain arrived");
    }
}

/// <summary>
/// The other half of the promise: a stored chain past the depth the routes enforce (corruption, or a
/// bound lowered after the fact) cannot be answered with, and the answer says so instead of stopping
/// in the middle of the body.
/// </summary>
[TestFixture]
public sealed class ItemPayloadDepthLimitTests : ApiTestBase
{
    private const int Levels = 200;

    protected override bool FreshApiPerTest => true;

    private async Task SeedChain()
    {
        using var scope = PersonaScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var rows = Enumerable.Range(1, Levels)
            .Select(level => new Item { Label = $"over-{level}", Icon = "", Attributes = "{}" })
            .ToList();
        db.Items.AddRange(rows);
        await db.SaveChangesAsync();
        for (var index = 1; index < rows.Count; index++) rows[index].ParentId = rows[index - 1].Id;
        await db.SaveChangesAsync();
    }

    [Test]
    public async Task ATreeDeeperThanTheSerializerAllowsIsAnHonestErrorNotHalfABody()
    {
        await SeedChain();

        var response = await Client.GetAsync("/api/item");
        // Reading the body is the assertion that it is whole: a truncated one throws here.
        var body = await response.Content.ReadAsStringAsync();
        TestContext.Out.WriteLine($"GET /api/item over a {Levels}-level chain, default depth bound: "
            + $"{(int)response.StatusCode} {body}");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(response.Content.Headers.ContentLength, Is.Not.Null, "not a chunked half document");
        using var document = JsonDocument.Parse(body);
        Assert.Multiple(() =>
        {
            Assert.That(document.RootElement.GetProperty("title").GetString(), Is.EqualTo("Item tree cannot be serialized"));
            Assert.That(document.RootElement.GetProperty("detail").GetString(), Does.Contain("MaxDepth"));
        });
    }
}
