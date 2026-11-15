using Microsoft.AspNetCore.Authorization;

namespace IMCAPI.Startup;

public static class AuthorizationSetup
{
    public static void AddAppAuthorization(this WebApplicationBuilder builder)
    {
        var authEnforce = IsEnforced(builder.Configuration);
        builder.Services.AddAuthorization(options =>
        {
            AddRolePolicy(options, "RequiereLectura", ["superadmin", "admin", "operador", "lector"], authEnforce, allowImpersonationRead: true);
            AddRolePolicy(options, "RequiereEscritura", ["superadmin", "admin", "operador"], authEnforce);
            AddRolePolicy(options, "RequiereAdmin", ["superadmin", "admin"], authEnforce);
            AddRolePolicy(options, "RequiereSuperadmin", ["superadmin"], authEnforce);
        });
    }

    /// <summary>
    /// The same reading of <c>Auth:Enforce</c> the policies use, for the boot-time warning the host
    /// logs after it is built.
    /// </summary>
    internal static bool IsEnforced(IConfiguration configuration) =>
        !bool.TryParse(configuration["Auth:Enforce"], out var configuredEnforce) || configuredEnforce;

    private static void AddRolePolicy(AuthorizationOptions options,
        string name, string[] roles, bool enforce, bool allowImpersonationRead = false)
    {
        options.AddPolicy(name, policy => policy.RequireAssertion(context =>
        {
            var impersonating = context.User.HasClaim(claim => claim.Type == "act");
            return (!impersonating || allowImpersonationRead) && (!enforce || roles.Any(context.User.IsInRole));
        }));
    }
}
