using System.Text;
using IMCAPI.DB;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace IMCAPI.Startup;

public static class AuthSetup
{
    public static void AddJwtAuthentication(this WebApplicationBuilder builder)
    {
        var jwtKey = builder.Configuration["Auth:Jwt:Key"] ?? Environment.GetEnvironmentVariable("AUTH_JWT_KEY")
            ?? throw new InvalidOperationException("Configure Auth:Jwt:Key or AUTH_JWT_KEY.");

        // The key that `scripts/dev-lan.sh` used to ship as a constant is in every clone of this
        // repository, so anyone who can read it and reach the API can mint a superadmin token.
        // Development may keep using its own key, but it is generated per machine now — a
        // deployment that answers with the published one is a mistake, not a configuration.
        const string publishedDevelopmentKey = "dev-lan-signing-key-at-least-32-bytes-long";
        if (!builder.Environment.IsDevelopment() && string.Equals(jwtKey, publishedDevelopmentKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "AUTH_JWT_KEY is the development key published in scripts/dev-lan.sh: any reader of the "
                + "repository could mint tokens with it. Set a key of its own outside Development.");
        }
        var jwtIssuer = builder.Configuration["Auth:Jwt:Issuer"] ?? Environment.GetEnvironmentVariable("AUTH_JWT_ISSUER")
            ?? throw new InvalidOperationException("Configure Auth:Jwt:Issuer or AUTH_JWT_ISSUER.");
        if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
            throw new InvalidOperationException("Auth:Jwt:Key must contain at least 32 UTF-8 bytes.");

        builder.Services.AddIdentityCore<IdentityUser>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = jwtIssuer,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = System.Security.Claims.ClaimTypes.Name,
                    RoleClaimType = System.Security.Claims.ClaimTypes.Role,
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context => ValidateTokenAsync(context)
                };
            });
    }

    private static async Task ValidateTokenAsync(TokenValidatedContext context)
    {
        var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationContext>();
        var principal = context.Principal!;
        var org = principal.FindFirst("org")?.Value;
        if (!int.TryParse(org, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var organizationId)
            || !await db.Constants.IgnoreQueryFilters().AsNoTracking().AnyAsync(row =>
                row.Id == organizationId && row.Group == "Organization" && row.TenantId == 0 && row.IsActive))
        {
            context.Fail("The organization is no longer active.");
            return;
        }
        var subjectId = principal.FindFirst("sub")?.Value
            ?? principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var actorId = principal.FindFirst("act")?.Value;
        var stoppingImpersonation = string.Equals(context.Request.Path.Value?.TrimEnd('/'),
            "/api/auth/stop-impersonation", StringComparison.OrdinalIgnoreCase);
        var ids = (stoppingImpersonation && actorId is not null ? new[] { actorId }
            : new[] { subjectId, actorId })
            .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToArray();
        var inactive = await db.UserProfiles.AsNoTracking()
            .AnyAsync(profile => ids.Contains(profile.UserId) && !profile.IsActive);
        if (inactive) context.Fail("The user account is inactive.");

        // A borrowed token is only good while its borrow is alive. Ending it (or
        // letting the seven minutes run out) kills the token on the next request,
        // instead of leaving it usable until it expires on its own.
        if (actorId is not null)
        {
            var sessionId = principal.FindFirst("imp_sid")?.Value;
            // Token validation runs before HttpContext.User is populated; the filter cannot read org yet.
            var live = !string.IsNullOrWhiteSpace(sessionId) && await db.ImpersonationSessions.IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(session => session.Id == sessionId
                    && session.TenantId == organizationId
                    && session.ActorUserId == actorId
                    && session.EndedAtUtc == null
                    && session.ExpiresAtUtc > DateTime.UtcNow);
            if (!live) context.Fail("The impersonation has ended.");
        }

        // A rotated security stamp (password reset, forced sign-out) has to kill the
        // tokens that were minted against the old one. Without this a reset revokes
        // the refresh tokens and leaves every live access token working.
        //
        // The stamp is required, not optional: `stamp is not null` made its absence a
        // way around the check, so a token written without the claim — by hand, with
        // a leaked key — survived every reset. Every token this API mints carries it.
        var stamp = principal.FindFirst("sstamp")?.Value;
        if (string.IsNullOrEmpty(stamp) || string.IsNullOrEmpty(subjectId))
        {
            context.Fail("The session is no longer valid.");
        }
        else
        {
            var users = context.HttpContext.RequestServices
                .GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Microsoft.AspNetCore.Identity.IdentityUser>>();
            var subject = await users.FindByIdAsync(subjectId);
            if (subject is null
                || !string.Equals(subject.SecurityStamp, stamp, StringComparison.Ordinal))
                context.Fail("The session is no longer valid.");
        }
    }
}
