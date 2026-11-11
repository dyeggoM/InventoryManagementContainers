using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using IMCAPI.DB;
using IMCAPI.DB.Entities;
using IMCAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IMCAPI.Controllers;

[ApiController]
[Route("api/users")]
public sealed class UserController : ControllerBase
{
    private static readonly string[] AssignableRoles = ["admin", "operador", "lector"];
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);
    private readonly UserManager<IdentityUser> _users;
    private readonly RoleManager<IdentityRole> _roles;
    private readonly ApplicationContext _context;
    private readonly AuthTokenService _tokens;
    private readonly AuditLogWriter _audit;

    public UserController(UserManager<IdentityUser> users, RoleManager<IdentityRole> roles,
        ApplicationContext context, AuthTokenService tokens, AuditLogWriter audit)
    {
        _users = users;
        _roles = roles;
        _context = context;
        _tokens = tokens;
        _audit = audit;
    }

    [HttpGet]
    [Authorize(Policy = "RequiereSuperadmin")]
    public async Task<IActionResult> GetUsers(CancellationToken cancellationToken)
    {
        var users = await _users.Users.OrderBy(user => user.Email).ToListAsync(cancellationToken);
        var profiles = await _context.UserProfiles.AsNoTracking().ToDictionaryAsync(profile => profile.UserId, cancellationToken);
        var items = new List<UserSummary>(users.Count);
        foreach (var user in users)
        {
            profiles.TryGetValue(user.Id, out var profile);
            items.Add(new UserSummary(user.Id, user.Email ?? "", await _users.GetRolesAsync(user),
                profile?.IsActive ?? true, profile?.LastAccessUtc));
        }
        return Ok(new { items });
    }

    [HttpPost]
    [Authorize(Policy = "RequiereSuperadmin")]
    public async Task<IActionResult> CreateUser(CreateUserRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim();
        if (string.IsNullOrWhiteSpace(email)) return BadRequest(new { error = "Email is required." });
        if (await _users.FindByEmailAsync(email) is not null) return Conflict(new { error = "Email is already in use." });
        var roleValidation = await ValidateRolesAsync(request.Roles);
        if (roleValidation.Error is not null) return BadRequest(new { error = roleValidation.Error });
        var roles = roleValidation.Roles;

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = false };
        var initialPassword = request.Password ?? GeneratePassword();
        var created = await _users.CreateAsync(user, initialPassword);
        if (!created.Succeeded) return BadRequest(new { errors = created.Errors.Select(item => item.Description) });

        _context.UserProfiles.Add(new UserProfile { UserId = user.Id, IsActive = true });
        await _context.SaveChangesAsync(cancellationToken);
        if (roles.Length > 0)
        {
            var assigned = await _users.AddToRolesAsync(user, roles);
            if (!assigned.Succeeded) return BadRequest(new { errors = assigned.Errors.Select(item => item.Description) });
        }
        await transaction.CommitAsync(cancellationToken);
        var summary = await ToSummaryAsync(user);
        return Created($"/api/users/{user.Id}", new CreatedUser(summary.Id, summary.Email, summary.Roles,
            summary.IsActive, summary.LastAccessUtc, request.Password is null ? initialPassword : null));
    }

    [HttpPut("{id}/roles")]
    [Authorize(Policy = "RequiereSuperadmin")]
    public async Task<IActionResult> UpdateRoles(string id, UpdateRolesRequest request, CancellationToken cancellationToken)
    {
        var roleValidation = await ValidateRolesAsync(request.Roles);
        if (roleValidation.Error is not null) return BadRequest(new { error = roleValidation.Error });
        var nextRoles = roleValidation.Roles;
        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.Equals(id, actorId, StringComparison.Ordinal))
            return BadRequest(new { error = "A superadmin cannot change their own roles." });

        var user = await _users.FindByIdAsync(id);
        if (user is null) return NotFound();
        var currentRoles = await _users.GetRolesAsync(user);
        if (currentRoles.Contains("superadmin", StringComparer.OrdinalIgnoreCase))
        {
            var superadmins = await _users.GetUsersInRoleAsync("superadmin");
            if (superadmins.Count <= 1 || !nextRoles.Contains("superadmin", StringComparer.OrdinalIgnoreCase))
                return Conflict(new { error = "The only superadmin cannot be degraded." });
        }

        var removed = currentRoles.Except(nextRoles, StringComparer.OrdinalIgnoreCase).ToArray();
        var added = nextRoles.Except(currentRoles, StringComparer.OrdinalIgnoreCase).ToArray();
        if (removed.Length == 0 && added.Length == 0) return NoContent();
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        if (removed.Length > 0)
        {
            var result = await _users.RemoveFromRolesAsync(user, removed);
            if (!result.Succeeded) return BadRequest(new { errors = result.Errors.Select(item => item.Description) });
        }
        if (added.Length > 0)
        {
            var result = await _users.AddToRolesAsync(user, added);
            if (!result.Succeeded) return BadRequest(new { errors = result.Errors.Select(item => item.Description) });
        }
        // A role change has to reach the sessions that are already open, or a demotion only takes
        // effect once the holder happens to re-login. Rotating the stamp is what the token pipeline
        // compares, so the token minted with the old roles answers 401 from here on.
        var stamp = await _users.UpdateSecurityStampAsync(user);
        if (!stamp.Succeeded) return BadRequest(new { errors = stamp.Errors.Select(item => item.Description) });
        await _audit.WriteAsync("RoleChange", "IdentityUser", id,
            beforeJson: JsonSerializer.Serialize(currentRoles, AuditJson),
            afterJson: JsonSerializer.Serialize(nextRoles, AuditJson), cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id}")]
    [Authorize(Policy = "RequiereSuperadmin")]
    public async Task<IActionResult> DeactivateUser(string id, CancellationToken cancellationToken)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null) return NotFound();
        var roles = await _users.GetRolesAsync(user);
        if (roles.Contains("superadmin", StringComparer.OrdinalIgnoreCase))
        {
            var superadmins = await _users.GetUsersInRoleAsync("superadmin");
            if (superadmins.Count <= 1) return Conflict(new { error = "The only superadmin cannot be deactivated." });
        }
        var profile = await _context.UserProfiles.SingleOrDefaultAsync(row => row.UserId == id, cancellationToken);
        if (profile is null)
        {
            profile = new UserProfile { UserId = id, IsActive = false };
            _context.UserProfiles.Add(profile);
        }
        else
        {
            profile.IsActive = false;
        }
        var now = DateTime.UtcNow;
        var refreshTokens = await _context.RefreshTokens.Where(token => token.UserId == id && token.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var refreshToken in refreshTokens) refreshToken.RevokedAtUtc = now;
        await _context.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("impersonate")]
    [Authorize]
    public async Task<IActionResult> Impersonate(ImpersonateRequest request, CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated != true) return Unauthorized();
        if (User.HasClaim(claim => claim.Type == "act")) return Forbid();
        var role = request.Role?.Trim().ToLowerInvariant();
        if (role is not ("admin" or "operador" or "lector")) return BadRequest(new { error = "Invalid target role." });
        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        var actor = actorId is null ? null : await _users.FindByIdAsync(actorId);
        if (actor is null) return Forbid();
        var callerRole = new[] { "superadmin", "admin", "operador", "lector" }
            .FirstOrDefault(candidate => User.IsInRole(candidate));
        if (callerRole is null || callerRole == "lector") return Forbid();
        var hierarchy = new[] { "superadmin", "admin", "operador", "lector" };
        if (Array.IndexOf(hierarchy, role) <= Array.IndexOf(hierarchy, callerRole)) return BadRequest(new { error = "Target role must be below caller role." });
        await _audit.WriteAsync("ImpersonateStart", "UserRole", role, actor.Id, actor.Email, cancellationToken);
        var access = await _tokens.CreateImpersonationTokenAsync(actor, role);
        return Ok(new { access_token = access.Token, expires_at_utc = access.ExpiresAtUtc, role, scope = "read", impersonated_by = new { id = actor.Id, email = actor.Email } });
    }

    [HttpPost("{id}/password")]
    [Authorize(Policy = "RequiereSuperadmin")]
    public async Task<IActionResult> ResetPassword(string id, PasswordRequest? request, CancellationToken cancellationToken)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null) return NotFound();
        var password = request?.Password ?? GeneratePassword();
        var reset = await _users.GeneratePasswordResetTokenAsync(user);
        var result = await _users.ResetPasswordAsync(user, reset, password);
        if (!result.Succeeded) return BadRequest(new { errors = result.Errors.Select(error => error.Description) });
        var now = DateTime.UtcNow;
        var active = await _context.RefreshTokens.Where(token => token.UserId == id && token.RevokedAtUtc == null).ToListAsync(cancellationToken);
        foreach (var token in active) token.RevokedAtUtc = now;
        await _context.SaveChangesAsync(cancellationToken);
        await _audit.WriteAsync("PasswordReset", "IdentityUser", id, cancellationToken: cancellationToken);
        return Ok(new { initialPassword = request?.Password is null ? password : null });
    }

    private async Task<(string[] Roles, string? Error)> ValidateRolesAsync(string[]? requested)
    {
        if (requested is null)
            return ([], "Roles are required.");
        if (requested.Any(role => string.Equals(role?.Trim(), "superadmin", StringComparison.OrdinalIgnoreCase)))
            return ([], "The superadmin role can only be assigned by the bootstrap process.");
        var normalized = requested.Select(role => role?.Trim().ToLowerInvariant() ?? "").ToArray();
        if (normalized.Any(role => !AssignableRoles.Contains(role, StringComparer.Ordinal)))
            return ([], "One or more roles are invalid.");
        var roles = normalized.Distinct(StringComparer.Ordinal).ToArray();
        foreach (var role in roles)
            if (await _roles.FindByNameAsync(role) is null)
                return ([], "One or more roles are invalid.");
        return (roles, null);
    }

    private async Task<UserSummary> ToSummaryAsync(IdentityUser user)
    {
        var profile = await _context.UserProfiles.AsNoTracking().SingleOrDefaultAsync(row => row.UserId == user.Id);
        return new UserSummary(user.Id, user.Email ?? "", await _users.GetRolesAsync(user),
            profile?.IsActive ?? true, profile?.LastAccessUtc);
    }

    private static string GeneratePassword()
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string symbols = "!@#$%&*?";
        const string all = upper + lower + digits + symbols;
        var characters = new char[24];
        // One character from every class first: Identity rejects a password that misses one, and a
        // purely random draw over the whole alphabet misses a symbol roughly one run in eight.
        characters[0] = Pick(upper);
        characters[1] = Pick(lower);
        characters[2] = Pick(digits);
        characters[3] = Pick(symbols);
        for (var index = 4; index < characters.Length; index++) characters[index] = Pick(all);
        for (var index = characters.Length - 1; index > 0; index--)
        {
            var swap = RandomNumberGenerator.GetInt32(index + 1);
            (characters[index], characters[swap]) = (characters[swap], characters[index]);
        }
        return new string(characters);

        static char Pick(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
    }

    public sealed record CreateUserRequest([Required, EmailAddress] string Email, [Required] string[] Roles, string? Password = null);
    public sealed record PasswordRequest(string? Password = null);
    public sealed record ImpersonateRequest(string? Role);
    public sealed record CreatedUser(string Id, string Email, IList<string> Roles, bool IsActive,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTime? LastAccessUtc,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? InitialPassword);
    public sealed record UpdateRolesRequest([Required] string[] Roles);
    public sealed record UserSummary(string Id, string Email, IList<string> Roles, bool IsActive,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTime? LastAccessUtc);
}
