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

    public AuthController(SignInManager<IdentityUser> signIn, UserManager<IdentityUser> users,
        ApplicationContext context, AuthTokenService tokens, IHostEnvironment environment)
    {
        _signIn = signIn;
        _users = users;
        _context = context;
        _tokens = tokens;
        _environment = environment;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrEmpty(request.Password)) return Unauthorized();
        var user = await _users.FindByEmailAsync(request.Email);
        if (user is null || !(await _signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true)).Succeeded)
            return Unauthorized();

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
        if (stored is null || stored.RevokedAtUtc is not null || stored.ExpiresAtUtc <= now)
        {
            DeleteRefreshCookie();
            return Unauthorized();
        }

        var user = await _users.FindByIdAsync(stored.UserId);
        if (user is null)
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
        if (Request.Cookies.TryGetValue(RefreshCookie, out var refresh) && !string.IsNullOrWhiteSpace(refresh))
        {
            var hash = AuthTokenService.HashRefreshToken(refresh);
            var stored = await _context.RefreshTokens.SingleOrDefaultAsync(token => token.TokenHash == hash);
            if (stored is not null && stored.RevokedAtUtc is null)
            {
                stored.RevokedAtUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }
        DeleteRefreshCookie();
        return NoContent();
    }

    private void SetRefreshCookie(string token, DateTime expires) => Response.Cookies.Append(RefreshCookie, token,
        new CookieOptions { HttpOnly = true, Secure = Request.IsHttps || !_environment.IsDevelopment(), SameSite = SameSiteMode.Lax, Path = "/api/auth", Expires = expires });

    private void DeleteRefreshCookie() => Response.Cookies.Delete(RefreshCookie,
        new CookieOptions { HttpOnly = true, Secure = Request.IsHttps || !_environment.IsDevelopment(), SameSite = SameSiteMode.Lax, Path = "/api/auth" });

    public sealed record LoginRequest(string Email, string Password);
}
