using IMCAPI.DB;
using IMCAPI.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace IMCAPI.Startup;

public static class StartupTasks
{
    public static Task RunStartupTasksAsync(this WebApplication app)
    {
        using (var serviceScope = app.Services.CreateScope())
        {
            var context = serviceScope.ServiceProvider.GetService<ApplicationContext>();
            context?.Database.Migrate();

            // The database is not in source control: build the configuration tree on first run.
            if (context is not null)
            {
                DatabaseSeeder.SeedConfiguration(context, app.Environment.ContentRootPath);
                var userManager = serviceScope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
                var roleManager = serviceScope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                DatabaseSeeder.SeedIdentityAsync(userManager, roleManager, app.Configuration).GetAwaiter().GetResult();
                DatabaseSeeder.EnsureUserProfilesAsync(context, userManager).GetAwaiter().GetResult();

                // Older rows carry the whole image; move it to disk once.
                var storage = serviceScope.ServiceProvider.GetRequiredService<AttachmentStorage>();
                DatabaseSeeder.BackfillAttachmentsAsync(context, storage).GetAwaiter().GetResult();

                // Attribute definitions predate typed values; give them sensible types once.
                DatabaseSeeder.BackfillAttributeTypesAsync(context).GetAwaiter().GetResult();
            }
        }

        return Task.CompletedTask;
    }
}
