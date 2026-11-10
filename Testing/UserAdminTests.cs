using System.Net;
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
public sealed class UserAdminTests
{
    private const string Password = "AuthTest!Secure123";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private ApiFactory _factory = null!;

    [OneTimeSetUp]
    public void Start() => _factory = new ApiFactory();

    [OneTimeTearDown]
    public void Stop() => _factory.Dispose();

    [Test]
    public async Task Superadmin_CreatesAndListsUser_AndDuplicateEmailConflicts()
    {
        using var client = await SuperadminClient();
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
        });
        var id = payload.GetProperty("id").GetString()!;

        var duplicate = await client.PostAsJsonAsync("/api/users", new { email = email.ToUpperInvariant(), roles = Array.Empty<string>() });
        Assert.That(duplicate.StatusCode, Is.EqualTo(HttpStatusCode.Conflict));

        var list = await client.GetFromJsonAsync<JsonElement>("/api/users", Json);
        Assert.That(list.GetProperty("items").EnumerateArray().Any(user => user.GetProperty("id").GetString() == id), Is.True);
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        Assert.That(await context.AuditLogs.AnyAsync(row => row.Entity == "IdentityUser" && row.EntityId == id && row.Action == "Create"), Is.True);
        Assert.That(await context.AuditLogs.AnyAsync(row => row.Action == "RoleChange" && row.EntityId == id), Is.True);
    }

    [Test]
    public async Task RoleChanges_RejectSelfAndSuperadminAssignment_AndAreAudited()
    {
        using var client = await SuperadminClient();
        using var scope = _factory.Services.CreateScope();
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
        using var client = await SuperadminClient();
        using var scope = _factory.Services.CreateScope();
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
    public async Task Impersonation_IsReadOnly_AndCannotManageUsersOrImpersonateAgain()
    {
        using var admin = await SuperadminClient();
        var created = await admin.PostAsJsonAsync("/api/users", new { email = $"impersonated-{Guid.NewGuid():N}@example.test", roles = new[] { "lector" } });
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.Created));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetString()!;
        var start = await admin.PostAsync($"/api/users/{id}/impersonate", content: null);
        Assert.That(start.StatusCode, Is.EqualTo(HttpStatusCode.OK), await start.Content.ReadAsStringAsync());
        var tokenPayload = await start.Content.ReadFromJsonAsync<JsonElement>(Json);
        var token = tokenPayload.GetProperty("access_token").GetString()!;
        Assert.That(tokenPayload.GetProperty("expires_at_utc").GetDateTime(), Is.InRange(DateTime.UtcNow.AddMinutes(4), DateTime.UtcNow.AddMinutes(11)));
        var claims = new JwtSecurityTokenHandler().ReadJwtToken(token).Claims.ToDictionary(claim => claim.Type, claim => claim.Value);
        using (var claimScope = _factory.Services.CreateScope())
        {
            var actor = await claimScope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>()
                .FindByEmailAsync("bootstrap@example.test");
            Assert.Multiple(() =>
            {
                Assert.That(claims["sub"], Is.EqualTo(id));
                Assert.That(claims["act"], Is.EqualTo(actor!.Id));
                Assert.That(claims["act_email"], Is.EqualTo(actor.Email));
                Assert.That(claims["scope"], Is.EqualTo("read"));
                Assert.That(claims["impersonated_user_id"], Is.EqualTo(id));
            });
        }

        using var impersonated = _factory.CreateClient();
        impersonated.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        Assert.That((await impersonated.GetAsync("/api/item")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await impersonated.PostAsJsonAsync("/api/item", new { label = "blocked", icon = "", attributes = "{}" })).StatusCode,
            Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await impersonated.PostAsJsonAsync("/api/auth/login", new { email = "bootstrap@example.test", password = Password })).StatusCode,
            Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await impersonated.GetAsync("/api/users")).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        Assert.That((await impersonated.PostAsync($"/api/users/{id}/impersonate", content: null)).StatusCode,
            Is.EqualTo(HttpStatusCode.Forbidden));

        Assert.That((await admin.DeleteAsync($"/api/users/{id}")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        var stop = await impersonated.PostAsync("/api/auth/stop-impersonation", content: null);
        Assert.That(stop.StatusCode, Is.EqualTo(HttpStatusCode.OK), await stop.Content.ReadAsStringAsync());
        var normalToken = (await stop.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("access_token").GetString();
        using var restored = _factory.CreateClient();
        restored.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", normalToken);
        Assert.That((await restored.GetAsync("/api/users")).StatusCode, Is.EqualTo(HttpStatusCode.OK));

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var audit = context.AuditLogs;
        var actorUser = await scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>()
            .FindByEmailAsync("bootstrap@example.test");
        var startAudit = await audit.SingleAsync(row => row.Action == "ImpersonateStart" && row.EntityId == id);
        var endAudit = await audit.SingleAsync(row => row.Action == "ImpersonateEnd" && row.EntityId == id);
        Assert.Multiple(() =>
        {
            Assert.That(startAudit.ActorUserId, Is.EqualTo(actorUser!.Id));
            Assert.That(startAudit.ImpersonatedUserId, Is.EqualTo(id));
            Assert.That(endAudit.ActorUserId, Is.EqualTo(actorUser.Id));
            Assert.That(endAudit.ImpersonatedUserId, Is.EqualTo(id));
        });
    }

    private async Task<HttpClient> SuperadminClient()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email = "bootstrap@example.test", password = Password });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("access_token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
