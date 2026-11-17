using System.Net;
using System.Security.Claims;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using IMCAPI.DB;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Testing;

[NonParallelizable]
[TestFixture]
public sealed class UserAdminTests : ApiTestBase
{
    protected override bool SignsInAtStartup => false;

    [Test]
    public async Task Superadmin_CreatesAndListsUser_AndDuplicateEmailConflicts()
    {
        using var client = await LoginAsSuperadmin();
        var email = $"created-{Guid.NewGuid():N}@example.test";

        var created = await client.PostAsJsonAsync("/api/users", new { email, roles = new[] { "lector" } });
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created), await created.Content.ReadAsStringAsync());
        var payload = await created.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Multiple(() =>
        {
            Assert.That(payload.GetProperty("email").GetString(), Is.EqualTo(email));
            Assert.That(payload.GetProperty("roles").EnumerateArray().Select(role => role.GetString()), Does.Contain("lector"));
            Assert.That(payload.GetProperty("isActive").GetBoolean(), Is.True);
            Assert.That(payload.TryGetProperty("lastAccessUtc", out _), Is.True);
            Assert.That(payload.GetProperty("initialPassword").GetString(), Has.Length.EqualTo(24));
        });
        var id = payload.GetProperty("id").GetString()!;

        var duplicate = await client.PostAsJsonAsync("/api/users", new { email = email.ToUpperInvariant(), roles = Array.Empty<string>() });
        Assert.That(duplicate.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));

        var list = await client.GetFromJsonAsync<JsonElement>("/api/users", Json);
        Assert.That(list.GetProperty("items").EnumerateArray().Any(user => user.GetProperty("id").GetString() == id), Is.True);
        using var scope = PersonaScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        Assert.That(await context.AuditLogs.AnyAsync(row => row.Entity == "IdentityUser" && row.EntityId == id && row.Action == "Create"), Is.True);
        Assert.That(await context.AuditLogs.AnyAsync(row => row.Action == "RoleChange" && row.EntityId == id), Is.True);
    }

    [Test]
    public async Task RoleChanges_RejectSelfAndSuperadminAssignment_AndAreAudited()
    {
        using var client = await LoginAsSuperadmin();
        using var scope = PersonaScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var root = await users.FindByEmailAsync("bootstrap@example.test");
        Assert.That(root, Is.Not.Null);

        var self = await client.PutAsJsonAsync($"/api/users/{root!.Id}/roles", new { roles = new[] { "lector" } });
        Assert.That(self.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var superadmin = await client.PostAsJsonAsync("/api/users", new { email = $"promote-{Guid.NewGuid():N}@example.test", roles = new[] { "superadmin" } });
        Assert.That(superadmin.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var targetEmail = $"role-target-{Guid.NewGuid():N}@example.test";
        var created = await client.PostAsJsonAsync("/api/users", new { email = targetEmail, roles = Array.Empty<string>() });
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString();
        var changed = await client.PutAsJsonAsync($"/api/users/{id}/roles", new { roles = new[] { "operador", "lector" } });
        Assert.That(changed.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), await changed.Content.ReadAsStringAsync());
        var forbiddenRole = await client.PutAsJsonAsync($"/api/users/{id}/roles", new { roles = new[] { "superadmin" } });
        Assert.That(forbiddenRole.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await scope.ServiceProvider.GetRequiredService<ApplicationContext>().AuditLogs
            .AnyAsync(row => row.Action == "RoleChange" && row.EntityId == id), Is.True);
    }

    [Test]
    public async Task Delete_SoftDeactivatesUserAndCannotDeleteSoleSuperadmin()
    {
        using var client = await LoginAsSuperadmin();
        using var scope = PersonaScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var root = await users.FindByEmailAsync("bootstrap@example.test");
        var rootDelete = await client.DeleteAsync($"/api/users/{root!.Id}");
        Assert.That(rootDelete.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));

        var created = await client.PostAsJsonAsync("/api/users", new { email = $"deactivate-{Guid.NewGuid():N}@example.test", roles = new[] { "lector" } });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString()!;
        var delete = await client.DeleteAsync($"/api/users/{id}");
        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That(await users.FindByIdAsync(id), Is.Not.Null, "soft delete retains the Identity row");
        var row = (await client.GetFromJsonAsync<JsonElement>("/api/users", Json)).GetProperty("items")
            .EnumerateArray().Single(user => user.GetProperty("id").GetString() == id);
        Assert.That(row.GetProperty("isActive").GetBoolean(), Is.False);
        Assert.That(await scope.ServiceProvider.GetRequiredService<ApplicationContext>().AuditLogs
            .AnyAsync(entry => entry.Entity == "UserProfile" && entry.EntityId == id && entry.Action == "SoftDelete"), Is.True);
    }

    [Test]
    public async Task Impersonation_IsByRole_ReadOnly_AndCannotImpersonateAgain()
    {
        using var admin = await LoginAsSuperadmin();
        var start = await admin.PostAsJsonAsync("/api/users/impersonate", new { role = "lector" });
        Assert.That(start.StatusCode, Is.EqualTo(HttpStatusCode.OK), await start.Content.ReadAsStringAsync());
        var payload = await start.Content.ReadFromJsonAsync<JsonElement>(Json);
        var token = payload.GetProperty("access_token").GetString()!;
        Assert.Multiple(() =>
        {
            Assert.That(payload.GetProperty("role").GetString(), Is.EqualTo("lector"));
            Assert.That(payload.GetProperty("scope").GetString(), Is.EqualTo("read"));
            Assert.That(payload.GetProperty("expires_at_utc").GetDateTime(),
                Is.InRange(DateTime.UtcNow.AddMinutes(4), DateTime.UtcNow.AddMinutes(11)));
            Assert.That(payload.GetProperty("impersonated_by").GetProperty("email").GetString(),
                Is.EqualTo("bootstrap@example.test"));
        });

        var claims = new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.ToList();
        string? Claim(string type) => claims.FirstOrDefault(claim => claim.Type == type)?.Value;
        var roleClaims = claims.Where(claim => claim.Type == ClaimTypes.Role || claim.Type == "role")
            .Select(claim => claim.Value).ToList();
        using var scope = PersonaScope();
        var actor = await scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>()
            .FindByEmailAsync("bootstrap@example.test");
        Assert.Multiple(() =>
        {
            // The token keeps the real identity in `sub`: impersonating a role must never turn the
            // actor into a different account, or the audit trail would name somebody who did nothing.
            Assert.That(Claim("sub"), Is.EqualTo(actor!.Id));
            Assert.That(Claim("act"), Is.EqualTo(actor.Id));
            Assert.That(Claim("act_email"), Is.EqualTo(actor.Email));
            Assert.That(Claim("impersonated_role"), Is.EqualTo("lector"));
            Assert.That(Claim("scope"), Is.EqualTo("read"));
            Assert.That(roleClaims, Is.EqualTo(new[] { "lector" }), "only the target role is carried");
        });

        using var impersonated = Factory.CreateClient();
        impersonated.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.That((await impersonated.GetAsync("/api/item")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await impersonated.PostAsJsonAsync("/api/item",
                new { label = "blocked", icon = "", attributes = "{}" })).StatusCode,
            Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await impersonated.PostAsJsonAsync("/api/auth/login",
                new { email = "bootstrap@example.test", password = Password })).StatusCode,
            Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await impersonated.GetAsync("/api/users")).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await impersonated.PostAsJsonAsync("/api/users/impersonate",
                new { role = "lector" })).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden),
            "an impersonation token cannot impersonate again");
        Assert.That((await impersonated.PatchAsync("/api/item/1/activate", content: null)).StatusCode,
            Is.EqualTo(HttpStatusCode.Forbidden), "restoring is a write and stays blocked");

        // Ending the borrow is done from the actor's own session (the browser keeps its refresh
        // cookie), never from a bare holder of the borrowed token — that holder cannot be allowed to
        // collect the actor's token, which is what StopImpersonation_WithoutTheActorsSession pins.
        var bareStop = await impersonated.PostAsync("/api/auth/stop-impersonation", content: null);
        Assert.Multiple(async () =>
        {
            Assert.That(bareStop.StatusCode, Is.EqualTo(HttpStatusCode.NoContent),
                "a bare bearer ends the borrow but is handed nothing");
            Assert.That(await bareStop.Content.ReadAsStringAsync(), Does.Not.Contain("access_token"));
        });
        // The actor's own session is the one that can collect their token back; that path has its own
        // test (StopImpersonation_WithTheActorsOwnSession_ReturnsTheirToken), and this borrow is already
        // over. What matters here: the bare holder got nothing, and the pair below is complete.

        var audit = scope.ServiceProvider.GetRequiredService<ApplicationContext>().AuditLogs;
        var startAudit = await audit.OrderByDescending(row => row.Id)
            .FirstAsync(row => row.Action == "ImpersonateStart");
        var endAudit = await audit.OrderByDescending(row => row.Id)
            .FirstAsync(row => row.Action == "ImpersonateEnd");
        Assert.Multiple(() =>
        {
            Assert.That(startAudit.Entity, Is.EqualTo("UserRole"));
            Assert.That(startAudit.EntityId, Is.EqualTo("lector"));
            Assert.That(startAudit.ActorUserId, Is.EqualTo(actor!.Id), "the actor is the real user");
            Assert.That(endAudit.EntityId, Is.EqualTo("lector"));
            Assert.That(endAudit.ActorUserId, Is.EqualTo(actor.Id));
        });
    }

    [Test]
    public async Task Impersonation_FollowsTheHierarchy_AndNeverTargetsSuperadmin()
    {
        using var root = await LoginAsSuperadmin();
        Assert.That((await root.PostAsJsonAsync("/api/users/impersonate", new { role = "superadmin" })).StatusCode,
            Is.EqualTo(HttpStatusCode.BadRequest), "the superadmin role is never impersonable");
        Assert.That((await root.PostAsJsonAsync("/api/users/impersonate", new { role = "wizard" })).StatusCode,
            Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await root.PostAsJsonAsync("/api/users/impersonate", new { role = "" })).StatusCode,
            Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await root.PostAsJsonAsync("/api/users/impersonate", new { role = "ADMIN" })).StatusCode,
            Is.EqualTo(HttpStatusCode.OK), "the role name is normalised, not case-sensitive");

        using var superadmin = await CreateAndLogin(root, "admin", "HierarchyPass!1");
        Assert.That((await superadmin.PostAsJsonAsync("/api/users/impersonate", new { role = "operador" })).StatusCode,
            Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await superadmin.PostAsJsonAsync("/api/users/impersonate", new { role = "admin" })).StatusCode,
            Is.EqualTo(HttpStatusCode.BadRequest), "an admin cannot impersonate an admin");

        using var operador = await CreateAndLogin(root, "operador", "HierarchyPass!2");
        Assert.That((await operador.PostAsJsonAsync("/api/users/impersonate", new { role = "lector" })).StatusCode,
            Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await operador.PostAsJsonAsync("/api/users/impersonate", new { role = "admin" })).StatusCode,
            Is.EqualTo(HttpStatusCode.BadRequest), "impersonation only goes down");

        using var lector = await CreateAndLogin(root, "lector", "HierarchyPass!3");
        Assert.That((await lector.PostAsJsonAsync("/api/users/impersonate", new { role = "lector" })).StatusCode,
            Is.EqualTo(HttpStatusCode.Forbidden), "a lector may not impersonate at all");
    }

    [Test]
    public async Task CreateUser_WithOrWithoutPassword_ProducesAnAccountThatCanLogIn()
    {
        using var root = await LoginAsSuperadmin();

        var chosenEmail = $"chosen-{Guid.NewGuid():N}@example.test";
        var created = await root.PostAsJsonAsync("/api/users",
            new { email = chosenEmail, roles = new[] { "lector" }, password = "ChosenPass!42" });
        var createdBody = await created.Content.ReadAsStringAsync();
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created), createdBody);
        Assert.That(createdBody, Does.Not.Contain("ChosenPass!42"), "a password the caller chose is never echoed back");
        Assert.That(JsonDocument.Parse(createdBody).RootElement.TryGetProperty("initialPassword", out var echoed),
            Is.True);
        Assert.That(echoed.ValueKind, Is.EqualTo(JsonValueKind.Null));

        using var chosen = await LoginAs(chosenEmail, "ChosenPass!42");
        Assert.That((await chosen.GetAsync("/api/item")).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // No password in the request: the server generates one and shows it exactly once.
        var generatedEmail = $"generated-{Guid.NewGuid():N}@example.test";
        var generated = await root.PostAsJsonAsync("/api/users", new { email = generatedEmail, roles = new[] { "lector" } });
        Assert.That(generated.StatusCode, Is.EqualTo(HttpStatusCode.Created), await generated.Content.ReadAsStringAsync());
        var initialPassword = (await generated.Content.ReadFromJsonAsync<JsonElement>(Json))
            .GetProperty("initialPassword").GetString();
        Assert.That(initialPassword, Has.Length.EqualTo(24));
        using var generatedClient = await LoginAs(generatedEmail, initialPassword!);
        Assert.That((await generatedClient.GetAsync("/api/item")).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var weak = await root.PostAsJsonAsync("/api/users",
            new { email = $"weak-{Guid.NewGuid():N}@example.test", roles = Array.Empty<string>(), password = "123" });
        Assert.That(weak.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task ResetPassword_ChangesTheUsablePassword_AndRevokesRefreshTokens()
    {
        using var root = await LoginAsSuperadmin();
        var email = $"reset-{Guid.NewGuid():N}@example.test";
        var created = await root.PostAsJsonAsync("/api/users",
            new { email, roles = new[] { "lector" }, password = "OriginalPass!1" });
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created), await created.Content.ReadAsStringAsync());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString()!;

        using var before = await LoginAs(email, "OriginalPass!1");

        var reset = await root.PostAsJsonAsync($"/api/users/{id}/password", new { password = "ResetPass!22" });
        Assert.That(reset.StatusCode, Is.EqualTo(HttpStatusCode.OK), await reset.Content.ReadAsStringAsync());

        // The reset rotates the security stamp, and the pipeline compares it: a token minted before the
        // reset must stop working at once, or "reset the password because it leaked" would leave the
        // attacker's 15-minute access token alive.
        Assert.That((await before.GetAsync("/api/item")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            "a reset invalidates the access tokens minted against the old security stamp");

        using var stale = Factory.CreateClient();
        var staleLogin = await stale.PostAsJsonAsync("/api/auth/login", new { email, password = "OriginalPass!1" });
        Assert.That(staleLogin.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "the old password stops working");

        // The revocation is checked before logging in again, or the new session's own refresh token
        // would make the assertion meaningless.
        using (var scope = PersonaScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
            Assert.That(await context.RefreshTokens.AnyAsync(row => row.UserId == id && row.RevokedAtUtc == null), Is.False,
                "a reset revokes every live refresh token of the account");
            Assert.That(await context.AuditLogs.AnyAsync(row => row.Action == "PasswordReset" && row.EntityId == id), Is.True);
        }

        using var after = await LoginAs(email, "ResetPass!22");
        Assert.That((await after.GetAsync("/api/item")).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var generatedReset = await root.PostAsJsonAsync($"/api/users/{id}/password", new { });
        var generatedValue = (await generatedReset.Content.ReadFromJsonAsync<JsonElement>(Json))
            .GetProperty("initialPassword").GetString();
        Assert.That(generatedValue, Has.Length.EqualTo(24));
        using var generatedLogin = await LoginAs(email, generatedValue!);

        Assert.That((await root.PostAsJsonAsync("/api/users/not-a-user/password", new { password = "Whatever!1" })).StatusCode,
            Is.EqualTo(HttpStatusCode.NotFound));

        using var operador = await CreateAndLogin(root, "operador", "ResetPass!33");
        Assert.That((await operador.PostAsJsonAsync($"/api/users/{id}/password", new { password = "Hijack!123" })).StatusCode,
            Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task RoleChange_RotatesTheSecurityStamp_SoTheOldTokenStopsWorking()
    {
        using var root = await LoginAsSuperadmin();
        var email = $"demote-{Guid.NewGuid():N}@example.test";
        var created = await root.PostAsJsonAsync("/api/users",
            new { email, roles = new[] { "lector" }, password = "RoleChange!1" });
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created), await created.Content.ReadAsStringAsync());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString()!;

        using var holder = await LoginAs(email, "RoleChange!1");
        Assert.That((await holder.PostAsJsonAsync("/api/item",
                new { label = "not-yours", icon = "", attributes = "{}" })).StatusCode,
            Is.EqualTo(HttpStatusCode.Forbidden), "a lector cannot write");

        var promoted = await root.PutAsJsonAsync($"/api/users/{id}/roles", new { roles = new[] { "operador" } });
        Assert.That(promoted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), await promoted.Content.ReadAsStringAsync());

        // The roles inside the token are stale, and the stamp is what the pipeline compares: the
        // token minted before the change must stop working instead of carrying the old role until
        // it expires.
        Assert.That((await holder.GetAsync("/api/item")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            "a role change invalidates the token minted under the previous roles");

        using var again = await LoginAs(email, "RoleChange!1");
        Assert.That((await again.PostAsJsonAsync("/api/item",
                new { label = "now-yours", icon = "", attributes = "{}" })).StatusCode,
            Is.EqualTo(HttpStatusCode.OK), "the new token carries the new role");
    }

    /// <summary>
    /// The borrowed token is a bearer handed to a read-only session. If holding it were enough to
    /// collect the actor's own token, the whole promise would be one request away: measured before
    /// the guard, a superadmin+lector borrow was exchanged for a 15-minute superadmin token with
    /// write, without a cookie and without a password.
    /// </summary>
    [Test]
    public async Task StopImpersonation_WithoutTheActorsSession_DoesNotHandOutTheActorsToken()
    {
        using var root = await LoginAsSuperadmin();
        var start = await root.PostAsJsonAsync("/api/users/impersonate", new { role = "lector" });
        Assert.That(start.StatusCode, Is.EqualTo(HttpStatusCode.OK), await start.Content.ReadAsStringAsync());
        var borrowed = (await start.Content.ReadFromJsonAsync<JsonElement>(Json))
            .GetProperty("access_token").GetString()!;

        // A holder of the borrowed token and nothing else: fresh client, so no refresh cookie.
        using var holder = Factory.CreateClient();
        holder.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", borrowed);
        var exchange = await holder.PostAsync("/api/auth/stop-impersonation", content: null);

        Assert.Multiple(async () =>
        {
            Assert.That(exchange.StatusCode, Is.EqualTo(HttpStatusCode.NoContent),
                "the borrow is closed, and the actor's token is not handed to a bare holder");
            Assert.That(await exchange.Content.ReadAsStringAsync(), Does.Not.Contain("access_token"));
        });

        // The escalation it used to allow: the token it never got — and the borrow is gone with it.
        var users = await holder.GetAsync("/api/users");
        Assert.That(users.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            "the token died with the borrow, so nothing wider is reachable");
    }

    [Test]
    public async Task StopImpersonation_WithTheActorsOwnSession_ReturnsTheirToken()
    {
        using var root = await LoginAsSuperadmin();
        var start = await root.PostAsJsonAsync("/api/users/impersonate", new { role = "operador" });
        Assert.That(start.StatusCode, Is.EqualTo(HttpStatusCode.OK), await start.Content.ReadAsStringAsync());
        var borrowed = (await start.Content.ReadFromJsonAsync<JsonElement>(Json))
            .GetProperty("access_token").GetString()!;

        // The same client that started the impersonation holds the refresh cookie: that is the proof.
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/stop-impersonation");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", borrowed);
        var response = await root.SendAsync(request);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("access_token").GetString()!;
        var claims = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Multiple(() =>
        {
            Assert.That(claims.Claims.Where(claim => claim.Type == ClaimTypes.Role).Select(claim => claim.Value),
                Does.Contain("superadmin"), "the actor gets their own privileges back");
            Assert.That(claims.Claims.Any(claim => claim.Type == "act"), Is.False,
                "and nothing of the borrow survives in it");
        });
    }

    [Test]
    public async Task Impersonation_EndsOnce_AndTheBorrowedTokenDiesWithIt()
    {
        using var root = await LoginAsSuperadmin();
        var start = await root.PostAsJsonAsync("/api/users/impersonate", new { role = "lector" });
        var payload = await start.Content.ReadFromJsonAsync<JsonElement>(Json);
        var borrowed = payload.GetProperty("access_token").GetString()!;
        var sessionId = payload.GetProperty("impersonation_session").GetString();
        Assert.That(sessionId, Is.Not.Null.And.Not.Empty, "the borrow names itself so it can be paired and revoked");

        using var holder = Factory.CreateClient();
        holder.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", borrowed);
        Assert.That((await holder.GetAsync("/api/item")).StatusCode, Is.EqualTo(HttpStatusCode.OK), "live borrow reads");

        // A holder without the actor's cookie ends the borrow but gets no token back.
        var firstStop = await holder.PostAsync("/api/auth/stop-impersonation", content: null);
        Assert.That(firstStop.StatusCode, Is.EqualTo(HttpStatusCode.NoContent),
            "the borrow closes; the actor's token is not handed to a bare holder");
        Assert.That(await firstStop.Content.ReadAsStringAsync(), Does.Not.Contain("access_token"));

        // …and the token that was borrowed stops working immediately, not in seven minutes.
        Assert.That((await holder.GetAsync("/api/item")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            "an ended borrow is a dead token");

        // A second stop finds nothing to end: single use, from any holder.
        using var actor = await LoginAsSuperadmin();
        using var second = new HttpRequestMessage(HttpMethod.Post, "/api/auth/stop-impersonation");
        second.Headers.Authorization = new AuthenticationHeaderValue("Bearer", borrowed);
        Assert.That((await actor.SendAsync(second)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            "a borrow is ended once: the second attempt has no live token to make it with");

        using var scope = PersonaScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var session = await context.ImpersonationSessions.SingleAsync(row => row.Id == sessionId);
        Assert.That(session.EndedAtUtc, Is.Not.Null);
        Assert.That(session.EndedBy, Is.EqualTo("api"));
    }

    [Test]
    public async Task Impersonation_TokenStopsWorkingWhenTheSessionExpires()
    {
        using var root = await LoginAsSuperadmin();
        var start = await root.PostAsJsonAsync("/api/users/impersonate", new { role = "operador" });
        var payload = await start.Content.ReadFromJsonAsync<JsonElement>(Json);
        var borrowed = payload.GetProperty("access_token").GetString()!;
        var sessionId = payload.GetProperty("impersonation_session").GetString()!;

        // Let the borrow run out without waiting seven minutes for it.
        using (var scope = PersonaScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
            var session = await context.ImpersonationSessions.SingleAsync(row => row.Id == sessionId);
            session.ExpiresAtUtc = DateTime.UtcNow.AddSeconds(-1);
            await context.SaveChangesAsync();
        }

        using var holder = Factory.CreateClient();
        holder.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", borrowed);
        Assert.That((await holder.GetAsync("/api/item")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Impersonation_AuditNamesTheSessionThatFramedIt()
    {
        using var root = await LoginAsSuperadmin();
        var start = await root.PostAsJsonAsync("/api/users/impersonate", new { role = "lector" });
        var payload = await start.Content.ReadFromJsonAsync<JsonElement>(Json);
        var sessionId = payload.GetProperty("impersonation_session").GetString()!;
        var borrowed = payload.GetProperty("access_token").GetString()!;

        using var stopRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/stop-impersonation");
        stopRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", borrowed);
        Assert.That((await root.SendAsync(stopRequest)).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var scope = PersonaScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var startRow = await context.AuditLogs.SingleAsync(row => row.Action == "ImpersonateStart" && row.ImpersonationSessionId == sessionId);
        var endRow = await context.AuditLogs.SingleAsync(row => row.Action == "ImpersonateEnd" && row.ImpersonationSessionId == sessionId);
        Assert.Multiple(() =>
        {
            Assert.That(startRow.EntityId, Is.EqualTo("lector"));
            Assert.That(endRow.EntityId, Is.EqualTo("lector"));
            Assert.That(startRow.ActorUserId, Is.EqualTo(endRow.ActorUserId), "both name the actor");
            Assert.That(startRow.AfterJson, Does.Contain(sessionId), "and the payload keeps the pairing");
        });

        // The session's own row is bookkeeping, not a business change: no Update row for it.
        Assert.That(await context.AuditLogs.AnyAsync(row => row.Entity == "ImpersonationSession"), Is.False);
    }

    private async Task<HttpClient> CreateAndLogin(HttpClient root, string role, string password)
    {
        var email = $"{role}-{Guid.NewGuid():N}@example.test";
        var created = await root.PostAsJsonAsync("/api/users", new { email, roles = new[] { role }, password });
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created), await created.Content.ReadAsStringAsync());
        return await LoginAs(email, password);
    }

}
