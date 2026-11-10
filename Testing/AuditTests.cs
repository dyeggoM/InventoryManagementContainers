using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IMCAPI.DB;
using IMCAPI.DB.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Testing;

[NonParallelizable]
[TestFixture]
public sealed class AuditTests
{
    private ApiFactory _factory = null!;
    private HttpClient _hostClient = null!;
    private const string Password = "AuditTest!Secure123";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [OneTimeSetUp]
    public void Start()
    {
        _factory = new ApiFactory();
        _hostClient = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public void Stop()
    {
        _hostClient.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task Interceptor_RecordsBeforeAndAfterOnUpdate()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var item = new Item { Label = "Audit before " + Guid.NewGuid(), Icon = "", UpdatedBy = "forged-on-create" };
        context.Items.Add(item);
        await context.SaveChangesAsync();
        var id = item.Id;

        item.Label = "Audit after " + Guid.NewGuid();
        item.UpdatedBy = "forged-on-update";
        await context.SaveChangesAsync();

        var entry = await context.AuditLogs.SingleAsync(log => log.Entity == nameof(Item)
            && log.EntityId == id.ToString() && log.Action == "Update");
        Assert.Multiple(() =>
        {
            Assert.That(entry.BeforeJson, Does.Contain("Audit before"));
            Assert.That(entry.AfterJson, Does.Contain("Audit after"));
            Assert.That(entry.AfterJson, Does.Not.Contain("forged-on-update"));
            Assert.That(item.UpdatedBy, Is.Null);
            Assert.That(item.CreatedAt, Is.Not.Null);
        });
    }

    [Test]
    public async Task Interceptor_RecordsDeletedEntityBeforeValues()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var item = new Item { Label = "Delete audit " + Guid.NewGuid(), Icon = "" };
        context.Items.Add(item);
        await context.SaveChangesAsync();
        var id = item.Id;

        context.Items.Remove(item);
        await context.SaveChangesAsync();

        var entry = await context.AuditLogs.SingleAsync(log => log.Entity == nameof(Item)
            && log.EntityId == id.ToString() && log.Action == "Delete");
        Assert.That(entry.BeforeJson, Does.Contain("Delete audit"));
        Assert.That(entry.AfterJson, Is.Null);
    }

    [Test]
    public async Task Interceptor_RedactsPasswordSecurityStampAndRefreshTokenHash()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var user = new IdentityUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = "audit-secret-" + Guid.NewGuid().ToString("N"),
            NormalizedUserName = "AUDIT-SECRET-" + Guid.NewGuid().ToString("N"),
            PasswordHash = "password-hash-MUST-NOT-APPEAR",
            SecurityStamp = "security-stamp-MUST-NOT-APPEAR",
        };
        var token = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = "refresh-token-hash-MUST-NOT-APPEAR",
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1),
        };
        context.Users.Add(user);
        context.RefreshTokens.Add(token);
        await context.SaveChangesAsync();

        var rows = await context.AuditLogs.Where(log => log.Entity == nameof(IdentityUser) && log.EntityId == user.Id
            || log.Entity == nameof(RefreshToken) && log.EntityId == token.Id.ToString()).ToListAsync();
        var json = string.Join("\n", rows.SelectMany(row => new[] { row.BeforeJson, row.AfterJson }).Where(value => value is not null));
        Assert.Multiple(() =>
        {
            Assert.That(json, Does.Not.Contain("password-hash-MUST-NOT-APPEAR"));
            Assert.That(json, Does.Not.Contain("security-stamp-MUST-NOT-APPEAR"));
            Assert.That(json, Does.Not.Contain("refresh-token-hash-MUST-NOT-APPEAR"));
            Assert.That(json, Does.Contain("[REDACTED]"));
        });
    }

    [Test]
    public async Task HttpActorAndRequestMetadataPopulateAuditAndCreatedFields()
    {
        using var client = _factory.CreateClient();
        await Authenticate(client, "bootstrap@example.test", "AuthTest!Secure123");
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var actor = await users.FindByEmailAsync("bootstrap@example.test");
        var label = "HTTP audit " + Guid.NewGuid().ToString("N");
        var response = await client.PostAsJsonAsync("/api/item", new
        {
            label, icon = "", parentId = (int?)null, attributes = "{}",
            children = Array.Empty<object>(), attachments = Array.Empty<object>()
        });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        var id = result.GetProperty("id").GetInt32();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var item = await context.Items.SingleAsync(row => row.Id == id);
        var audit = await context.AuditLogs.SingleAsync(row => row.Action == "Create" && row.Entity == nameof(Item)
            && row.EntityId == id.ToString());
        using var after = JsonDocument.Parse(audit.AfterJson!);
        Assert.Multiple(() =>
        {
            Assert.That(after.RootElement.GetProperty("Id").GetInt32(), Is.EqualTo(id));
            Assert.That(item.CreatedAt, Is.Not.Null);
            Assert.That(item.CreatedBy, Is.EqualTo(actor!.Email));
            Assert.That(item.UpdatedBy, Is.EqualTo(actor.Email));
            Assert.That(audit.ActorUserId, Is.EqualTo(actor.Id));
            Assert.That(audit.ActorEmail, Is.EqualTo(actor.Email));
            Assert.That(audit.RequestId, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public async Task AuditEndpoint_AdminIsForbiddenAndSuperadminCanRead()
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var email = "audit-admin-" + Guid.NewGuid().ToString("N") + "@example.test";
        var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
        Assert.That((await users.CreateAsync(user, Password)).Succeeded, Is.True);
        Assert.That((await users.AddToRoleAsync(user, "admin")).Succeeded, Is.True);
        var roleChange = await scope.ServiceProvider.GetRequiredService<ApplicationContext>().AuditLogs
            .SingleAsync(row => row.Action == "RoleChange" && row.Entity == "IdentityUserRole" && row.EntityId == user.Id);
        Assert.That(roleChange.AfterJson, Does.Contain("UserId"));

        using var admin = _factory.CreateClient();
        await Authenticate(admin, email, Password);
        Assert.That((await admin.GetAsync("/api/audit")).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        using var root = _factory.CreateClient();
        await Authenticate(root, "bootstrap@example.test", "AuthTest!Secure123");
        Assert.That((await root.GetAsync("/api/audit")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public async Task HttpUpdateAuditShowsDifferentPreviousAndCurrentFieldValues()
    {
        using var client = _factory.CreateClient();
        await Authenticate(client, "bootstrap@example.test", "AuthTest!Secure123");
        var originalLabel = "Audit update old " + Guid.NewGuid().ToString("N");
        var create = await client.PostAsJsonAsync("/api/item", new
        {
            label = originalLabel, icon = "", parentId = (int?)null, attributes = "{}",
            children = Array.Empty<object>(), attachments = Array.Empty<object>()
        });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var itemId = (await create.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();
        var newLabel = "Audit update new " + Guid.NewGuid().ToString("N");
        var update = await client.PutAsJsonAsync($"/api/item/{itemId}", new
        {
            id = itemId, label = newLabel, icon = "", parentId = (int?)null, attributes = "{}",
            children = Array.Empty<object>(), attachments = Array.Empty<object>()
        });
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var response = await client.GetAsync($"/api/audit?action=Update&entity=Item&entityId={itemId}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var rows = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        var row = rows.GetProperty("items").EnumerateArray().First();
        using var before = JsonDocument.Parse(row.GetProperty("beforeJson").GetString()!);
        using var after = JsonDocument.Parse(row.GetProperty("afterJson").GetString()!);
        Assert.Multiple(() =>
        {
            Assert.That(before.RootElement.GetProperty("Label").GetString(), Is.EqualTo(originalLabel));
            Assert.That(after.RootElement.GetProperty("Label").GetString(), Is.EqualTo(newLabel));
            Assert.That(before.RootElement.GetProperty("Label").GetString(), Is.Not.EqualTo(after.RootElement.GetProperty("Label").GetString()));
        });
    }

    [Test]
    public async Task SoftDeletedItemCanBeFoundBySoftDeleteAction()
    {
        using var client = _factory.CreateClient();
        await Authenticate(client, "bootstrap@example.test", "AuthTest!Secure123");
        var create = await client.PostAsJsonAsync("/api/item", new
        {
            label = "Soft-delete audit " + Guid.NewGuid().ToString("N"), icon = "", parentId = (int?)null,
            attributes = "{}", children = Array.Empty<object>(), attachments = Array.Empty<object>()
        });
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();
        Assert.That((await client.DeleteAsync($"/api/item/{id}")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var response = await client.GetAsync($"/api/audit?action=SoftDelete&entity=Item&entityId={id}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var rows = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.That(rows.GetProperty("items").EnumerateArray().Single().GetProperty("action").GetString(), Is.EqualTo("SoftDelete"));
    }

    [Test]
    public async Task ClientCannotWriteIdOrAuditMetadataOnCreateOrUpdate()
    {
        using var client = _factory.CreateClient();
        await Authenticate(client, "bootstrap@example.test", "AuthTest!Secure123");
        var forgedDate = new DateTime(2000, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        const int forgedId = 987654321;
        var create = await client.PostAsJsonAsync("/api/item", new
        {
            id = forgedId, createdAt = forgedDate, createdBy = "forged-created-by",
            updatedAt = forgedDate, updatedBy = "forged-updated-by",
            label = "Protected audit fields " + Guid.NewGuid().ToString("N"), icon = "", parentId = (int?)null,
            attributes = "{}", children = Array.Empty<object>(), attachments = Array.Empty<object>()
        });
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.OK), await create.Content.ReadAsStringAsync());
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var persisted = await context.Items.SingleAsync(item => item.Id == id);
        var originalCreatedAt = persisted.CreatedAt;
        var originalCreatedBy = persisted.CreatedBy;
        Assert.That(id, Is.Not.EqualTo(forgedId));

        var update = await client.PutAsJsonAsync($"/api/item/{id}", new
        {
            id, createdAt = forgedDate, createdBy = "forged-created-by",
            updatedAt = forgedDate, updatedBy = "forged-updated-by",
            label = "Protected fields updated", icon = "", parentId = (int?)null,
            attributes = "{}", children = Array.Empty<object>(), attachments = Array.Empty<object>()
        });
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        await context.Entry(persisted).ReloadAsync();
        var actor = await scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Microsoft.AspNetCore.Identity.IdentityUser>>()
            .FindByEmailAsync("bootstrap@example.test");
        Assert.Multiple(() =>
        {
            Assert.That(persisted.Id, Is.Not.EqualTo(forgedId));
            Assert.That(persisted.CreatedAt, Is.EqualTo(originalCreatedAt));
            Assert.That(persisted.CreatedBy, Is.EqualTo(originalCreatedBy));
            Assert.That(persisted.CreatedAt, Is.Not.EqualTo(forgedDate));
            Assert.That(persisted.CreatedBy, Is.Not.EqualTo("forged-created-by"));
            Assert.That(persisted.UpdatedAt, Is.Not.EqualTo(forgedDate));
            Assert.That(persisted.UpdatedBy, Is.EqualTo(actor!.Email));
            Assert.That(persisted.UpdatedBy, Is.Not.EqualTo("forged-updated-by"));
        });
    }

    [Test]
    public async Task AuditEndpoint_FiltersAndPaginates()
    {
        const string actorId = "audit-filter-actor";
        using (var scope = _factory.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
            context.AuditLogs.Add(new AuditLog
            {
                TimestampUtc = DateTime.UtcNow,
                ActorUserId = actorId,
                ActorEmail = "audit-filter@example.test",
                Action = "Update",
                Entity = "FilterProbe",
                EntityId = "probe-17",
                RequestId = "audit-filter-request",
            });
            await context.SaveChangesAsync();
        }

        using var root = _factory.CreateClient();
        await Authenticate(root, "bootstrap@example.test", "AuthTest!Secure123");
        var response = await root.GetAsync("/api/audit?actor=audit-filter-actor&entity=FilterProbe&entityId=probe-17&action=Update&page=1&pageSize=5");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        Assert.Multiple(() =>
        {
            Assert.That(payload.GetProperty("totalCount").GetInt32(), Is.EqualTo(1));
            Assert.That(payload.GetProperty("items").GetArrayLength(), Is.EqualTo(1));
            Assert.That(payload.GetProperty("items")[0].GetProperty("requestId").GetString(), Is.EqualTo("audit-filter-request"));
        });
    }

    [Test]
    public async Task AuditEndpoint_InvalidDateRangeReturnsProblemDetails()
    {
        using var root = _factory.CreateClient();
        await Authenticate(root, "bootstrap@example.test", "AuthTest!Secure123");
        var response = await root.GetAsync("/api/audit?from=2026-05-02T00:00:00Z&to=2026-05-01T00:00:00Z");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
    }

    [Test]
    public void AuditEndpoint_HasNoWriteOrDeleteRoute()
    {
        var source = _factory.Services.GetRequiredService<EndpointDataSource>();
        var auditEndpoints = source.Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => string.Equals(endpoint.RoutePattern.RawText, "api/audit", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var methods = auditEndpoints.SelectMany(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? Array.Empty<string>())
            .ToArray();
        Assert.That(methods, Is.EquivalentTo(new[] { "GET" }));
    }

    [Test]
    public async Task LoginFailureAndLogoutWriteExplicitAuditRows()
    {
        using var client = _factory.CreateClient();
        var failed = await client.PostAsJsonAsync("/api/auth/login", new { email = "nobody@example.test", password = "wrong" });
        Assert.That(failed.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "bootstrap@example.test", password = "AuthTest!Secure123" });
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await client.PostAsync("/api/auth/logout", content: null)).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var actions = await context.AuditLogs.Where(row => row.Action == "LoginFailed" || row.Action == "Login" || row.Action == "Logout")
            .Select(row => row.Action).ToListAsync();
        Assert.That(actions, Does.Contain("LoginFailed").And.Contain("Login").And.Contain("Logout"));
    }

    [Test]
    public async Task RefreshingAnAlreadyRotatedTokenWritesRefreshReuseAudit()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "bootstrap@example.test", password = "AuthTest!Secure123" });
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var oldCookie = login.Headers.GetValues("Set-Cookie").Single().Split(';', 2)[0];

        using var firstRefresh = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        firstRefresh.Headers.TryAddWithoutValidation("Cookie", oldCookie);
        Assert.That((await client.SendAsync(firstRefresh)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using var replay = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        replay.Headers.TryAddWithoutValidation("Cookie", oldCookie);
        Assert.That((await client.SendAsync(replay)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        Assert.That(await context.AuditLogs.CountAsync(row => row.Action == "RefreshReuse"), Is.EqualTo(1));
    }

    private static async Task Authenticate(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(Json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload.GetProperty("access_token").GetString());
    }
}
