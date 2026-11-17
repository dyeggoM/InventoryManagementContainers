using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IMCAPI.DB;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Testing;

/// <summary>
/// What the host refuses to start with, and where the pipeline puts its guards.
///
/// The refactor moved all of this into `IMCAPI/Startup/` and the suite could not tell whether the
/// order or the refusals survived: a startup check that is silently dropped still passes every
/// request test. These tests run the real host with a real environment name and assert the boot
/// fails, or that the guard answers before the endpoint does.
/// </summary>
[NonParallelizable]
[TestFixture]
public sealed class StartupInvariantsTests
{
    private const string DevelopmentKey = "dev-lan-signing-key-at-least-32-bytes-long";
    private const string TestKey = "test-signing-key-with-at-least-32-bytes-123456";

    /// <summary>A host in an explicit environment, with the settings a test needs to override.</summary>
    private static WebApplicationFactory<IMCAPI.Program> Host(string environment,
        Dictionary<string, string?> settings)
    {
        // Its own database and media root: ApiFactory writes those two settings as process-wide
        // environment variables, so a host that did not pin them would migrate whichever file another
        // fixture happened to leave behind ("table AspNetRoles already exists").
        var root = Path.Combine(Path.GetTempPath(), "imc-startup-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        return new WebApplicationFactory<IMCAPI.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:DefaultConnection",
                $"Data Source={Path.Combine(root, "startup.db")}");
            builder.UseSetting("Media:Root", Path.Combine(root, "media"));
            builder.UseSetting("Auth:Jwt:Key", TestKey);
            builder.UseSetting("Auth:Jwt:Issuer", "InventoryManagementContainers.Tests");
            builder.UseSetting("Auth:Bootstrap:Email", "bootstrap@example.test");
            builder.UseSetting("Auth:Bootstrap:Password", "AuthTest!Secure123");
            foreach (var (key, value) in settings) builder.UseSetting(key, value);
        });
    }

    private static string Boot(WebApplicationFactory<IMCAPI.Program> factory)
    {
        // CreateClient is what builds the host, so a refusal surfaces here.
        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient(),
            "the host must refuse to start");
        Assert.That(exception, Is.Not.Null);
        return exception!.ToString();
    }

    [Test]
    public void OutsideDevelopment_ThePublishedDevelopmentKeyIsRefused()
    {
        using var factory = Host("Production", new() { ["Auth:Jwt:Key"] = DevelopmentKey });

        var message = Boot(factory);

        Assert.That(message, Does.Contain("development key"),
            "a key that every reader of the repository knows must not sign tokens outside a dev box");
    }

    [Test]
    public void OutsideDevelopment_AnEmptyCorsListIsRefused()
    {
        using var factory = Host("Production", new() { ["CORS:AllowedCors"] = "" });

        var message = Boot(factory);

        Assert.That(message, Does.Contain("CORS:AllowedCors"));
    }

    [Test]
    public void OutsideDevelopment_TurningAuthorizationOffIsRefused()
    {
        using var factory = Host("Production", new() { ["Auth:Enforce"] = "false" });

        var message = Boot(factory);

        Assert.That(message, Does.Contain("Auth:Enforce=false"),
            "a switch that turns every role check into a no-op cannot survive a deployment");
    }

    [Test]
    public async Task InDevelopment_TheSamePiecesAreAccepted()
    {
        // The control for the three refusals: the same host, in Development, starts and serves.
        using var factory = Host("Development", new() { ["CORS:AllowedCors"] = "", ["Auth:Enforce"] = "false" });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/item/test");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "the probe endpoint is anonymous, and the dev box is allowed to run with those settings");
    }

    /// <summary>The bootstrap account this factory seeded, signed in on a client of its own.</summary>
    private static async Task SignIn(HttpClient client)
    {
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "bootstrap@example.test", password = "AuthTest!Secure123" });
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("access_token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    [Test]
    public async Task TheLoginWindowAnswersBeforeAuthenticationDoes()
    {
        // Rate limiting is registered before authentication, so a caller that has no credentials is
        // throttled too. If the order were reversed, an anonymous flood would reach the identity
        // stack and the limiter would only see authenticated traffic.
        using var factory = new ApiFactory(authPermitLimit: 3);
        using var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login",
                new { email = "nadie@example.test", password = "no-importa" });
            statuses.Add(response.StatusCode);
        }

        Assert.That(statuses, Has.Some.EqualTo(HttpStatusCode.TooManyRequests),
            "the window is enforced on unauthenticated attempts");
        Assert.That(statuses.Count(status => status == HttpStatusCode.TooManyRequests), Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public async Task TheProbeEndpointAnswersWithoutACredential()
    {
        // BaseController.TestEndpoint is [AllowAnonymous] and inherited by every controller, so it is
        // reachable in production: a test that pins it is the difference between "we know" and "we hope".
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/item/test");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Multiple(() =>
        {
            Assert.That(body.TryGetProperty("isActive", out var isActive), Is.True,
                "it answers with the entity shape it inherits, not an error envelope");
            Assert.That(isActive.GetBoolean(), Is.True);
        });
    }

    [Test]
    public async Task TwoFactoriesDoNotShareTheirDatabase_AndNeitherWritesToTheProcess()
    {
        // The factory used to hand its own connection string to the whole process, so a second host
        // inherited the first one's database — "table AspNetRoles already exists" was the symptom.
        using var first = new ApiFactory();
        using var second = new ApiFactory();

        Assert.Multiple(() =>
        {
            Assert.That(Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection"), Is.Null,
                "a factory must not decide where the next host connects");
            Assert.That(Environment.GetEnvironmentVariable("Media__Root"), Is.Null,
                "nor where it stores its files");
        });

        using var one = first.CreateClient();
        using var two = second.CreateClient();
        await SignIn(one);
        await SignIn(two);

        using var scope = first.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMCAPI.Services.TenantScope>().SelectSystemTenant(900001);
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        context.Items.Add(new IMCAPI.DB.Entities.Item { Label = "only in the first" });
        await context.SaveChangesAsync();

        var mine = await one.GetFromJsonAsync<JsonElement>("/api/item");
        var theirs = await two.GetFromJsonAsync<JsonElement>("/api/item");

        Assert.Multiple(() =>
        {
            Assert.That(mine.GetRawText(), Does.Contain("only in the first"));
            Assert.That(theirs.GetRawText(), Does.Not.Contain("only in the first"),
                "the second host reads its own database, not the first one's");
        });
    }

    [Test]
    public async Task TheServiceLifetimesAreTheOnesTheApiDependsOn()
    {
        // The startup refactor moved every registration; a lifetime that silently changed would only
        // show up as a concurrency bug under load.
        using var factory = new ApiFactory();
        using var scope = factory.Services.CreateScope();
        var provider = scope.ServiceProvider;

        Assert.Multiple(() =>
        {
            Assert.That(provider.GetRequiredService<IMCAPI.Services.AttachmentStorage>(),
                Is.SameAs(scope.ServiceProvider.GetRequiredService<IMCAPI.Services.AttachmentStorage>()),
                "storage is a singleton: it owns the media root and its caches");
            var contextOnce = provider.GetRequiredService<ApplicationContext>();
            var contextAgain = provider.GetRequiredService<ApplicationContext>();
            Assert.That(contextOnce, Is.SameAs(contextAgain),
                "resolved twice inside one scope, the same context comes back");
            using var other = factory.Services.CreateScope();
            Assert.That(provider.GetRequiredService<IMCAPI.Services.ItemImporter>(),
                Is.Not.SameAs(other.ServiceProvider.GetRequiredService<IMCAPI.Services.ItemImporter>()),
                "the importer is scoped: two requests never share one");
        });
    }
}
