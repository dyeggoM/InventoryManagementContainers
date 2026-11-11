using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace IMCAPI.Services;

public sealed class AuthTokenService
{
    private readonly UserManager<IdentityUser> _users;
    private readonly IConfiguration _configuration;

    public AuthTokenService(UserManager<IdentityUser> users, IConfiguration configuration)
    {
        _users = users;
        _configuration = configuration;
    }

    public async Task<(string Token, DateTime ExpiresAtUtc)> CreateAccessTokenAsync(IdentityUser user)
    {
        return await CreateTokenAsync(user, actor: null, targetRole: null, impersonation: false);
    }

    public async Task<(string Token, DateTime ExpiresAtUtc)> CreateImpersonationTokenAsync(
        IdentityUser actor, string role)
    {
        return await CreateTokenAsync(actor, actor, role, impersonation: true);
    }

    private async Task<(string Token, DateTime ExpiresAtUtc)> CreateTokenAsync(
        IdentityUser user, IdentityUser? actor, string? targetRole, bool impersonation)
    {
        var key = GetSetting("Auth:Jwt:Key", "AUTH_JWT_KEY");
        var issuer = GetSetting("Auth:Jwt:Issuer", "AUTH_JWT_ISSUER");
        var expires = DateTime.UtcNow.AddMinutes(impersonation ? 7 : 15);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Name, user.UserName ?? user.Email ?? user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            // The stamp is the server's lever over a live session: rotating it (a password reset) has
            // to invalidate the tokens already handed out, or a reset would only stop the next login.
            new("sstamp", user.SecurityStamp ?? ""),
        };
        if (impersonation && actor is not null)
        {
            claims.Add(new Claim("act", actor.Id));
            claims.Add(new Claim("act_email", actor.Email ?? ""));
            claims.Add(new Claim("impersonated_role", targetRole!));
            claims.Add(new Claim("scope", "read"));
        }
        claims.AddRange((impersonation ? [targetRole!] : await _users.GetRolesAsync(user))
            .Select(role => new Claim(ClaimTypes.Role, role)));
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var token = new JwtSecurityToken(issuer, issuer, claims, expires: expires,
            signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    public static string NewRefreshToken() => Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(64));

    public static string HashRefreshToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private string GetSetting(string configKey, string environmentKey) =>
        _configuration[configKey] ?? Environment.GetEnvironmentVariable(environmentKey)
        ?? throw new InvalidOperationException($"Missing required setting '{configKey}'.");
}
