using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Testing;

/// <summary>
/// A real API over a throwaway database and media folder: migrations, seeding and the startup
/// backfills all run, so the tests exercise the same path a fresh clone takes. Each factory gets its
/// own SQLite file, so tests never see each other's records and nothing lands in the project folder.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<IMCAPI.Program>
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "imc-api-tests", Guid.NewGuid().ToString("N"));

    private readonly int? _authPermitLimit;
    private readonly int? _maxDepth;

    /// <param name="authPermitLimit">
    /// How many `/api/auth` calls one address may make per window. Left null the suite is effectively
    /// unlimited, because a hundred logins must not trip a production guard; the rate-limit tests
    /// pass a small number to walk the window.
    /// </param>
    /// <summary>
    /// The connection string is set here, not in <see cref="CreateClient"/>, because a test that only
    /// reads <c>Services</c> would otherwise build the host with the default `./imc.db` — measured: a
    /// stray database appeared next to the test binary and the second run refused to start with
    /// "exactly one superadmin must exist; found 2".
    /// </summary>
    public ApiFactory(int? authPermitLimit = null, int? maxDepth = null)
    {
        _authPermitLimit = authPermitLimit;
        _maxDepth = maxDepth;
        Directory.CreateDirectory(MediaRoot);
    }

    public string DatabasePath => Path.Combine(_root, "test.db");

    public string MediaRoot => Path.Combine(_root, "media-store");

    public new HttpClient CreateClient() => base.CreateClient();

    /// <summary>
    /// Every setting this host needs, per host. It used to write them as process-wide environment
    /// variables instead, which every later host inherited: a second factory pointed at the first
    /// one's database, and a host created for a different purpose migrated a file another test had
    /// already migrated ("table AspNetRoles already exists"). Nothing here touches the environment.
    /// </summary>
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(MediaRoot);

        builder.UseEnvironment("Development");

        // `UseSetting`, not `ConfigureAppConfiguration`: the API reads these in `Main` — the JWT key
        // before the host is built — and a provider added to the application configuration arrives
        // too late for that read. Host settings are there from the start, which is what a value read
        // during startup needs.
        foreach (var (key, value) in Settings())
            builder.UseSetting(key, value);
        if (_maxDepth is int depth) builder.UseSetting("Items:MaxDepth", depth.ToString());
    }

    private Dictionary<string, string> Settings() => new()
    {
        ["ConnectionStrings:DefaultConnection"] = $"Data Source={DatabasePath}",
        ["Media:Root"] = MediaRoot,
        ["CORS:AllowedCors"] = "http://localhost",
        ["Auth:Jwt:Key"] = "test-signing-key-with-at-least-32-bytes-123456",
        ["Auth:Jwt:Issuer"] = "InventoryManagementContainers.Tests",
        ["Auth:Enforce"] = "true",
        ["Auth:Bootstrap:Email"] = "bootstrap@example.test",
        ["Auth:Bootstrap:Password"] = "AuthTest!Secure123",
        // The limiter is a production guard; a suite that logs in a hundred times must not trip it.
        // The test that exercises it raises its own factory with a tiny window.
        ["Auth:RateLimit:PermitLimit"] = (_authPermitLimit ?? 100000).ToString(),
        ["Auth:RateLimit:WindowSeconds"] = "60",
    };

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A file still open on Windows is not a test failure.
        }
    }
}
