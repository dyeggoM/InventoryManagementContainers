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
public sealed class AuthController : ControllerBase
{
    private const string RefreshCookie = "imc_refresh";
    private readonly SignInManager<IdentityUser> _signIn;
    private readonly UserManager<IdentityUser> _users;
    private readonly ApplicationContext _context;
    private readonly AuthTokenService _tokens;
    private readonly IHostEnvironment _environment;
    private readonly AuditLogWriter _audit;

    public AuthController(SignInManager<IdentityUser> signIn, UserManager<IdentityUser> users,
        ApplicationContext context, AuthTokenService tokens, IHostEnvironment environment, AuditLogWriter audit)
    {
        _signIn = signIn;
        _users = users;
        _context = context;
        _tokens = tokens;
        _environment = environment;
        _audit = audit;
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
            await _audit.WriteAsync("RefreshReuse", "RefreshToken", stored.Id.ToString(), stored.UserId, previousUser?.Email);
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
        if (user is null || profile is { IsActive: false })
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
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(7),
        });
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();
        SetRefreshCookie(replacement, now.AddDays(7));
        var access = await _tokens.CreateAccessTokenAsync(user);
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

    [Authorize]
    [HttpPost("stop-impersonation")]
    public async Task<IActionResult> StopImpersonation(CancellationToken cancellationToken)
    {
        var targetId = User.FindFirst("impersonated_user_id")?.Value
            ?? User.FindFirst("impersonatedUserId")?.Value;
        var actorId = User.FindFirst("act")?.Value;
        var actorEmail = User.FindFirst("act_email")?.Value;
        if (string.IsNullOrWhiteSpace(targetId) || string.IsNullOrWhiteSpace(actorId)
            || string.IsNullOrWhiteSpace(actorEmail)) return Forbid();

        var actor = await _users.FindByIdAsync(actorId);
        if (actor is null || !string.Equals(actor.Email, actorEmail, StringComparison.OrdinalIgnoreCase)
            || !await _users.IsInRoleAsync(actor, "superadmin")) return Forbid();
        var profile = await _context.UserProfiles.AsNoTracking().SingleOrDefaultAsync(row => row.UserId == actorId, cancellationToken);
        if (profile is { IsActive: false }) return Forbid();

        await _audit.WriteAsync("ImpersonateEnd", "IdentityUser", targetId, actor.Id, actor.Email,
            cancellationToken, impersonatedUserId: targetId);
        var access = await _tokens.CreateAccessTokenAsync(actor);
        return Ok(new { access_token = access.Token, expires_at_utc = access.ExpiresAtUtc });
    }

    private void SetRefreshCookie(string token, DateTime expires) => Response.Cookies.Append(RefreshCookie, token,
        new CookieOptions { HttpOnly = true, Secure = Request.IsHttps || !_environment.IsDevelopment(), SameSite = SameSiteMode.Lax, Path = "/api/auth", Expires = expires });

    private void DeleteRefreshCookie() => Response.Cookies.Delete(RefreshCookie,
        new CookieOptions { HttpOnly = true, Secure = Request.IsHttps || !_environment.IsDevelopment(), SameSite = SameSiteMode.Lax, Path = "/api/auth" });

    public sealed record LoginRequest(string Email, string Password);
}
