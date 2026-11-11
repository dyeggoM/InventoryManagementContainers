using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IMCAPI.DB;
using Microsoft.EntityFrameworkCore;
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
public sealed class SecurityRegressionTests
{
    private const string Password = "AuthTest!Secure123";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private ApiFactory _factory = null!;

    [OneTimeSetUp]
    public void Start() => _factory = new ApiFactory();

    [OneTimeTearDown]
    public void Stop() => _factory.Dispose();

    [Test]
    public async Task Upload_ToAMissingRecord_IsRefusedBeforeAnythingIsWrittenToDisk()
    {
        using var client = await SuperadminClient();
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

        var orphanDirectory = Path.Combine(_factory.MediaRoot, "items", "999999");
        Assert.That(Directory.Exists(orphanDirectory), Is.False,
            "the record was checked before the bytes were written, so no folder is left behind");
    }

    [Test]
    public async Task Upload_DoesNotKeepAClientSuppliedHostileExtension()
    {
        using var client = await SuperadminClient();
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
        Assert.That(File.Exists(Path.Combine(_factory.MediaRoot, storedPath.Replace('/', Path.DirectorySeparatorChar))), Is.True);
    }

    [Test]
    public async Task Logs_RejectsASegmentThatIsNotADate()
    {
        using var client = await SuperadminClient();
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
        using var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "bootstrap@example.test", password = Password });
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var firstCookie = login.Headers.GetValues("Set-Cookie").Single().Split(';', 2)[0];

        var rotated = await client.PostAsync("/api/auth/refresh", content: null);
        Assert.That(rotated.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var rotatedCookie = rotated.Headers.GetValues("Set-Cookie").Single().Split(';', 2)[0];

        // Somebody replays the retired cookie: that is the signal the token was copied.
        using var thief = _factory.CreateClient();
        using var replay = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        replay.Headers.Add("Cookie", firstCookie);
        Assert.That((await thief.SendAsync(replay)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        // The family is gone: the token the honest client is holding stops working too.
        using var honest = _factory.CreateClient();
        using var next = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        next.Headers.Add("Cookie", rotatedCookie);
        Assert.That((await honest.SendAsync(next)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            "reuse revokes every live refresh token of the account, so the thief gains nothing");
    }

    [Test]
    public async Task Delete_ReachedTheRowBehindTheQueryFilter_SoASoftDeletedRowCanBeRemovedForGood()
    {
        using var client = await SuperadminClient();
        var created = await client.PostAsJsonAsync("/api/constants",
            new { label = $"delete-probe-{Guid.NewGuid():N}", value = "x", group = "Test", parentId = (int?)null });
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.OK), await created.Content.ReadAsStringAsync());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();

        Assert.That((await client.DeleteAsync($"/api/constants/{id}")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        using var scope = _factory.Services.CreateScope();
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
        using var client = await SuperadminClient();
        var payload = new
        {
            itemId = 888888,
            type = "Photo",
            fileName = "payload.png",
            uploadBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("<html>not an image</html>")),
        };
        var response = await client.PostAsJsonAsync("/api/attachment", payload);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), await response.Content.ReadAsStringAsync());
        Assert.That(Directory.Exists(Path.Combine(_factory.MediaRoot, "items", "888888")), Is.False,
            "the JSON route runs the same existence check as the multipart one");
    }

    [Test]
    public async Task JsonUpload_WhenTheInsertFails_RemovesTheFileItJustWrote()
    {
        using var client = await SuperadminClient();
        var itemId = await CreateItem(client, "orphan probe");

        using var scope = _factory.Services.CreateScope();
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

            var directory = Path.Combine(_factory.MediaRoot, "items", itemId.ToString());
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
        using var client = await SuperadminClient();
        var parentId = await CreateConstant(client, "delete-parent", null);
        await CreateConstant(client, "delete-child", parentId);

        Assert.That((await client.DeleteAsync($"/api/constants/{parentId}")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await client.DeleteAsync($"/api/constants/{parentId}")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent),
            "the database refuses the hard delete while children point at the row; that is a no-op, not a 500");

        using var scope = _factory.Services.CreateScope();
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
        using var scope = _factory.Services.CreateScope();
        var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        var directory = Path.Combine(environment.ContentRootPath, "Logs");
        Directory.CreateDirectory(directory);
        var file = Path.Combine(directory, "19990101_Logs.txt");
        await File.WriteAllTextAsync(file, "log-marker-1999");
        try
        {
            using var client = await SuperadminClient();
            var response = await client.GetAsync("/api/logs/1999-01-01");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
            Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("log-marker-1999"));
        }
        finally
        {
            File.Delete(file);
        }
    }

    private async Task<int> CreateConstant(HttpClient client, string label, int? parentId)
    {
        var response = await client.PostAsJsonAsync("/api/constants",
            new { label, value = label, group = "Test", parentId });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();
    }

    private async Task<int> CreateItem(HttpClient client, string label)
    {
        var response = await client.PostAsJsonAsync("/api/item",
            new { label, icon = "", attributes = "{}" });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();
    }

    private async Task<HttpClient> SuperadminClient()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "bootstrap@example.test", password = Password });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("access_token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
