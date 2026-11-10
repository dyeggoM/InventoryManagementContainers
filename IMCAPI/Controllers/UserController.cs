using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
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
[Authorize(Policy = "RequiereSuperadmin")]
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
        var created = await _users.CreateAsync(user);
        if (!created.Succeeded) return BadRequest(new { errors = created.Errors.Select(item => item.Description) });

        _context.UserProfiles.Add(new UserProfile { UserId = user.Id, IsActive = true });
        await _context.SaveChangesAsync(cancellationToken);
        if (roles.Length > 0)
        {
            var assigned = await _users.AddToRolesAsync(user, roles);
            if (!assigned.Succeeded) return BadRequest(new { errors = assigned.Errors.Select(item => item.Description) });
        }
        await transaction.CommitAsync(cancellationToken);
        return Created($"/api/users/{user.Id}", await ToSummaryAsync(user));
    }

    [HttpPut("{id}/roles")]
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
        await _audit.WriteAsync("RoleChange", "IdentityUser", id,
            beforeJson: JsonSerializer.Serialize(currentRoles, AuditJson),
            afterJson: JsonSerializer.Serialize(nextRoles, AuditJson), cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id}")]
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

    [HttpPost("{id}/impersonate")]
    public async Task<IActionResult> Impersonate(string id, CancellationToken cancellationToken)
    {
        var actorId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        var actor = actorId is null ? null : await _users.FindByIdAsync(actorId);
        if (actor is null || !await _users.IsInRoleAsync(actor, "superadmin")) return Forbid();
        var target = await _users.FindByIdAsync(id);
        if (target is null) return NotFound();
        if (await _users.IsInRoleAsync(target, "superadmin"))
            return Conflict(new { error = "The superadmin account cannot be impersonated." });
        var profile = await _context.UserProfiles.AsNoTracking().SingleOrDefaultAsync(row => row.UserId == id, cancellationToken);
        if (profile is { IsActive: false }) return Conflict(new { error = "An inactive account cannot be impersonated." });

        await _audit.WriteAsync("ImpersonateStart", "IdentityUser", id, actor.Id, actor.Email,
            cancellationToken, impersonatedUserId: id);
        var access = await _tokens.CreateImpersonationTokenAsync(target, actor);
        return Ok(new { access_token = access.Token, expires_at_utc = access.ExpiresAtUtc });
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

    public sealed record CreateUserRequest([Required, EmailAddress] string Email, [Required] string[] Roles);
    public sealed record UpdateRolesRequest([Required] string[] Roles);
    public sealed record UserSummary(string Id, string Email, IList<string> Roles, bool IsActive,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] DateTime? LastAccessUtc);
}
