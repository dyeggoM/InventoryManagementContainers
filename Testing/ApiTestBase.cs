using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IMCAPI.DB;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using IMCAPI.Services;

namespace Testing;

/// <summary>
/// One copy of the scaffolding the HTTP fixtures share: an API of its own over a throwaway database,
/// the login and bearer-token plumbing, and the calls that more than one fixture used to carry a copy
/// of.
///
/// The database is deliberately not shared between fixtures. Each fixture still builds its own
/// factory, so one fixture's records and item codes never reach another's; what is shared here is the
/// code that builds it, not the instance. A fixture that needs something else from the constructor
/// overrides <see cref="CreateFactory"/> — the rate-limit tests set the window there — and a fixture
/// that must not keep a database between tests overrides <see cref="FreshApiPerTest"/>.
/// </summary>
public abstract class ApiTestBase
{
    /// <summary>The bootstrap superadmin: the one account a fresh database is seeded with.</summary>
    protected const string SuperadminEmail = "bootstrap@example.test";

    /// <summary>The password of the bootstrap account, and of the accounts a test creates itself.</summary>
    protected const string Password = "AuthTest!Secure123";

    protected const string Csv = "text/csv";

    protected const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>This fixture's API, over this fixture's own database and media folder.</summary>
    protected ApiFactory Factory { get; private set; } = null!;

    /// <summary>The client the fixture's tests share. Signed in unless the fixture opted out.</summary>
    protected HttpClient Client { get; private set; } = null!;

    /// <summary>
    /// Whether <see cref="Client"/> arrives signed in as the bootstrap superadmin. False for the
    /// fixtures whose tests are about getting a token, or about what a caller without one gets.
    /// </summary>
    protected virtual bool SignsInAtStartup => true;

    /// <summary>
    /// Whether every test gets a database of its own instead of the fixture sharing one. The import
    /// writes change the tree and one of them imports the same file twice; the rate-limit window is
    /// spent by the first test that walks it. Neither can share a database with a sibling test.
    /// </summary>
    protected virtual bool FreshApiPerTest => false;

    /// <summary>
    /// Builds this fixture's factory. Override it only to configure the constructor, never to hand
    /// out one shared factory: the per-fixture database is what keeps the suite's tests independent.
    /// </summary>
    protected virtual ApiFactory CreateFactory() => new();

    /// <summary>A requestless fixture scope acting for Persona, including filtered reads.</summary>
    protected IServiceScope PersonaScope()
    {
        var scope = Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantScope>().SelectSystemTenant(900001);
        return scope;
    }

    /// <summary>Opens the fixture's API and, unless opted out, signs the shared client in.</summary>
    protected virtual async Task StartApi()
    {
        Factory = CreateFactory();
        Client = Factory.CreateClient();
        if (SignsInAtStartup) await Authenticate(Client, SuperadminEmail);
    }

    /// <summary>Closes the fixture's API. The factory deletes its database and its media folder.</summary>
    protected virtual void StopApi()
    {
        if (Client is not null) Client.Dispose();
        if (Factory is not null) Factory.Dispose();
        Client = null!;
        Factory = null!;
    }

    [OneTimeSetUp]
    public async Task StartFixture()
    {
        if (!FreshApiPerTest) await StartApi();
    }

    [OneTimeTearDown]
    public void StopFixture()
    {
        if (!FreshApiPerTest) StopApi();
    }

    [SetUp]
    public async Task StartTestApi()
    {
        if (FreshApiPerTest) await StartApi();
    }

    [TearDown]
    public void StopTestApi()
    {
        if (FreshApiPerTest) StopApi();
    }

    /// <summary>Puts a bearer token for that account on a client the caller already owns.</summary>
    protected static async Task Authenticate(HttpClient client, string email, string password = Password)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            $"login as {email}: {await login.Content.ReadAsStringAsync()}");
        var payload = await login.Content.ReadFromJsonAsync<JsonElement>(Json);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", payload.GetProperty("access_token").GetString());
    }

    /// <summary>Logs a brand-new client in and returns it carrying that account's token.</summary>
    protected async Task<HttpClient> LoginAs(string email, string password = Password)
    {
        var client = Factory.CreateClient();
        await Authenticate(client, email, password);
        return client;
    }

    /// <summary>The same, as the only superadmin a fresh database has.</summary>
    protected Task<HttpClient> LoginAsSuperadmin() => LoginAs(SuperadminEmail);

    /// <summary>
    /// Creates an account with that role through Identity and returns a client signed in as it. The
    /// API cannot hand out a superadmin, so a test that needs one keeps the bootstrap account.
    /// </summary>
    protected async Task<HttpClient> CreateAccountAndLogin(string email, string role, string password = Password)
    {
        using (var scope = PersonaScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            var created = await users.CreateAsync(user, password);
            Assert.That(created.Succeeded, Is.True,
                string.Join("; ", created.Errors.Select(error => error.Description)));
            Assert.That((await users.AddToRoleAsync(user, role)).Succeeded, Is.True);
        }

        return await LoginAs(email, password);
    }

    /// <summary>Creates an item with the payload the UI posts and returns its id.</summary>
    protected async Task<int> CreateItem(HttpClient client, string label, int? parentId = null)
    {
        var response = await client.PostAsJsonAsync("/api/item", new
        {
            label, icon = "", parentId, attributes = "{}",
            children = Array.Empty<object>(), attachments = Array.Empty<object>(),
        });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();
    }

    /// <summary>The same, on the fixture's shared client.</summary>
    protected Task<int> CreateItem(string label, int? parentId = null) => CreateItem(Client, label, parentId);

    /// <summary>
    /// Every item row in the database, the ones the soft delete hides from the API included: the
    /// count is how a test says "the refused import wrote nothing at all".
    /// </summary>
    protected async Task<int> CountItems()
    {
        using var scope = PersonaScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        return await context.Items.IgnoreQueryFilters().CountAsync();
    }

    /// <summary>Where a fixture file lives: next to the test binary, copied there by the project.</summary>
    protected static string FixturePath(string name) =>
        Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);

    /// <summary>
    /// Posts a fixture file as the multipart body the import routes expect and hands back the raw
    /// answer, so a test can assert on the refusal itself.
    /// </summary>
    protected async Task<HttpResponseMessage> PostFixture(string route, string fixture, string contentType)
    {
        using var payload = new MultipartFormDataContent();
        var file = new ByteArrayContent(await File.ReadAllBytesAsync(FixturePath(fixture)));
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        payload.Add(file, "file", fixture);
        payload.Add(new StringContent(OrganizationConfiguration.DefaultTenantId.ToString()), "organizationId");
        return await Client.PostAsync(route, payload);
    }

    /// <summary>The preview of a fixture file: what the route says, or a failed assertion if it refused.</summary>
    protected async Task<JsonElement> Preview(string fixture, string contentType = Xlsx)
    {
        var response = await PostFixture("/api/import/preview", fixture, contentType);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    /// <summary>
    /// The write call. <paramref name="includeYellow"/> left null puts no field in the body at all,
    /// which is what a test that pins the route's own default has to send.
    /// </summary>
    protected async Task<HttpResponseMessage> PostImport(string fixture, string contentType = Xlsx,
        bool? includeYellow = true, IEnumerable<string>? skipCodes = null)
    {
        using var payload = new MultipartFormDataContent();
        var file = new ByteArrayContent(await File.ReadAllBytesAsync(FixturePath(fixture)));
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        payload.Add(file, "file", fixture);
        payload.Add(new StringContent(OrganizationConfiguration.DefaultTenantId.ToString()), "organizationId");
        if (includeYellow is { } include) payload.Add(new StringContent(include ? "true" : "false"), "includeYellow");
        foreach (var code in skipCodes ?? Array.Empty<string>()) payload.Add(new StringContent(code), "skipCodes");
        return await Client.PostAsync("/api/import", payload);
    }

    /// <summary>The import of a fixture file, asserted successful: the summary the route answered.</summary>
    protected async Task<JsonElement> Import(string fixture, string contentType = Xlsx,
        bool includeYellow = true, string[]? skipCodes = null)
    {
        var response = await PostImport(fixture, contentType, includeYellow, skipCodes);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>(Json);
    }

    /// <summary>Every row of a preview, flattening the category → group → item tree it answers with.</summary>
    protected static IEnumerable<JsonElement> Items(JsonElement preview) =>
        preview.GetProperty("sheets").EnumerateArray().SelectMany(ItemsOfSheet);

    /// <summary>The rows of one category of a preview.</summary>
    protected static IEnumerable<JsonElement> ItemsOfSheet(JsonElement sheet) =>
        sheet.GetProperty("groups").EnumerateArray()
            .SelectMany(group => group.GetProperty("items").EnumerateArray());

    protected static string? Label(JsonElement item) => item.GetProperty("label").GetString();
}
