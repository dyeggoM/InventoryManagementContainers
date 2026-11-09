using Microsoft.EntityFrameworkCore;

namespace IMCAPI.DB;

/// <summary>
/// Seeds the configuration tree a fresh database needs. The database itself is not in source
/// control any more, so the app has to be able to build one from nothing.
/// </summary>
public static class DatabaseSeeder
{
    /// <summary>
    /// Runs <c>DB/Seed/seed.sql</c> when the constant table is empty. Only configuration is
    /// seeded — items are created by the user, so seeding never invents inventory.
    /// </summary>
    /// <returns>true when the seed script was executed.</returns>
    public static bool SeedConfiguration(ApplicationContext context, string contentRootPath)
    {
        if (context.Constants.Any())
        {
            return false;
        }

        var seedPath = Path.Combine(contentRootPath, "DB", "Seed", "seed.sql");
        if (!File.Exists(seedPath))
        {
            return false;
        }

        context.Database.ExecuteSqlRaw(File.ReadAllText(seedPath));
        return true;
    }
}
