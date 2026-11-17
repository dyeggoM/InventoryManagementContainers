using System.Data;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using IMCAPI.DB;
using IMCAPI.DB.Entities;
using IMCAPI.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Testing;

/// <summary>
/// The three hierarchy answers that have to agree with the rest: a restore under a chain that already
/// loops, a delete a foreign child blocks, and the wait one writer imposes on another.
/// </summary>
[TestFixture]
public sealed class ItemHierarchyHardeningTests : ApiTestBase
{
    protected override bool FreshApiPerTest => true;

    /// <summary>
    /// Post and Put refuse a parent chain that already contains a cycle; a restore used to skip that
    /// check and put the record back under a node the tree cannot paint.
    /// </summary>
    [Test]
    public async Task Activate_UnderAnAlreadyLoopingParent_IsRefusedWithTheSameMessageAsPostAndPut()
    {
        var parent = await CreateItem("looping-parent");
        var archived = await CreateItem("looping-child", parent);
        Assert.That((await Client.DeleteAsync($"/api/item/{archived}")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        using (var scope = PersonaScope())
        {
            // Only corruption leaves a self-parented row behind, and that is the state the guard is for.
            var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
            var row = await db.Items.IgnoreQueryFilters().SingleAsync(item => item.Id == parent);
            row.ParentId = parent;
            await db.SaveChangesAsync();
        }

        var response = await Client.PatchAsync($"/api/item/{archived}/activate", content: null);
        var body = await response.Content.ReadAsStringAsync();
        TestContext.Out.WriteLine($"activate under a looping parent: {(int)response.StatusCode} {body}");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), body);
            Assert.That(body, Does.Contain("The parent hierarchy already contains a cycle."));
        });
        Assert.That((await Client.GetAsync($"/api/item/{archived}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
            "a refused restore writes nothing");
    }

    /// <summary>
    /// The pre-check counts this organization's children, because that is what the caller can see and
    /// move. The foreign-key failure that follows has no such filter, so the diagnosis must not either:
    /// a child row of another organization used to turn the honest 409 into a 500.
    /// </summary>
    [Test]
    public async Task Delete_WithAChildOfAnotherOrganization_AnswersConflictAndSaysHowMany()
    {
        var parent = await CreateItem("parent-of-a-foreign-child");
        using (var scope = Factory.Services.CreateScope())
        {
            // Written under the other organization, not smuggled in under this one: the interceptor
            // derives TenantId from the scope, which is exactly how only corruption produces this row.
            scope.ServiceProvider.GetRequiredService<TenantScope>().SelectSystemTenant(900002);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
            db.Items.Add(new Item { Label = "foreign-child", ParentId = parent, Attributes = "{}" });
            await db.SaveChangesAsync();
        }

        var response = await Client.DeleteAsync($"/api/item/{parent}");
        var body = await response.Content.ReadAsStringAsync();
        TestContext.Out.WriteLine($"delete with a child of another organization: {(int)response.StatusCode} {body}");

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), body);
            Assert.That(body, Does.Contain("This item has 1 child item(s), possibly from another organization"));
        });

        using var check = PersonaScope();
        var context = check.ServiceProvider.GetRequiredService<ApplicationContext>();
        Assert.That(await context.Items.IgnoreQueryFilters().AnyAsync(row => row.Id == parent), Is.True,
            "the delete was refused, not carried out");
    }

    /// <summary>
    /// The mechanism behind the retracted promise, measured. `PRAGMA busy_timeout = 500` is SQLite's
    /// busy handler, and a held writer lock is not what it decides about: it says 500 ms and a
    /// `BEGIN IMMEDIATE` still waits the whole hold, because Microsoft.Data.Sqlite retries the
    /// statement itself. The knob that bounds that retry is the connection's default timeout.
    /// </summary>
    [Test]
    public async Task TheBusyTimeoutPragmaDoesNotBoundTheWaitButTheConnectionTimeoutDoes()
    {
        TimeSpan waited;
        await using (var holder = await HoldWriteLockAsync(TimeSpan.FromSeconds(2)))
        {
            var started = Stopwatch.StartNew();
            await using var reader = new SqliteConnection($"Data Source={Factory.DatabasePath}");
            await reader.OpenAsync();
            await using (var pragma = reader.CreateCommand())
            {
                pragma.CommandText = "PRAGMA busy_timeout = 500";
                await pragma.ExecuteNonQueryAsync();
            }

            await using var transaction = await reader.BeginTransactionAsync(IsolationLevel.Serializable);
            waited = started.Elapsed;
            await transaction.RollbackAsync();
        }

        TimeSpan bounded;
        int order;
        await using (var holder = await HoldWriteLockAsync(TimeSpan.FromSeconds(2)))
        {
            var started = Stopwatch.StartNew();
            await using var reader = new SqliteConnection($"Data Source={Factory.DatabasePath}") { DefaultTimeout = 1 };
            await reader.OpenAsync();

            var busy = Assert.ThrowsAsync<SqliteException>(async () =>
            {
                await using var transaction = await reader.BeginTransactionAsync(IsolationLevel.Serializable);
            });
            bounded = started.Elapsed;
            order = busy!.SqliteErrorCode;
        }

        TestContext.Out.WriteLine($"PRAGMA busy_timeout = 500 under a 2 s hold: BEGIN IMMEDIATE waited "
            + $"{waited.TotalMilliseconds:F0} ms; with DefaultTimeout = 1 s the same lock answers "
            + $"SQLITE_BUSY({order}) after {bounded.TotalMilliseconds:F0} ms");

        Assert.Multiple(() =>
        {
            Assert.That(waited, Is.GreaterThan(TimeSpan.FromMilliseconds(1500)),
                "the pragma named 500 ms and the wait was the writer's whole hold");
            Assert.That(order, Is.EqualTo(5), "SQLITE_BUSY");
            Assert.That(bounded, Is.LessThan(TimeSpan.FromMilliseconds(1500)),
                "the connection timeout is what bounds the provider's own retry");
        });
    }

    /// <summary>Another writer holds the database's only write lock for a while, then rolls back.</summary>
    private async Task<LockHolder> HoldWriteLockAsync(TimeSpan duration)
    {
        var connection = new SqliteConnection($"Data Source={Factory.DatabasePath}");
        await connection.OpenAsync();
        await using (var begin = connection.CreateCommand())
        {
            begin.CommandText = "BEGIN IMMEDIATE";
            await begin.ExecuteNonQueryAsync();
        }

        var release = Task.Run(async () =>
        {
            await Task.Delay(duration);
            await using var rollback = connection.CreateCommand();
            rollback.CommandText = "ROLLBACK";
            await rollback.ExecuteNonQueryAsync();
        });
        return new LockHolder(connection, release);
    }

    private sealed class LockHolder(SqliteConnection connection, Task release) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await release;
            await connection.DisposeAsync();
        }
    }
}
