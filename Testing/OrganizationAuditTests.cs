using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IMCAPI.DB;
using IMCAPI.DB.Entities;
using IMCAPI.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Testing;

[NonParallelizable]
[TestFixture]
public sealed class OrganizationAuditTests : ApiTestBase
{
    protected override bool FreshApiPerTest => true;
    private const int Persona = OrganizationConfiguration.DefaultTenantId;
    private const int LiveRoom = 900002;

    [Test]
    public async Task ItemPut_WithAttachment_IsRefusedBeforeWritingAnyFileOrRow()
    {
        var id = await CreateItem(Client, "ITEM PUT NO ORPHANS");
        using var bitmap = new SkiaSharp.SKBitmap(1, 1);
        bitmap.SetPixel(0, 0, SkiaSharp.SKColors.SteelBlue);
        using var encoded = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 90);
        var directory = Path.Combine(Factory.MediaRoot, "media", "items", id.ToString());
        static string[] Listing(string path) => Directory.Exists(path)
            ? Directory.GetFiles(path).Select(Path.GetFileName).OrderBy(name => name).ToArray()!
            : Array.Empty<string>();
        var before = Listing(directory);
        using var check = Factory.Services.CreateScope();
        var db = check.ServiceProvider.GetRequiredService<ApplicationContext>();
        var rowsBefore = await db.Attachments.IgnoreQueryFilters().CountAsync(row => row.ItemId == id);
        var itemBefore = await db.Items.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.Id == id)
            .Select(row => new { row.Label, row.Attributes, row.ParentId, row.UpdatedAt,
                row.UpdatedBy, row.IsActive, row.TenantId }).SingleAsync();
        TestContext.Out.WriteLine("directory before " + directory + ": " + JsonSerializer.Serialize(before));

        var response = await Client.PutAsJsonAsync($"/api/item/{id}", new
        {
            id, label = "CHANGED", icon = "", attributes = "{}",
            children = Array.Empty<object>(),
            attachments = new[] { new
            {
                itemId = 0, type = "Photo", fileName = "put-orphan.png",
                uploadBase64 = "data:image/png;base64," + Convert.ToBase64String(encoded.ToArray())
            } }
        });
        var after = Listing(directory);
        var rowsAfter = await db.Attachments.IgnoreQueryFilters().CountAsync(row => row.ItemId == id);
        var itemAfter = await db.Items.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.Id == id)
            .Select(row => new { row.Label, row.Attributes, row.ParentId, row.UpdatedAt,
                row.UpdatedBy, row.IsActive, row.TenantId }).SingleAsync();
        TestContext.Out.WriteLine("directory after " + directory + ": " + JsonSerializer.Serialize(after));
        TestContext.Out.WriteLine("item-put-attachment " + JsonSerializer.Serialize(new
        {
            status = (int)response.StatusCode, rowsBefore, rowsAfter, label = itemAfter.Label
        }));
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Contain("/api/attachment"));
        Assert.That(before, Is.Empty);
        Assert.That(after, Is.EqualTo(before));
        Assert.That(rowsAfter, Is.EqualTo(rowsBefore).And.EqualTo(0));
        Assert.That(itemAfter, Is.EqualTo(itemBefore));
    }

    [Test]
    public async Task CrossOrganizationPut_AnswersNotFoundForConstantsAttachmentsAndItems()
    {
        var ownItem = await CreateItem(Client, "OWN ITEM FOR FOREIGN ATTACHMENT PUT");
        int constantId, itemId, attachmentId;
        using (var scope = Factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantScope>().SelectSystemTenant(LiveRoom);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
            var constant = new Constants { Group = "Brand", Label = "FOREIGN PUT CONSTANT", Code = "FOREIGN PUT CONSTANT" };
            var item = new Item { Label = "FOREIGN PUT ITEM" };
            db.Constants.Add(constant);
            db.Items.Add(item);
            await db.SaveChangesAsync();
            var attachment = new Attachment { ItemId = item.Id, FileName = "foreign-original.txt", Type = "Photo" };
            db.Attachments.Add(attachment);
            await db.SaveChangesAsync();
            (constantId, itemId, attachmentId) = (constant.Id, item.Id, attachment.Id);
        }
        var constantResponse = await Client.PutAsJsonAsync($"/api/constants/{constantId}",
            new { id = constantId, group = "Brand", label = "CHANGED", icon = "", isActive = true });
        var attachmentResponse = await Client.PutAsJsonAsync($"/api/attachment/{attachmentId}",
            new { id = attachmentId, itemId = ownItem, fileName = "CHANGED", type = "Photo", isActive = true });
        var itemResponse = await Client.PutAsJsonAsync($"/api/item/{itemId}",
            new { id = itemId, label = "CHANGED", icon = "", isActive = true, attributes = "{}",
                children = Array.Empty<object>(), attachments = Array.Empty<object>() });
        var statuses = new { constants = (int)constantResponse.StatusCode, attachments = (int)attachmentResponse.StatusCode,
            items = (int)itemResponse.StatusCode };
        TestContext.Out.WriteLine("foreign-put " + JsonSerializer.Serialize(statuses));
        Assert.That(statuses.constants, Is.EqualTo(404), await constantResponse.Content.ReadAsStringAsync());
        Assert.That(statuses.attachments, Is.EqualTo(404), await attachmentResponse.Content.ReadAsStringAsync());
        Assert.That(statuses.items, Is.EqualTo(404), await itemResponse.Content.ReadAsStringAsync());
        using var check = Factory.Services.CreateScope();
        var context = check.ServiceProvider.GetRequiredService<ApplicationContext>();
        Assert.That(await context.Constants.IgnoreQueryFilters().Where(row => row.Id == constantId).Select(row => row.Label).SingleAsync(),
            Is.EqualTo("FOREIGN PUT CONSTANT"));
        Assert.That(await context.Items.IgnoreQueryFilters().Where(row => row.Id == itemId).Select(row => row.Label).SingleAsync(),
            Is.EqualTo("FOREIGN PUT ITEM"));
        Assert.That(await context.Attachments.IgnoreQueryFilters().Where(row => row.Id == attachmentId).Select(row => row.FileName).SingleAsync(),
            Is.EqualTo("foreign-original.txt"));
    }

    [Test]
    public async Task RelabellingAConstantIntoSharedOrUnknownGroups_IsRefused()
    {
        var created = await Client.PostAsJsonAsync("/api/constants", new { group = "Brand", label = "OWN BRAND", icon = "", isActive = true });
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.OK), await created.Content.ReadAsStringAsync());
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();
        foreach (var group in new[] { "Organization", "FieldType", "NotAGroup" })
        {
            var response = await Client.PutAsJsonAsync($"/api/constants/{id}",
                new { id, group, label = "CHANGED", icon = "", isActive = true });
            TestContext.Out.WriteLine("relabel " + JsonSerializer.Serialize(new { group, status = (int)response.StatusCode,
                body = await response.Content.ReadAsStringAsync() }));
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var persisted = await db.Constants.IgnoreQueryFilters().SingleAsync(row => row.Id == id);
        Assert.That((persisted.Group, persisted.TenantId, persisted.Label), Is.EqualTo(("Brand", Persona, "OWN BRAND")));
    }

    [Test]
    public async Task Switch_IsRecordedInBothOrganizations()
    {
        var response = await Client.PostAsJsonAsync("/api/auth/switch-organization", new { organizationId = LiveRoom });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var logs = await db.AuditLogs.IgnoreQueryFilters().Where(row => row.Action == "OrganizationSwitch")
            .OrderBy(row => row.TenantId).ToListAsync();
        Assert.That(logs.Select(row => row.TenantId), Is.EqualTo(new[] { Persona, LiveRoom }));
        foreach (var log in logs)
        {
            Assert.That(JsonDocument.Parse(log.BeforeJson!).RootElement.GetProperty("id").GetInt32(), Is.EqualTo(Persona));
            Assert.That(JsonDocument.Parse(log.AfterJson!).RootElement.GetProperty("id").GetInt32(), Is.EqualTo(LiveRoom));
        }
        TestContext.Out.WriteLine("switch-audits HTTP 200 " + JsonSerializer.Serialize(logs.Select(row =>
            new { row.TenantId, row.BeforeJson, row.AfterJson })));
    }

    [Test]
    public async Task RefreshReuseInPersona_LeavesLiveRoomChainValid()
    {
        using var client = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = SuperadminEmail, password = Password });
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var personaCookie = Cookie(login);
        var token = (await login.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("access_token").GetString();
        using var switchRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/switch-organization");
        switchRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        switchRequest.Headers.TryAddWithoutValidation("Cookie", personaCookie);
        switchRequest.Content = JsonContent.Create(new { organizationId = LiveRoom });
        var switched = await client.SendAsync(switchRequest);
        Assert.That(switched.StatusCode, Is.EqualTo(HttpStatusCode.OK), await switched.Content.ReadAsStringAsync());
        var liveCookie = Cookie(switched);
        async Task<HttpResponseMessage> RefreshWith(string cookie)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
            return await client.SendAsync(request);
        }
        var replay = await RefreshWith(personaCookie);
        var live = await RefreshWith(liveCookie);
        TestContext.Out.WriteLine("tenant-reuse " + JsonSerializer.Serialize(new { replay = (int)replay.StatusCode,
            otherOrganizationRefresh = (int)live.StatusCode }));
        Assert.That(replay.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        Assert.That(live.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var liveHash = AuthTokenService.HashRefreshToken(liveCookie["imc_refresh=".Length..]);
        var original = await db.RefreshTokens.SingleAsync(row => row.TokenHash == liveHash);
        Assert.That(original.TenantId, Is.EqualTo(LiveRoom));
        Assert.That(original.ReplacedByTokenHash, Is.Not.Null);
    }

    private static string Cookie(HttpResponseMessage response) => response.Headers.GetValues("Set-Cookie")
        .Single(value => value.StartsWith("imc_refresh=", StringComparison.Ordinal)).Split(';')[0];

    [Test]
    public async Task AttachmentPut_RejectsForeignAndMissingItem_WithoutChangingTheRow()
    {
        var ownItem = await CreateItem(Client, "OWN ATTACHMENT ITEM");
        var created = await Client.PostAsJsonAsync("/api/attachment", new
        {
            itemId = ownItem, type = "Photo", fileName = "original.txt", isActive = true
        });
        Assert.That(created.StatusCode, Is.EqualTo(HttpStatusCode.OK), await created.Content.ReadAsStringAsync());
        var attachmentId = (await created.Content.ReadFromJsonAsync<JsonElement>(Json)).GetProperty("id").GetInt32();
        int foreignItem;
        using (var scope = Factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantScope>().SelectSystemTenant(LiveRoom);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
            var item = new Item { Label = "FOREIGN ATTACHMENT ITEM" };
            db.Items.Add(item);
            await db.SaveChangesAsync();
            foreignItem = item.Id;
        }
        async Task<int> TryPut(int itemId)
        {
            var response = await Client.PutAsJsonAsync($"/api/attachment/{attachmentId}", new
            {
                id = attachmentId, itemId, type = "Photo", fileName = "changed.txt", isActive = true
            });
            return (int)response.StatusCode;
        }
        var foreignStatus = await TryPut(foreignItem);
        var missingStatus = await TryPut(999999);
        using (var check = Factory.Services.CreateScope())
        {
            var unchanged = await check.ServiceProvider.GetRequiredService<ApplicationContext>().Attachments.IgnoreQueryFilters()
                .SingleAsync(attachment => attachment.Id == attachmentId);
            Assert.That((unchanged.ItemId, unchanged.FileName, unchanged.TenantId),
                Is.EqualTo((ownItem, "original.txt", Persona)));
        }
        var renameStatus = await TryPut(ownItem);
        using var renamedCheck = Factory.Services.CreateScope();
        var row = await renamedCheck.ServiceProvider.GetRequiredService<ApplicationContext>().Attachments.IgnoreQueryFilters()
            .SingleAsync(attachment => attachment.Id == attachmentId);
        TestContext.Out.WriteLine("attachment-put " + JsonSerializer.Serialize(new
        {
            foreignStatus, missingStatus, renameStatus, row.ItemId, row.FileName, row.TenantId
        }));
        Assert.That((foreignStatus, missingStatus), Is.EqualTo((400, 400)));
        Assert.That(renameStatus, Is.EqualTo(204));
        Assert.That((row.ItemId, row.FileName, row.TenantId), Is.EqualTo((ownItem, "changed.txt", Persona)));
    }

    [Test]
    public async Task ConstantsPut_ForeignGroupDoesNotRevealWhetherRowExists()
    {
        int foreignId;
        using (var scope = Factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantScope>().SelectSystemTenant(LiveRoom);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
            var row = new Constants { Group = "Brand", Label = "FOREIGN ORACLE", Code = "FOREIGN ORACLE" };
            db.Constants.Add(row);
            await db.SaveChangesAsync();
            foreignId = row.Id;
        }
        async Task<int> TryPut(int id, string group)
        {
            var response = await Client.PutAsJsonAsync($"/api/constants/{id}", new
            {
                id, group, label = "CHANGED", icon = "", isActive = true
            });
            return (int)response.StatusCode;
        }
        var matching = await TryPut(foreignId, "Brand");
        var different = await TryPut(foreignId, "Category");
        var unknownGroup = await TryPut(foreignId, "NotAGroup");
        var absent = await TryPut(999999, "Brand");
        var absentUnknownGroup = await TryPut(999999, "NotAGroup");
        TestContext.Out.WriteLine("constants-oracle " + JsonSerializer.Serialize(new
        {
            matching, different, unknownGroup, absent, absentUnknownGroup
        }));
        Assert.That((matching, different, unknownGroup, absent, absentUnknownGroup),
            Is.EqualTo((404, 404, 404, 404, 404)));
        using var check = Factory.Services.CreateScope();
        Assert.That(await check.ServiceProvider.GetRequiredService<ApplicationContext>().Constants.IgnoreQueryFilters()
            .Where(row => row.Id == foreignId).Select(row => row.Label).SingleAsync(), Is.EqualTo("FOREIGN ORACLE"));
    }

    [Test]
    public async Task AuditGuardThrow_LeavesNoOpenTransaction()
    {
        var id = await CreateItem(Client, "GUARD ORIGINAL");
        using var scope = Factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<TenantScope>().SelectSystemTenant(LiveRoom);
        var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var foreign = await db.Items.IgnoreQueryFilters().SingleAsync(item => item.Id == id);
        foreign.Label = "GUARD CHANGED";
        Assert.ThrowsAsync<InvalidOperationException>(async () => await db.SaveChangesAsync());
        var transactionIsNull = db.Database.CurrentTransaction is null;
        TestContext.Out.WriteLine("audit-guard " + JsonSerializer.Serialize(new { transactionIsNull }));
        Assert.That(transactionIsNull, Is.True);
        db.Entry(foreign).State = EntityState.Unchanged;
        var recovery = new Item { Label = "GUARD RECOVERED" };
        db.Items.Add(recovery);
        await db.SaveChangesAsync();
        Assert.That(db.Database.CurrentTransaction, Is.Null);
        using var check = Factory.Services.CreateScope();
        var checkedDb = check.ServiceProvider.GetRequiredService<ApplicationContext>();
        Assert.That(await checkedDb.Items.IgnoreQueryFilters()
            .Where(item => item.Id == id).Select(item => item.Label).SingleAsync(), Is.EqualTo("GUARD ORIGINAL"));
        Assert.That(await checkedDb.Items.IgnoreQueryFilters()
            .Where(item => item.Id == recovery.Id).Select(item => item.Label).SingleAsync(), Is.EqualTo("GUARD RECOVERED"));
    }

    [Test]
    public async Task Switch_CommittedRefreshRowWorksOnNextRefresh()
    {
        var switched = await Client.PostAsJsonAsync("/api/auth/switch-organization", new { organizationId = LiveRoom });
        Assert.That(switched.StatusCode, Is.EqualTo(HttpStatusCode.OK), await switched.Content.ReadAsStringAsync());
        var newCookie = Cookie(switched);
        var hash = AuthTokenService.HashRefreshToken(newCookie["imc_refresh=".Length..]);
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
            var row = await db.RefreshTokens.SingleAsync(token => token.TokenHash == hash);
            Assert.That(row.TenantId, Is.EqualTo(LiveRoom));
            Assert.That(row.RevokedAtUtc, Is.Null);
        }
        var refreshed = await Client.PostAsync("/api/auth/refresh", null);
        TestContext.Out.WriteLine("switch-refresh " + JsonSerializer.Serialize(new
        {
            switchStatus = (int)switched.StatusCode, committedRow = true,
            refreshStatus = (int)refreshed.StatusCode
        }));
        Assert.That(refreshed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public void WriteScope_NestedSameDestinationRestoresPrevious_AndRejectsDifferentDestination()
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        try
        {
            var tenant = new TenantScope(accessor);
            using (tenant.SetWriteTenant(LiveRoom))
            {
                using (tenant.SetWriteTenant(LiveRoom))
                {
                    Assert.That(tenant.WriteTenantOverride, Is.EqualTo(LiveRoom));
                    Assert.Throws<InvalidOperationException>(() => tenant.SetWriteTenant(Persona));
                }
                Assert.That(tenant.WriteTenantOverride, Is.EqualTo(LiveRoom));
            }
            Assert.That(tenant.WriteTenantOverride, Is.Null);
            TestContext.Out.WriteLine("write-scope " + JsonSerializer.Serialize(new { nestedRestored = true, conflictingDestinationRefused = true }));
        }
        finally
        {
            accessor.HttpContext = null;
        }
    }
}
