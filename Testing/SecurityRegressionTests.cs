using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IMCAPI.DB;
using IMCAPI.DB.Entities;
using IMCAPI.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Testing;

/// <summary>
/// Regression tests for the findings the security matrix recorded against this codebase. Each one
/// names the behaviour that was wrong, so a later refactor that reintroduces it fails here instead of
/// in a review.
/// </summary>
[NonParallelizable]
[TestFixture]
public sealed class SecurityRegressionTests : ApiTestBase
{
    protected override bool SignsInAtStartup => false;

    [Test]
    public async Task Upload_ToAMissingRecord_IsRefusedBeforeAnythingIsWrittenToDisk()
    {
        using var client = await LoginAsSuperadmin();
        using var form = new MultipartFormDataContent
        {
            { new StringContent("999999"), "itemId" },
            { new StringContent("Photo"), "type" },
        };
        var bytes = new ByteArrayContent([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        bytes.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(bytes, "file", "photo.png");

        var response = await client.PostAsync("/api/attachment/upload", form);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), await response.Content.ReadAsStringAsync());

        var orphanDirectory = Path.Combine(Factory.MediaRoot, "items", "999999");
        Assert.That(Directory.Exists(orphanDirectory), Is.False,
            "the record was checked before the bytes were written, so no folder is left behind");
    }

    [Test]
    public async Task Upload_DoesNotKeepAClientSuppliedHostileExtension()
    {
        using var client = await LoginAsSuperadmin();
        var itemId = await CreateItem(client, "extension probe");

        using var form = new MultipartFormDataContent
        {
            { new StringContent(itemId.ToString()), "itemId" },
            { new StringContent("Document"), "type" },
        };
        var bytes = new ByteArrayContent(Encoding.UTF8.GetBytes("<html><script>alert(1)</script></html>"));
        form.Add(bytes, "file", "payload.html");

        var response = await client.PostAsync("/api/attachment/upload", form);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        var storedPath = payload.GetProperty("filePath").GetString()!;

        Assert.That(storedPath.EndsWith(".html", StringComparison.OrdinalIgnoreCase), Is.False,
            "an extension outside the allow-list is re-detected from the content");
        Assert.That(File.Exists(Path.Combine(Factory.MediaRoot, storedPath.Replace('/', Path.DirectorySeparatorChar))), Is.True);
    }

    [Test]
    public async Task Logs_RejectsASegmentThatIsNotADate()
    {
        using var client = await LoginAsSuperadmin();
        foreach (var attempt in new[] { "not-a-date", "2026-13-45", "..%2F..%2Fappsettings", "2026-11-11%20" })
        {
            var response = await client.GetAsync($"/api/logs/{attempt}");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest),
                $"'{attempt}' is not a date and must be refused before it reaches the file system");
        }

        // A well-formed date with no file behind it is a 404, not a bad request. The date is a far
        // past one on purpose: today's log exists as soon as the API has run.
        var valid = await client.GetAsync("/api/logs/1970-01-01");
        Assert.That(valid.StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
            "a well-formed date with no file is a 404, not a bad request");
    }

    [Test]
    public async Task RefreshTokenReuse_KillsTheWholeFamily_NotOnlyTheRetiredToken()
    {
        using var client = Factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "bootstrap@example.test", password = Password });
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var firstCookie = login.Headers.GetValues("Set-Cookie").Single().Split(';', 2)[0];

        var rotated = await client.PostAsync("/api/auth/refresh", content: null);
        Assert.That(rotated.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var rotatedCookie = rotated.Headers.GetValues("Set-Cookie").Single().Split(';', 2)[0];

        // Somebody replays the retired cookie: that is the signal the token was copied.
        using var thief = Factory.CreateClient();
        using var replay = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        replay.Headers.Add("Cookie", firstCookie);
        Assert.That((await thief.SendAsync(replay)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        // The family is gone: the token the honest client is holding stops working too.
        using var honest = Factory.CreateClient();
        using var next = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        next.Headers.Add("Cookie", rotatedCookie);
        Assert.That((await honest.SendAsync(next)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            "reuse revokes every live refresh token of the account, so the thief gains nothing");
    }

    [Test]
    public async Task Delete_ReachedTheRowBehindTheQueryFilter_SoASoftDeletedRowCanBeRemovedForGood()
    {
        using var client = await LoginAsSuperadmin();
        var created = await client.PostAsJsonAsync("/api/constants",
            new { label = $"delete-probe-{Guid.NewGuid():N}", value = "x", group = "Brand", parentId = (int?)null });
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.OK), await created.Content.ReadAsStringAsync());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();

        Assert.That((await client.DeleteAsync($"/api/constants/{id}")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        using var scope = PersonaScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        Assert.That(await context.Constants.IgnoreQueryFilters().AnyAsync(row => row.Id == id), Is.True,
            "the first delete is logical and the row stays");

        Assert.That((await client.DeleteAsync($"/api/constants/{id}")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(await context.Constants.IgnoreQueryFilters().AnyAsync(row => row.Id == id), Is.False,
            "the second delete reaches the row the query filter hides and removes it");
    }

    [Test]
    public async Task JsonUpload_ToAMissingRecord_IsRefusedBeforeTheFileIsWritten()
    {
        using var client = await LoginAsSuperadmin();
        var payload = new
        {
            itemId = 888888,
            type = "Photo",
            fileName = "payload.png",
            uploadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("<html>not an image</html>")),
        };
        var response = await client.PostAsJsonAsync("/api/attachment", payload);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), await response.Content.ReadAsStringAsync());
        Assert.That(Directory.Exists(Path.Combine(Factory.MediaRoot, "items", "888888")), Is.False,
            "the JSON route runs the same existence check as the multipart one");
    }

    [Test]
    public async Task JsonUpload_WhenTheInsertFails_RemovesTheFileItJustWrote()
    {
        using var client = await LoginAsSuperadmin();
        var itemId = await CreateItem(client, "orphan probe");

        using var scope = PersonaScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        // Break the insert on purpose: the row cannot be written, so the bytes must not stay.
        await context.Database.ExecuteSqlRawAsync("ALTER TABLE Attachments RENAME TO Attachments_bak");
        try
        {
            var response = await client.PostAsJsonAsync("/api/attachment", new
            {
                itemId,
                type = "Photo",
                fileName = "probe.png",
                uploadBase64 = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5 }),
            });
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.InternalServerError),
                await response.Content.ReadAsStringAsync());

            var directory = Path.Combine(Factory.MediaRoot, "items", itemId.ToString());
            var files = Directory.Exists(directory) ? Directory.GetFiles(directory) : Array.Empty<string>();
            Assert.That(files, Is.Empty, "a failed insert removes the file it just wrote");
        }
        finally
        {
            await context.Database.ExecuteSqlRawAsync("ALTER TABLE Attachments_bak RENAME TO Attachments");
        }
    }

    [Test]
    public async Task SecondDelete_OfAParentWithLiveChildren_DoesNotAnswer500()
    {
        using var client = await LoginAsSuperadmin();
        var parentId = await CreateConstant(client, "delete-parent", null);
        await CreateConstant(client, "delete-child", parentId);

        Assert.That((await client.DeleteAsync($"/api/constants/{parentId}")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await client.DeleteAsync($"/api/constants/{parentId}")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent),
            "the database refuses the hard delete while children point at the row; that is a no-op, not a 500");

        using var scope = PersonaScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var row = await context.Constants.IgnoreQueryFilters().SingleOrDefaultAsync(item => item.Id == parentId);
        Assert.That(row, Is.Not.Null);
        Assert.That(row!.IsActive, Is.False, "the row stays soft-deleted, so nothing is lost");
    }

    [Test]
    public async Task Logs_ReturnsTheFileTheWriterActuallyCreates()
    {
        // The writer names its files yyyyMMdd_Logs.txt; the reader used to ask for yyyy-MM-dd_Logs.txt,
        // so a well-formed date answered 404 while the log sat next to it.
        using var scope = PersonaScope();
        var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var directory = Path.Combine(environment.ContentRootPath, "Logs");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "19990101_Logs.txt");
        await File.WriteAllTextAsync(file, "log-marker-1999");
        try
        {
            using var client = await LoginAsSuperadmin();
            var response = await client.GetAsync("/api/logs/1999-01-01");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
            Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("log-marker-1999"));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Test]
    public async Task SharedVocabulary_CannotBeRenamedFilledOrDeletedByAnOrganization()
    {
        var admin = await CreateAccountAndLogin($"shared-vocabulary-{Guid.NewGuid():N}@example.test", "admin");

        // The reproduction: PUT /api/constants/104 with a token for an organization renamed the
        // FieldType/DateTime row every organization reads. It is seed, not user configuration.
        var rename = await admin.PutAsJsonAsync("/api/constants/104",
            new { id = 104, label = "DateTime HIJACKED", group = "FieldType", parentId = 1, sortOrder = 4 });
        Assert.That(rename.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), await rename.Content.ReadAsStringAsync());

        var additionLabel = $"hijacked-{Guid.NewGuid():N}";
        var addition = await admin.PostAsJsonAsync("/api/constants",
            new { label = additionLabel, group = "FieldType", parentId = 1 });
        Assert.That(addition.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), await addition.Content.ReadAsStringAsync());

        var refusal = await admin.DeleteAsync("/api/constants/104");
        Assert.That(refusal.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), await refusal.Content.ReadAsStringAsync());

        using var scope = PersonaScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var row = await context.Constants.IgnoreQueryFilters().SingleAsync(item => item.Id == 104);
        var added = await context.Constants.IgnoreQueryFilters().AnyAsync(item => item.Label == additionLabel);
        Assert.Multiple(() =>
        {
            Assert.That(row.TenantId, Is.EqualTo(0));
            Assert.That(row.Label, Is.EqualTo("DateTime"), "the shared row is unchanged");
            Assert.That(added, Is.False, "no value was added to the shared vocabulary");
        });
    }

    [Test]
    public async Task AnOrganizationCannotBeMintedByAnAdmin_AndAnUnknownGroupIsRefused()
    {
        var admin = await CreateAccountAndLogin($"rogue-organization-{Guid.NewGuid():N}@example.test", "admin");

        // `Organization` is the tenant list itself: one more row is one more tenant, born active
        // because the interceptor forces IsActive on insert.
        var rogue = await admin.PostAsJsonAsync("/api/constants", new { label = "Rogue Org", group = "Organization" });
        Assert.That(rogue.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), await rogue.Content.ReadAsStringAsync());

        var unknownGroup = await admin.PostAsJsonAsync("/api/constants",
            new { label = "Rogue Org", group = $"Namespace{Guid.NewGuid():N}" });
        Assert.That(unknownGroup.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), await unknownGroup.Content.ReadAsStringAsync());

        var organizations = await admin.GetFromJsonAsync<JsonElement>("/api/organization", Json);
        Assert.That(organizations.EnumerateArray().Select(row => row.GetProperty("id").GetInt32()).ToArray(),
            Is.EqualTo(new[] { 900001, 900002, 900003 }), "no rogue organization exists");

        // The role that owns the list can still add one.
        using var root = await LoginAsSuperadmin();
        var label = $"Probe Org {Guid.NewGuid():N}";
        var created = await root.PostAsJsonAsync("/api/constants", new { label, group = "Organization" });
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.OK), await created.Content.ReadAsStringAsync());
        var listed = await root.GetFromJsonAsync<JsonElement>("/api/organization", Json);
        Assert.That(listed.EnumerateArray().Any(row => row.GetProperty("label").GetString() == label), Is.True);
    }

    [Test]
    public async Task AConstantCannotBeHungFromAnotherOrganizationsParent()
    {
        string foreignLabel = $"live-parent-{Guid.NewGuid():N}";
        int foreignId;
        using (var live = Factory.Services.CreateScope())
        {
            live.ServiceProvider.GetRequiredService<TenantScope>().SelectSystemTenant(900002);
            var liveContext = live.ServiceProvider.GetRequiredService<ApplicationContext>();
            var foreign = new Constants { Group = "Brand", Label = foreignLabel, Code = foreignLabel, TenantId = 900002 };
            liveContext.Constants.Add(foreign);
            await liveContext.SaveChangesAsync();
            foreignId = foreign.Id;
        }

        using var client = await LoginAsSuperadmin();
        var post = await client.PostAsJsonAsync("/api/constants",
            new { label = $"cross-parent-{Guid.NewGuid():N}", group = "Brand", parentId = foreignId });
        Assert.That(post.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), await post.Content.ReadAsStringAsync());

        var ownId = await CreateConstant(client, $"own-parent-{Guid.NewGuid():N}", null);
        var put = await client.PutAsJsonAsync($"/api/constants/{ownId}",
            new { id = ownId, label = "moved", group = "Brand", parentId = foreignId });
        Assert.That(put.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), await put.Content.ReadAsStringAsync());

        using var scope = PersonaScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var persisted = await context.Constants.IgnoreQueryFilters().SingleAsync(row => row.Id == ownId);
        Assert.That(persisted.ParentId, Is.Null, "a refused parent wrote nothing");
    }

    [Test]
    public async Task IdentityEventsAtTheSystemTenant_HaveAReaderForTheSuperadmin()
    {
        var actor = $"system-event-{Guid.NewGuid():N}";
        using (var scope = PersonaScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
            // Anonymous requests carry no organization claim, so Login/LoginFailed/RefreshReuse are
            // written at tenant 0: the rows an incident is read from.
            context.AuditLogs.Add(new AuditLog
            {
                TenantId = 0,
                TimestampUtc = DateTime.UtcNow,
                ActorUserId = actor,
                ActorEmail = "anonymous@example.test",
                Action = "LoginFailed",
                Entity = "IdentityUser",
                EntityId = actor,
                RequestId = "system-event-probe",
            });
            await context.SaveChangesAsync();
        }

        using var root = await LoginAsSuperadmin();
        var response = await root.GetAsync($"/api/audit?action=LoginFailed&actor={actor}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Multiple(() =>
        {
            Assert.That(payload.GetProperty("totalCount").GetInt32(), Is.EqualTo(1));
            Assert.That(payload.GetProperty("items")[0].GetProperty("tenantId").GetInt32(), Is.EqualTo(0),
                "the row's tenant id is what names a system event");
        });

        var admin = await CreateAccountAndLogin($"audit-reader-{Guid.NewGuid():N}@example.test", "admin");
        Assert.That((await admin.GetAsync($"/api/audit?action=LoginFailed&actor={actor}")).StatusCode,
            Is.EqualTo(HttpStatusCode.Forbidden),
            "an organization admin sees nothing outside its organization, and this reader is the superadmin's");
    }

    [Test]
    public async Task Delete_OfAMissingRow_Is404_AndASuperadminCanDeleteASharedRow()
    {
        using var root = await LoginAsSuperadmin();
        Assert.That((await root.DeleteAsync("/api/constants/999999")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
            "the status describes what happened: nothing was deleted");
        Assert.That((await root.DeleteAsync("/api/item/999999")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
            "the rule holds for every table, not only constants");

        var label = $"shared-delete-{Guid.NewGuid():N}";
        int id;
        using (var scope = PersonaScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
            context.Constants.Add(new Constants { Group = "AttachmentType", Label = label, Code = label, TenantId = 0 });
            await context.SaveChangesAsync();
            id = await context.Constants.IgnoreQueryFilters().Where(row => row.Label == label).Select(row => row.Id).SingleAsync();
        }

        Assert.That((await root.DeleteAsync($"/api/constants/{id}")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent),
            "a superadmin may clean up a shared row");

        using var after = PersonaScope();
        var afterContext = after.ServiceProvider.GetRequiredService<ApplicationContext>();
        var row = await afterContext.Constants.IgnoreQueryFilters().SingleAsync(item => item.Id == id);
        Assert.That(row.IsActive, Is.False, "the delete was found and applied");
    }

    [Test]
    public void OrganizationsMigration_DownRuns_AndMigratesForwardAgain()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"imc-migration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var database = Path.Combine(directory, "migration.db");
        try
        {
            var options = new DbContextOptionsBuilder<ApplicationContext>()
                .UseSqlite($"Data Source={database}")
                .Options;
            using var context = new ApplicationContext(options, new TenantScope(new HttpContextAccessor()));
            const string previous = "20261115173510_AddImpersonationSessions";
            var migrator = context.Database.GetService<IMigrator>();

            migrator.Migrate(previous);
            // A parent and its child: Up copies the tree, and Down has to remove it without tripping
            // the Restrict self-reference on the way out.
            context.Database.ExecuteSqlRaw("""
                INSERT INTO Constants (Id, "Group", Icon, Label, SortOrder, UpdatedBy, IsActive, UpdatedAt)
                VALUES (500001, 'Brand', '', 'MIGRATION PROBE', 0, 'system', 1, CURRENT_TIMESTAMP);
                INSERT INTO Constants (Id, "Group", Icon, Label, ParentId, SortOrder, UpdatedBy, IsActive, UpdatedAt)
                VALUES (500002, 'Brand', '', 'MIGRATION CHILD', 500001, 0, 'system', 1, CURRENT_TIMESTAMP);
                """);

            // Up copies Persona's constants into Live Room and Alquimia. The rollback used to die on
            // the unique (Group, Label) index it recreates, leaving the schema half-migrated.
            migrator.Migrate();
            Assert.That(Scalar(context, "SELECT COUNT(*) FROM Constants WHERE TenantId IN (900002, 900003)"),
                Is.GreaterThan(0), "Up copied Persona's rows for the two new organizations");

            migrator.Migrate(previous);
            Assert.Multiple(() =>
            {
                Assert.That(Scalar(context, "SELECT COUNT(*) FROM Constants WHERE \"Group\" = 'Organization'"), Is.Zero,
                    "the organization rows Up inserted are gone");
                Assert.That(Scalar(context, "SELECT COUNT(*) FROM Constants WHERE \"Group\" = 'Brand' AND Label = 'MIGRATION PROBE'"),
                    Is.EqualTo(1), "Persona's own row survives the rollback");
                Assert.That(Scalar(context, "SELECT COUNT(*) FROM Constants WHERE \"Group\" = 'Brand' AND Label = 'MIGRATION CHILD'"),
                    Is.EqualTo(1), "and so does its child: only the copies are removed");
            });

            migrator.Migrate();
            Assert.That(Scalar(context, "SELECT COUNT(*) FROM Constants WHERE TenantId IN (900002, 900003)"),
                Is.GreaterThan(0), "the migration applies again");
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, recursive: true);
        }
    }

    private static int Scalar(ApplicationContext context, string sql)
    {
        var connection = context.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened) connection.Open();
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
        }
        finally
        {
            if (opened) connection.Close();
        }
    }

    private async Task<int> CreateConstant(HttpClient client, string label, int? parentId)
    {
        var response = await client.PostAsJsonAsync("/api/constants",
            new { label, value = label, group = "Brand", parentId });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();
    }

    }
