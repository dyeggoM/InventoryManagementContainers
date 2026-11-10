using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace IMCAPI.DB;

public static class DatabaseSeeder
{
    private static readonly string[] Roles = ["superadmin", "admin", "operador", "lector"];

    public static async Task SeedIdentityAsync(
        UserManager<IdentityUser> users,
        RoleManager<IdentityRole> roles,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        foreach (var role in Roles)
        {
            if (!await roles.RoleExistsAsync(role))
            {
                var result = await roles.CreateAsync(new IdentityRole(role));
                if (!result.Succeeded)
                    throw new InvalidOperationException($"Could not create role '{role}': {string.Join("; ", result.Errors.Select(error => error.Description))}");
            }
        }

        var superadmins = await users.GetUsersInRoleAsync("superadmin");
        if (superadmins.Count == 0)
        {
            var email = configuration["Auth:Bootstrap:Email"] ?? Environment.GetEnvironmentVariable("AUTH_BOOTSTRAP_EMAIL");
            var password = configuration["Auth:Bootstrap:Password"] ?? Environment.GetEnvironmentVariable("AUTH_BOOTSTRAP_PASSWORD");
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("Configure Auth:Bootstrap:Email and Auth:Bootstrap:Password to initialize the superadmin account.");

            var user = await users.FindByEmailAsync(email);
            if (user is null)
            {
                user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
                var created = await users.CreateAsync(user, password);
                if (!created.Succeeded)
                    throw new InvalidOperationException($"Could not create bootstrap superadmin: {string.Join("; ", created.Errors.Select(error => error.Description))}");
            }
            else if (!await users.CheckPasswordAsync(user, password))
            {
                throw new InvalidOperationException("The configured bootstrap account exists but its password does not match.");
            }

            var assigned = await users.AddToRoleAsync(user, "superadmin");
            if (!assigned.Succeeded)
                throw new InvalidOperationException($"Could not assign superadmin role: {string.Join("; ", assigned.Errors.Select(error => error.Description))}");
        }

        await EnsureSingleSuperadminAsync(users);
    }

    public static async Task EnsureSingleSuperadminAsync(UserManager<IdentityUser> users)
    {
        var superadmins = await users.GetUsersInRoleAsync("superadmin");
        if (superadmins.Count != 1)
            throw new InvalidOperationException($"Exactly one superadmin account must exist; found {superadmins.Count}.");
    }

    public static async Task EnsureUserProfilesAsync(ApplicationContext context,
        UserManager<IdentityUser> users, CancellationToken cancellationToken = default)
    {
        var idsWithProfiles = await context.UserProfiles.Select(profile => profile.UserId).ToHashSetAsync(cancellationToken);
        var missingProfiles = await users.Users.Where(user => !idsWithProfiles.Contains(user.Id)).ToListAsync(cancellationToken);
        foreach (var user in missingProfiles)
            context.UserProfiles.Add(new IMCAPI.DB.Entities.UserProfile { UserId = user.Id, IsActive = true });
        if (missingProfiles.Count > 0)
            await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Seeds catalog definitions on a fresh database.</summary>
    public static bool SeedConfiguration(ApplicationContext context, string contentRootPath)
    {
        if (context.Constants.Any()) return false;
        var seedPath = Path.Combine(contentRootPath, "DB", "Seed", "seed.sql");
        if (!File.Exists(seedPath)) return false;
        context.Database.ExecuteSqlRaw(File.ReadAllText(seedPath));
        return true;
    }

    public static async Task<int> BackfillAttributeTypesAsync(ApplicationContext context, CancellationToken cancellationToken = default)
    {
        var pending = await context.Constants.Where(c => c.Group == "Attribute" && c.ParentId != null && c.DataType == null).ToListAsync(cancellationToken);
        foreach (var attribute in pending)
            attribute.DataType = attribute.Label switch { "BuyPrice" or "RentPrice" => "Money", "Description" or "Notes" => "LongText", _ => "Text" };
        if (pending.Count > 0) await context.SaveChangesAsync(cancellationToken);
        return pending.Count;
    }

    public static async Task<int> BackfillAttachmentsAsync(ApplicationContext context, IMCAPI.Services.AttachmentStorage storage, CancellationToken cancellationToken = default)
    {
        var legacy = await context.Attachments.Where(attachment => attachment.FilePath == null && attachment.ThumbnailBase64 != null).ToListAsync(cancellationToken);
        foreach (var attachment in legacy)
        {
            var bytes = IMCAPI.Services.AttachmentStorage.DecodePayload(attachment.ThumbnailBase64);
            if (bytes is null || bytes.Length == 0) continue;
            var stored = await storage.SaveAsync(attachment.ItemId, attachment.FileName, bytes, cancellationToken);
            attachment.FilePath = stored.RelativePath;
            attachment.ThumbnailBase64 = stored.ThumbnailBase64;
        }
        if (legacy.Count > 0) await context.SaveChangesAsync(cancellationToken);
        return legacy.Count;
    }
}
