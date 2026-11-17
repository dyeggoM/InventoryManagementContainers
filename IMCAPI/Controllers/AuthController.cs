using IMCAPI.DB;
using IMCAPI.DB.Entities;
using IMCAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IMCAPI.Controllers;

[ApiController]
[Route("api/auth")]
[Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("auth")]
public sealed class AuthController : ControllerBase
{
    private const string RefreshCookie = "imc_refresh";
    private readonly SignInManager<IdentityUser> _signIn;
    private readonly UserManager<IdentityUser> _users;
    private readonly ApplicationContext _context;
    private readonly AuthTokenService _tokens;
    private readonly IHostEnvironment _environment;
    private readonly AuditLogWriter _audit;
    private readonly TenantScope _tenant;

    public AuthController(SignInManager<IdentityUser> signIn, UserManager<IdentityUser> users,
        ApplicationContext context, AuthTokenService tokens, IHostEnvironment environment, AuditLogWriter audit,
        TenantScope tenant)
    {
        _signIn = signIn;
        _users = users;
        _context = context;
        _tokens = tokens;
        _environment = environment;
        _audit = audit;
        _tenant = tenant;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password))
        {
            await _audit.WriteAsync("LoginFailed", "IdentityUser", actorEmail: request.Email);
            return Unauthorized();
        }
        var user = await _users.FindByEmailAsync(request.Email);
        if (user is null || !(await _signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true)).Succeeded)
        {
            await _audit.WriteAsync("LoginFailed", "IdentityUser", user?.Id, actorEmail: request.Email);
            return Unauthorized();
        }
        var profile = await _context.UserProfiles.SingleOrDefaultAsync(row => row.UserId == user.Id);
        if (profile is { IsActive: false })
        {
            await _audit.WriteAsync("LoginFailed", "IdentityUser", user.Id, actorEmail: request.Email);
            return Unauthorized();
        }
        if (profile is null)
        {
            profile = new UserProfile { UserId = user.Id, IsActive = true };
            _context.UserProfiles.Add(profile);
        }
        profile.LastAccessUtc = DateTime.UtcNow;
        await _context.SaveChangesAsync();
        await _audit.WriteAsync("Login", "IdentityUser", user.Id, user.Id, user.Email);

        var refresh = AuthTokenService.NewRefreshToken();
        var now = DateTime.UtcNow;
        _context.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = AuthTokenService.HashRefreshToken(refresh),
            TenantId = OrganizationConfiguration.DefaultTenantId,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(7),
        });
        await _context.SaveChangesAsync();
        SetRefreshCookie(refresh, now.AddDays(7));
        var access = await _tokens.CreateAccessTokenAsync(user);
        return Ok(new { access_token = access.Token, expires_at_utc = access.ExpiresAtUtc });
    }

    [AllowAnonymous]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        if (!Request.Cookies.TryGetValue(RefreshCookie, out var refresh) || string.IsNullOrWhiteSpace(refresh)) return Unauthorized();
        var now = DateTime.UtcNow;
        var hash = AuthTokenService.HashRefreshToken(refresh);
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var stored = await _context.RefreshTokens.SingleOrDefaultAsync(token => token.TokenHash == hash);
        if (stored?.RevokedAtUtc is not null && stored.ReplacedByTokenHash is not null)
        {
            var previousUser = await _users.FindByIdAsync(stored.UserId);
            // Presenting a retired token means the cookie was copied somewhere. Auditing the reuse
            // while leaving the replacement alive gives the audit trail a confession nobody acts on:
            // only this organization's live chain dies; independent organization sessions survive.
            var family = await _context.RefreshTokens
                .Where(token => token.UserId == stored.UserId && token.TenantId == stored.TenantId
                    && token.RevokedAtUtc == null)
                .ToListAsync();
            foreach (var token in family) token.RevokedAtUtc = now;
            await _audit.WriteAsync("RefreshReuse", "RefreshToken", stored.Id.ToString(), stored.UserId, previousUser?.Email);
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            DeleteRefreshCookie();
            return Unauthorized();
        }
        if (stored is null || stored.RevokedAtUtc is not null || stored.ExpiresAtUtc <= now)
        {
            DeleteRefreshCookie();
            return Unauthorized();
        }

        var user = await _users.FindByIdAsync(stored.UserId);
        var profile = user is null ? null : await _context.UserProfiles.SingleOrDefaultAsync(row => row.UserId == user.Id);
        if (user is null || profile is { IsActive: false }
            || !await _context.Constants.IgnoreQueryFilters().AnyAsync(row => row.Id == stored.TenantId
                && row.Group == "Organization" && row.TenantId == 0 && row.IsActive))
        {
            stored.RevokedAtUtc = now;
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
            DeleteRefreshCookie();
            return Unauthorized();
        }

        var replacement = AuthTokenService.NewRefreshToken();
        var replacementHash = AuthTokenService.HashRefreshToken(replacement);
        stored.RevokedAtUtc = now;
        stored.ReplacedByTokenHash = replacementHash;
        _context.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = replacementHash,
            TenantId = stored.TenantId,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(7),
        });
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
        SetRefreshCookie(replacement, now.AddDays(7));
        var access = await _tokens.CreateAccessTokenAsync(user, stored.TenantId);
        return Ok(new { access_token = access.Token, expires_at_utc = access.ExpiresAtUtc });
    }

    [Authorize]
    [HttpPost("switch-organization")]
    public async Task<IActionResult> SwitchOrganization(SwitchOrganizationRequest request)
    {
        if (User.HasClaim(claim => claim.Type == "act")) return Forbid();
        // Switching changes the token: admin can write in the target too because roles are global.
        // UserOrganization membership enables per-organization read-only rights and replaces this boundary.
        if (!User.IsInRole("admin") && !User.IsInRole("superadmin")) return Forbid();
        var next = await _context.Constants.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(row =>
            row.Id == request.OrganizationId && row.Group == "Organization" && row.TenantId == 0 && row.IsActive);
        if (next is null) return NotFound();
        var oldId = _context.CurrentTenantId;
        var old = await _context.Constants.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == oldId && row.Group == "Organization" && row.TenantId == 0);
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        var user = userId is null ? null : await _users.FindByIdAsync(userId);
        if (user is null || !Request.Cookies.TryGetValue(RefreshCookie, out var refresh)) return Unauthorized();
        var now = DateTime.UtcNow;
        await using var transaction = await _context.Database.BeginTransactionAsync();
        var stored = await _context.RefreshTokens.SingleOrDefaultAsync(row =>
            row.TokenHash == AuthTokenService.HashRefreshToken(refresh) && row.UserId == user.Id
            && row.TenantId == oldId && row.RevokedAtUtc == null && row.ExpiresAtUtc > now);
        if (stored is null) return Unauthorized();
        var replacement = AuthTokenService.NewRefreshToken();
        var hash = AuthTokenService.HashRefreshToken(replacement);
        var closed = await _context.RefreshTokens
            .Where(token => token.Id == stored.Id && token.RevokedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(token => token.RevokedAtUtc, now));
        if (closed == 0) return Unauthorized(); // Another request already rotated this chain.
        stored.ReplacedByTokenHash = hash;
        _context.RefreshTokens.Add(new RefreshToken
        {
            TenantId = next.Id, UserId = user.Id, TokenHash = hash,
            CreatedAtUtc = now, ExpiresAtUtc = now.AddDays(7)
        });
        // Persist the rotation before auditing it: a future audit change cannot strand the new cookie.
        await _context.SaveChangesAsync();
        var beforeJson = System.Text.Json.JsonSerializer.Serialize(new { id = oldId, label = old?.Label });
        var afterJson = System.Text.Json.JsonSerializer.Serialize(new { id = next.Id, label = next.Label });
        await _audit.WriteAsync("OrganizationSwitch", "Organization", next.Id.ToString(), user.Id, user.Email,
            beforeJson: beforeJson, afterJson: afterJson);
        if (next.Id != oldId)
        {
            // Both trails need the same departure/arrival: the old token alone would log only the exit.
            using (_tenant.SetWriteTenant(next.Id))
                await _audit.WriteAsync(AuditActions.OrganizationSwitch, "Organization", next.Id.ToString(), user.Id, user.Email,
                    beforeJson: beforeJson, afterJson: afterJson);
        }
        await transaction.CommitAsync();
        SetRefreshCookie(replacement, now.AddDays(7));
        var access = await _tokens.CreateAccessTokenAsync(user, next.Id);
        return Ok(new { access_token = access.Token, expires_at_utc = access.ExpiresAtUtc });
    }

    [AllowAnonymous]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        string? actorId = null;
        string? actorEmail = null;
        if (Request.Cookies.TryGetValue(RefreshCookie, out var refresh) && !string.IsNullOrWhiteSpace(refresh))
        {
            var hash = AuthTokenService.HashRefreshToken(refresh);
            var stored = await _context.RefreshTokens.SingleOrDefaultAsync(token => token.TokenHash == hash);
            if (stored is not null && stored.RevokedAtUtc is null)
            {
                actorId = stored.UserId;
                actorEmail = (await _users.FindByIdAsync(stored.UserId))?.Email;
                stored.RevokedAtUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }
        DeleteRefreshCookie();
        await _audit.WriteAsync("Logout", "IdentityUser", actorId, actorId, actorEmail);
        return NoContent();
    }

    /// <summary>
    /// Ends the borrow. Two separate things happen here, and they are deliberately not the same:
    ///
    /// - the session row is closed, which is what makes the borrowed token stop working immediately
    ///   (the middleware rejects a token whose <c>imp_sid</c> is not live) and is idempotent-safe: a
    ///   borrow can only be ended once;
    /// - the actor's own token is handed back **only** with proof that the caller is the actor's
    ///   session (the refresh cookie, hashed and live). Without that proof the borrow is still closed
    ///   and the answer is 204 — a client that lost its cookie is logged out, never upgraded.
    /// </summary>
    [Authorize]
    [HttpPost("stop-impersonation")]
    public async Task<IActionResult> StopImpersonation(CancellationToken cancellationToken)
    {
        var targetRole = User.FindFirst("impersonated_role")?.Value;
        var actorId = User.FindFirst("act")?.Value;
        var actorEmail = User.FindFirst("act_email")?.Value;
        var sessionId = User.FindFirst("imp_sid")?.Value;
        if (string.IsNullOrWhiteSpace(targetRole) || string.IsNullOrWhiteSpace(actorId)
            || string.IsNullOrWhiteSpace(actorEmail) || string.IsNullOrWhiteSpace(sessionId)) return Forbid();

        var actor = await _users.FindByIdAsync(actorId);
        if (actor is null || !string.Equals(actor.Email, actorEmail, StringComparison.OrdinalIgnoreCase)) return Forbid();
        var profile = await _context.UserProfiles.AsNoTracking().SingleOrDefaultAsync(row => row.UserId == actorId, cancellationToken);
        if (profile is { IsActive: false }) return Forbid();

        // Closing the borrow is one conditional statement, not a read followed by a write: two stops
        // arriving together (a double click, a retry, two tabs) both used to see it open, and both
        // wrote an ImpersonateEnd for the same borrow. The database decides who closes it.
        var ended = await _context.ImpersonationSessions
            .Where(row => row.Id == sessionId && row.ActorUserId == actorId && row.EndedAtUtc == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(row => row.EndedAtUtc, DateTime.UtcNow)
                .SetProperty(row => row.EndedBy, "api"), cancellationToken);
        if (ended == 0) return Forbid();

        await _audit.WriteAsync("ImpersonateEnd", "UserRole", targetRole, actor.Id, actor.Email,
            cancellationToken, afterJson: System.Text.Json.JsonSerializer.Serialize(
                new { role = targetRole, impersonation_session = sessionId }),
            impersonationSessionId: sessionId);

        // The proof of being the actor: their own refresh cookie, alive and unrevoked. A holder of the
        // borrowed token alone has nothing to show here, which is the whole point.
        if (!Request.Cookies.TryGetValue(RefreshCookie, out var actorRefresh) || string.IsNullOrWhiteSpace(actorRefresh))
            return NoContent();
        var actorHash = AuthTokenService.HashRefreshToken(actorRefresh);
        var ownsSession = await _context.RefreshTokens.AnyAsync(
            token => token.TokenHash == actorHash && token.UserId == actor.Id
                && token.RevokedAtUtc == null && token.ExpiresAtUtc > DateTime.UtcNow,
            cancellationToken);
        if (!ownsSession) return NoContent();

        var access = await _tokens.CreateAccessTokenAsync(actor, _context.CurrentTenantId);
        return Ok(new { access_token = access.Token, expires_at_utc = access.ExpiresAtUtc });
    }

    private void SetRefreshCookie(string token, DateTime expires) => Response.Cookies.Append(RefreshCookie, token,
        new CookieOptions { HttpOnly = true, Secure = Request.IsHttps || !_environment.IsDevelopment(), SameSite = SameSiteMode.Lax, Path = "/api/auth", Expires = expires });

    private void DeleteRefreshCookie() => Response.Cookies.Delete(RefreshCookie,
        new CookieOptions { HttpOnly = true, Secure = Request.IsHttps || !_environment.IsDevelopment(), SameSite = SameSiteMode.Lax, Path = "/api/auth" });

    public sealed record LoginRequest(string Email, string Password);
    public sealed record SwitchOrganizationRequest(int OrganizationId);
}
