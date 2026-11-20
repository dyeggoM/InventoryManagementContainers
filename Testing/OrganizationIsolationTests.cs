using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IMCAPI.DB;
using IMCAPI.DB.Entities;
using IMCAPI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Testing;

[TestFixture]
public sealed class OrganizationIsolationTests : ApiTestBase
{
    protected override bool FreshApiPerTest => true;

    [Test]
    public void RequestlessWrites_RequireAnExplicitTenant()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        db.Items.Add(new Item { Label = "orphan" });
        Assert.ThrowsAsync<InvalidOperationException>(async () => await db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.Attachments.Add(new Attachment { ItemId = 1, FileName = "orphan", Type = "Photo" });
        Assert.ThrowsAsync<InvalidOperationException>(async () => await db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.ItemArchives.Add(new ItemArchive { ItemId = 1, Label = "orphan", Icon = "" });
        Assert.ThrowsAsync<InvalidOperationException>(async () => await db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.ImpersonationSessions.Add(new ImpersonationSession { Id = Guid.NewGuid().ToString("N") });
        Assert.ThrowsAsync<InvalidOperationException>(async () => await db.SaveChangesAsync());
    }

    [Test]
    public async Task HttpCreatedItem_IsInvisibleToAnotherOrganizationsRepository()
    {
        var persona = await CreateItem(Client, "persona-repository-only");
        using var scope = Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantScope>().SelectSystemTenant(900002);
        var repository = new IMCAPI.DB.Repositories.BaseRepository<Item>(
            scope.ServiceProvider.GetRequiredService<ApplicationContext>());
        Assert.That((await repository.GetAll()).Select(row => row.Id), Does.Not.Contain(persona));
        Assert.That(await repository.GetById(persona), Is.Null);
    }

    private async Task<int> Switch(HttpClient client, int organizationId)
    {
        var response = await client.PostAsJsonAsync("/api/auth/switch-organization", new { organizationId });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        var token = (await response.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("access_token").GetString();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return organizationId;
    }

    [Test]
    public async Task Tree_Detail_Create_AndSwitch_DoNotCrossOrganizations()
    {
        using (var check = Factory.Services.CreateScope())
        {
            var checkingDb = check.ServiceProvider.GetRequiredService<ApplicationContext>();
            TestContext.Out.WriteLine("organizations " + string.Join(",", await checkingDb.Constants.IgnoreQueryFilters()
                .Where(row => row.Group == "Organization").Select(row => row.Id + ":" + row.TenantId).ToArrayAsync()));
        }
        TestContext.Out.WriteLine("GET org " + (await Client.GetAsync("/api/organization")).StatusCode);
        var persona = await CreateItem(Client, "persona-only");
        using var oldToken = await LoginAsSuperadmin();
        await Switch(Client, 900002);
        var tree = await Client.GetStringAsync("/api/item");
        Assert.That(tree, Does.Not.Contain("persona-only"));
        Assert.That((await Client.GetAsync($"/api/item/{persona}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        var live = await CreateItem(Client, "live-only");
        Assert.That(await Client.GetStringAsync("/api/item"), Does.Contain("live-only"));
        Assert.That(await oldToken.GetStringAsync("/api/item"), Does.Contain("persona-only").And.Not.Contain("live-only"));

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var rows = await db.Items.IgnoreQueryFilters().Where(row => row.Id == persona || row.Id == live).ToListAsync();
        Assert.Multiple(() =>
        {
            Assert.That(rows.Single(row => row.Id == persona).TenantId, Is.EqualTo(900001));
            Assert.That(rows.Single(row => row.Id == live).TenantId, Is.EqualTo(900002));
        });
    }

    [Test]
    public async Task Archive_Restore_Attachment_AndConstants_DoNotCrossOrganizations()
    {
        var persona = await CreateItem(Client, "archived-persona");
        var withFile = await CreateItem(Client, "persona-file");
        Assert.That((await Client.DeleteAsync($"/api/item/{persona}")).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        using var scope = Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantScope>().SelectSystemTenant(900001);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        db.Attachments.Add(new Attachment { ItemId = withFile, FileName = "foreign.txt", Type = "Photo", FilePath = "missing.txt", TenantId = 900001 });
        db.Constants.Add(new Constants { Group = "Brand", Label = "PERSONA ONLY", Code = "PERSONA ONLY", TenantId = 900001 });
        await db.SaveChangesAsync();
        var attachmentId = await db.Attachments.IgnoreQueryFilters().Where(row => row.FileName == "foreign.txt").Select(row => row.Id).SingleAsync();
        await Switch(Client, 900002);
        Assert.That(await Client.GetStringAsync("/api/item/archive"), Does.Not.Contain("archived-persona"));
        Assert.That((await Client.PatchAsync($"/api/item/{persona}/activate", null)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That((await Client.GetAsync($"/api/attachment/{attachmentId}/file")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(await Client.GetStringAsync("/api/constants"), Does.Not.Contain("PERSONA ONLY"));
    }

    [Test]
    public async Task CycleGuard_AndDeletePath_IgnoreFiltersButNotTenant()
    {
        var foreign = await CreateItem(Client, "foreign-parent");
        await Switch(Client, 900002);
        var proposed = await Client.PostAsJsonAsync("/api/item", new { label = "bad-child", icon = "", parentId = foreign });
        Assert.That(proposed.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await Client.DeleteAsync($"/api/item/{foreign}")).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        using var scope = Factory.Services.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<TenantScope>();
        tenant.SelectSystemTenant(900002);
        var repo = new IMCAPI.DB.Repositories.BaseRepository<Item>(scope.ServiceProvider.GetRequiredService<ApplicationContext>());
        await repo.Delete(foreign);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        Assert.That(await db.Items.IgnoreQueryFilters().AnyAsync(row => row.Id == foreign && row.IsActive), Is.True);
    }

    [Test]
    public async Task Switch_RotatesRefresh_KeepsOldToken_AndBorrowedTokenCannotSwitch()
    {
        var persona = await CreateItem(Client, "persona-still-here");
        using var oldToken = await LoginAsSuperadmin();
        var organizations = await Client.GetFromJsonAsync<JsonElement>("/api/organization");
        Assert.That(organizations.EnumerateArray().Select(row => row.GetProperty("id").GetInt32()).ToArray(),
            Is.EqualTo(new[] { 900001, 900002, 900003 }));
        await Switch(Client, 900002);
        Assert.That(await oldToken.GetStringAsync("/api/item"), Does.Contain("persona-still-here"));
        Assert.That(await Client.GetStringAsync("/api/item"), Does.Not.Contain("persona-still-here"));
        var refresh = await Client.PostAsync("/api/auth/refresh", null);
        Assert.That(refresh.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var token = (await refresh.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("access_token").GetString();
        Client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        Assert.That(await Client.GetStringAsync("/api/item"), Does.Not.Contain("persona-still-here"));
        var borrowed = await Client.PostAsJsonAsync("/api/users/impersonate", new { role = "lector" });
        Assert.That(borrowed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var borrowedToken = (await borrowed.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("access_token").GetString();
        Client.DefaultRequestHeaders.Authorization = new("Bearer", borrowedToken);
        Assert.That((await Client.PostAsJsonAsync("/api/auth/switch-organization", new { organizationId = 900001 })).StatusCode,
            Is.EqualTo(HttpStatusCode.Forbidden));
    }
}
