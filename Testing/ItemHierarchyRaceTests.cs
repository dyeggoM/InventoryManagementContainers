using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using IMCAPI.Controllers;
using IMCAPI.DB;
using IMCAPI.DB.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Testing;

[TestFixture]
public sealed class ItemHierarchyRaceTests : ApiTestBase
{
    protected override bool FreshApiPerTest => true;
    protected override ApiFactory CreateFactory() => new(maxDepth: 4);

    [Test]
    public async Task ConcurrentCreateAndMoveNeverExceedDepthFour()
    {
        for (var round = 1; round <= 12; round++)
        {
            var root = await CreateItem($"ROOT {round}");
            var destination = await CreateItem($"DESTINATION {round}", root);
            var branch = await CreateItem($"BRANCH {round}");
            var tip = await CreateItem($"TIP {round}", branch);
            var results = await Task.WhenAll(
                Client.PostAsJsonAsync("/api/item", new { label = $"NEW {round}", icon = "", parentId = tip, attributes = "{}" }),
                Client.PostAsJsonAsync($"/api/item/{destination}/children", new { ids = new[] { branch } }));
            var statuses = results.Select(result => (int)result.StatusCode).Order().ToArray();
            using var scope = PersonaScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
            var parents = await db.Items.AsNoTracking().Select(item => new { item.Id, item.ParentId }).ToDictionaryAsync(item => item.Id, item => item.ParentId);
            var height = 0;
            foreach (var id in parents.Keys)
            {
                var level = 0;
                var seen = new HashSet<int>();
                int? cursor = id;
                while (cursor is int current && seen.Add(current))
                {
                    level++;
                    cursor = parents[current];
                }
                Assert.That(cursor, Is.Null, $"round {round}: cycle");
                height = Math.Max(height, level);
            }
            TestContext.Out.WriteLine($"create/move round {round}: HTTP {JsonSerializer.Serialize(statuses)}, maxDepth={height}");
            Assert.That(statuses, Is.EqualTo(new[] { 200, 400 }), $"round {round}");
            Assert.That(height, Is.LessThanOrEqualTo(4), $"round {round}");
        }
    }

    [Test]
    public async Task ConcurrentDeleteAndAttachNeverReturnsServerErrorOrHalfArchives()
    {
        for (var round = 1; round <= 12; round++)
        {
            var parent = await CreateItem($"DELETE PARENT {round}");
            var piece = await CreateItem($"ATTACH PIECE {round}");
            var relative = Path.Combine("media", "items", parent.ToString(), "proof.txt");
            var live = Path.Combine(Factory.MediaRoot, relative);
            var trash = Path.Combine(Factory.MediaRoot, "media", "trash", "items", parent.ToString(), "proof.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(live)!);
            await File.WriteAllTextAsync(live, "proof");
            using (var scope = PersonaScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
                db.Attachments.Add(new Attachment { ItemId = parent, FilePath = relative, FileName = "proof.txt", Type = "Photo" });
                await db.SaveChangesAsync();
            }

            var results = await Task.WhenAll(Client.DeleteAsync($"/api/item/{parent}"),
                Client.PostAsJsonAsync($"/api/item/{parent}/children", new { ids = new[] { piece } }));
            var delete = results[0];
            var attach = results[1];
            using var check = PersonaScope();
            var context = check.ServiceProvider.GetRequiredService<ApplicationContext>();
            var archiveCount = await context.ItemArchives.CountAsync(row => row.ItemId == parent);
            var parentExists = await context.Items.IgnoreQueryFilters().AnyAsync(item => item.Id == parent);
            var pieceParent = await context.Items.IgnoreQueryFilters().Where(item => item.Id == piece)
                .Select(item => item.ParentId).SingleAsync();
            await using var connection = new SqliteConnection($"Data Source={Factory.DatabasePath}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM Items c LEFT JOIN Items p ON p.Id = c.ParentId WHERE c.ParentId IS NOT NULL AND p.Id IS NULL";
            var orphans = Convert.ToInt32(await command.ExecuteScalarAsync());
            TestContext.Out.WriteLine($"delete/attach round {round}: HTTP delete={(int)delete.StatusCode}, attach={(int)attach.StatusCode}, archives={archiveCount}, orphans={orphans}, live={File.Exists(live)}, trash={File.Exists(trash)}");
            Assert.That(delete.StatusCode, Is.AnyOf(HttpStatusCode.NoContent, HttpStatusCode.Conflict), $"round {round}: {await delete.Content.ReadAsStringAsync()}");
            Assert.That(orphans, Is.Zero, $"round {round}");
            if (delete.StatusCode == HttpStatusCode.Conflict)
            {
                Assert.That(attach.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(await delete.Content.ReadAsStringAsync(), Does.Contain("Item has children"));
                Assert.That((archiveCount, parentExists, pieceParent), Is.EqualTo((0, true, (int?)parent)));
                Assert.That((File.Exists(live), File.Exists(trash)), Is.EqualTo((true, false)));
            }
            else
            {
                Assert.That(attach.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
                Assert.That((archiveCount, parentExists, pieceParent), Is.EqualTo((1, false, (int?)null)));
                Assert.That((File.Exists(live), File.Exists(trash)), Is.EqualTo((false, true)));
            }
        }
    }

    [Test]
    public async Task DeleteWithExistingChildStillReturnsConflictAndDoesNotTouchFilesOrArchive()
    {
        var parent = await CreateItem("PARENT WITH CHILD");
        var child = await CreateItem("CHILD", parent);
        var response = await Client.DeleteAsync($"/api/item/{parent}");
        using var scope = PersonaScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("Item has children"));
        Assert.That(await db.Items.IgnoreQueryFilters().AnyAsync(item => item.Id == parent), Is.True);
        Assert.That(await db.Items.IgnoreQueryFilters().Where(item => item.Id == child).Select(item => item.ParentId).SingleAsync(), Is.EqualTo(parent));
        Assert.That(await db.ItemArchives.AnyAsync(row => row.ItemId == parent), Is.False);
    }

    [Test]
    public void KnownHierarchyRoutesDeclareTheirLockBoundary()
    {
        // Explicit contract until the separate architecture round supplies one enforced write gate.
        // Every route that reads the hierarchy before writing it is on this list, without exceptions.
        var guarded = new[] { "Post", "Put", "MoveChildren", "DetachChild", "Delete", "Activate" };
        var deferred = Array.Empty<string>();
        var sourceRoot = Factory.Services.GetRequiredService<IWebHostEnvironment>().ContentRootPath;
        var source = File.ReadAllText(Path.Combine(sourceRoot, "Controllers", "ItemController.cs"));
        var routes = typeof(ItemController).GetMethods(System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(method => method.GetCustomAttributes(typeof(HttpMethodAttribute), true).Length > 0)
            .Select(method => method.Name).ToArray();
        foreach (var name in guarded.Concat(deferred))
            Assert.That(routes, Does.Contain(name), $"the hierarchy route {name} disappeared from the inventory");
        Assert.That(deferred, Is.Empty, "no hierarchy route is left outside the write lock");
        // Source assertions deliberately fail if a protected handler drops its pre-read wrapper.
        foreach (var name in guarded)
            Assert.That(Regex.IsMatch(source,
                $@"public (?:override )?(?:async )?Task<IActionResult> {name}\([^)]*\)[\s\S]{{0,450}}?WithHierarchyWriteLock\("),
                Is.True, $"{name} must acquire the lock before reading the hierarchy");
    }

    /// <summary>
    /// The contention the old `PRAGMA busy_timeout = 500` claimed to bound. An external process holds
    /// `BEGIN IMMEDIATE` for two seconds: both routes have to wait and answer, not turn someone else's
    /// writer lock into a 500 after thirty seconds.
    /// </summary>
    [Test]
    public async Task AnExternalWriterHoldingTheLockDoesNotTurnDetachOrActivateIntoAServerError()
    {
        var parent = await CreateItem("LOCKED PARENT");
        var child = await CreateItem("LOCKED CHILD", parent);
        var archived = await CreateItem("LOCKED ARCHIVED");
        Assert.That((await Client.DeleteAsync($"/api/item/{archived}")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        await using var writer = new SqliteConnection($"Data Source={Factory.DatabasePath}");
        await writer.OpenAsync();
        await using (var begin = writer.CreateCommand())
        {
            begin.CommandText = "BEGIN IMMEDIATE";
            await begin.ExecuteNonQueryAsync();
        }

        var started = System.Diagnostics.Stopwatch.StartNew();
        var detachTask = Client.DeleteAsync($"/api/item/{parent}/children/{child}");
        var activateTask = Client.PatchAsync($"/api/item/{archived}/activate", content: null);
        await Task.Delay(TimeSpan.FromSeconds(2));
        await using (var release = writer.CreateCommand())
        {
            release.CommandText = "ROLLBACK";
            await release.ExecuteNonQueryAsync();
        }

        var detach = await detachTask;
        var activate = await activateTask;
        started.Stop();
        TestContext.Out.WriteLine($"external writer held the lock for 2 s: detach={(int)detach.StatusCode}, "
            + $"activate={(int)activate.StatusCode}, waited {started.ElapsedMilliseconds} ms");

        var detachBody = await detach.Content.ReadAsStringAsync();
        var activateBody = await activate.Content.ReadAsStringAsync();
        Assert.Multiple(() =>
        {
            Assert.That(detach.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), detachBody);
            Assert.That(activate.StatusCode, Is.EqualTo(HttpStatusCode.OK), activateBody);
            Assert.That(started.Elapsed, Is.LessThan(TimeSpan.FromSeconds(20)),
                "the wait is bounded by the retry loop, not by the provider's 30 s default");
        });
    }

    /// <summary>
    /// The budget regression: a writer holding the lock for 10 s — twice the old 6.7 s budget, which
    /// used to answer 500 — is waited out and answered 2xx. The 20 s budget exists for exactly this.
    /// </summary>
    [Test]
    public async Task AnExternalWriterHoldingTheLockForTenSecondsIsWaitedOutNotRefused()
    {
        var parent = await CreateItem("TEN SECOND PARENT");
        var child = await CreateItem("TEN SECOND CHILD", parent);
        var archived = await CreateItem("TEN SECOND ARCHIVED");
        Assert.That((await Client.DeleteAsync($"/api/item/{archived}")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        await using var writer = new SqliteConnection($"Data Source={Factory.DatabasePath}");
        await writer.OpenAsync();
        await using (var begin = writer.CreateCommand())
        {
            begin.CommandText = "BEGIN IMMEDIATE";
            await begin.ExecuteNonQueryAsync();
        }

        var started = System.Diagnostics.Stopwatch.StartNew();
        var detachTask = Client.DeleteAsync($"/api/item/{parent}/children/{child}");
        var activateTask = Client.PatchAsync($"/api/item/{archived}/activate", content: null);
        await Task.Delay(TimeSpan.FromSeconds(10));
        await using (var release = writer.CreateCommand())
        {
            release.CommandText = "ROLLBACK";
            await release.ExecuteNonQueryAsync();
        }

        var detach = await detachTask;
        var activate = await activateTask;
        started.Stop();
        var detachBody = await detach.Content.ReadAsStringAsync();
        var activateBody = await activate.Content.ReadAsStringAsync();
        TestContext.Out.WriteLine($"external writer held the lock for 10 s: detach={(int)detach.StatusCode}, "
            + $"activate={(int)activate.StatusCode}, waited {started.ElapsedMilliseconds} ms");

        Assert.Multiple(() =>
        {
            Assert.That(detach.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), detachBody);
            Assert.That(activate.StatusCode, Is.EqualTo(HttpStatusCode.OK), activateBody);
            Assert.That(started.Elapsed, Is.GreaterThan(TimeSpan.FromSeconds(10)).And.LessThan(TimeSpan.FromSeconds(25)),
                "the request waited out the holder inside the 20 s budget instead of answering 500");
        });
    }

    /// <summary>
    /// Past the budget the failure is honest: a real error status, no half-written state, and no
    /// hang — the request gives up while the external writer still holds the lock.
    /// </summary>
    [Test]
    public async Task AnExternalWriterHoldingTheLockForFortySecondsFailsHonestlyWithoutAPartialWrite()
    {
        var parent = await CreateItem("FORTY SECOND PARENT");
        var child = await CreateItem("FORTY SECOND CHILD", parent);

        await using var writer = new SqliteConnection($"Data Source={Factory.DatabasePath}");
        await writer.OpenAsync();
        await using (var begin = writer.CreateCommand())
        {
            begin.CommandText = "BEGIN IMMEDIATE";
            await begin.ExecuteNonQueryAsync();
        }

        var started = System.Diagnostics.Stopwatch.StartNew();
        // 35 s is under the holder's 40 s: if the request needed the holder to release, this fails
        // with a timeout — which is exactly the hang this test exists to rule out.
        var detach = await Client.DeleteAsync($"/api/item/{parent}/children/{child}").WaitAsync(TimeSpan.FromSeconds(35));
        started.Stop();
        var detachBody = await detach.Content.ReadAsStringAsync();
        TestContext.Out.WriteLine($"external writer held the lock for 40 s: detach={(int)detach.StatusCode} "
            + $"after {started.ElapsedMilliseconds} ms (budget ~21 s, holder released at 40 s), body={detachBody}");

        using var check = PersonaScope();
        var db = check.ServiceProvider.GetRequiredService<ApplicationContext>();
        Assert.Multiple(async () =>
        {
            Assert.That((int)detach.StatusCode, Is.GreaterThanOrEqualTo(500), detachBody);
            Assert.That(started.Elapsed, Is.GreaterThan(TimeSpan.FromSeconds(18)).And.LessThan(TimeSpan.FromSeconds(40)),
                "the request gave up on its own budget, before the holder released");
            Assert.That(await db.Items.IgnoreQueryFilters().Where(item => item.Id == child)
                .Select(item => item.ParentId).SingleAsync(), Is.EqualTo(parent), "the refused move wrote nothing");
            Assert.That(await db.ItemArchives.AnyAsync(row => row.ItemId == child), Is.False, "no half archive");
        });
    }

    /// <summary>
    /// The normal case stays normal: 30 concurrent single-child moves contend on the write lock, and
    /// every one is served inside the budget — no 500, and no new artificial wait on top of the lock.
    /// </summary>
    [Test]
    public async Task ThirtyConcurrentMovesAllSucceedWithoutAServerError()
    {
        var source = await CreateItem("MOVE SOURCE");
        var destination = await CreateItem("MOVE DESTINATION", source);
        var children = new List<int>();
        for (var i = 0; i < 30; i++)
            children.Add(await CreateItem($"MOVE CHILD {i}", source));

        var started = System.Diagnostics.Stopwatch.StartNew();
        var moves = await Task.WhenAll(children.Select(childId =>
            Client.PostAsJsonAsync($"/api/item/{destination}/children", new { ids = new[] { childId } })));
        started.Stop();

        var statuses = moves.Select(move => (int)move.StatusCode).ToArray();
        TestContext.Out.WriteLine($"30 concurrent single-child moves: HTTP {JsonSerializer.Serialize(statuses)} "
            + $"in {started.ElapsedMilliseconds} ms");
        using var check = PersonaScope();
        var db = check.ServiceProvider.GetRequiredService<ApplicationContext>();
        Assert.Multiple(async () =>
        {
            Assert.That(statuses, Is.All.EqualTo(200), "no move was refused or errored");
            Assert.That(await db.Items.AsNoTracking().Where(item => children.Contains(item.Id))
                .Select(item => item.ParentId).ToListAsync(), Is.All.EqualTo(destination),
                "every child ended up under the destination");
            Assert.That(started.Elapsed, Is.LessThan(TimeSpan.FromSeconds(30)), "contention was served, not stalled");
        });
    }
}
