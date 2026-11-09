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

    public string DatabasePath => Path.Combine(_root, "test.db");

    public string MediaRoot => Path.Combine(_root, "media-store");

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
