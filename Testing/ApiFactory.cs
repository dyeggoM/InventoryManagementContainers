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
    static ApiFactory()
    {
        Environment.SetEnvironmentVariable("AUTH_JWT_KEY", "test-signing-key-with-at-least-32-bytes-123456");
        Environment.SetEnvironmentVariable("AUTH_JWT_ISSUER", "InventoryManagementContainers.Tests");
        Environment.SetEnvironmentVariable("AUTH_BOOTSTRAP_EMAIL", "bootstrap@example.test");
        Environment.SetEnvironmentVariable("AUTH_BOOTSTRAP_PASSWORD", "AuthTest!Secure123");
    }

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "imc-api-tests", Guid.NewGuid().ToString("N"));

    private readonly int? _authPermitLimit;

    /// <param name="authPermitLimit">
    /// How many `/api/auth` calls one address may make per window. Left null the suite is effectively
    /// unlimited, because a hundred logins must not trip a production guard; the rate-limit tests
    /// pass a small number to walk the window.
    /// </param>
    public ApiFactory(int? authPermitLimit = null) => _authPermitLimit = authPermitLimit;

    public string DatabasePath => Path.Combine(_root, "test.db");

    public string MediaRoot => Path.Combine(_root, "media-store");

    public new HttpClient CreateClient()
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", $"Data Source={DatabasePath}");
        Environment.SetEnvironmentVariable("Media__Root", MediaRoot);
        return base.CreateClient();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(MediaRoot);

        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = $"Data Source={DatabasePath}",
                ["Media:Root"] = MediaRoot,
                ["CORS:AllowedCors"] = "http://localhost",
                ["Auth:Jwt:Key"] = "test-signing-key-with-at-least-32-bytes-123456",
                ["Auth:Jwt:Issuer"] = "InventoryManagementContainers.Tests",
                ["Auth:Enforce"] = "true",
                ["Auth:Bootstrap:Email"] = "bootstrap@example.test",
                ["Auth:Bootstrap:Password"] = "AuthTest!Secure123",
                // The limiter is a production guard; a suite that logs in a hundred times must not
                // trip it. The test that exercises it raises its own factory with a tiny window.
                ["Auth:RateLimit:PermitLimit"] = (_authPermitLimit ?? 100000).ToString(),
                ["Auth:RateLimit:WindowSeconds"] = "60",
            });
        });
    }

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
