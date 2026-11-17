using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IMCAPI.DB;
using IMCAPI.DB.Entities;
using IMCAPI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Testing;

[NonParallelizable]
[TestFixture]
public sealed class OrganizationImportTests : ApiTestBase
{
    protected override bool FreshApiPerTest => true;
    private const int Persona = OrganizationConfiguration.DefaultTenantId;
    private const int LiveRoom = 900002;
    private const string CsvFile = "DESCRIPCION,SUB CATEGORIA,MARCA,CODIGO\nTARGET IMPORT,BASE TECLADO SENCILLA,YAMAHA,ORG-TEST-001\n";

    private async Task<HttpResponseMessage> SendFile(string route, int? organizationId, HttpClient? client = null,
        string csv = CsvFile, string? corrections = null)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "sample.csv");
        if (organizationId.HasValue) form.Add(new StringContent(organizationId.Value.ToString()), "organizationId");
        if (corrections is not null) form.Add(new StringContent(corrections), "corrections");
        return await (client ?? Client).PostAsync(route, form);
    }

    private static async Task<JsonElement> Body(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Json);

    private async Task Print(string name, HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        if (text.Contains("\"access_token\"", StringComparison.Ordinal))
        {
            var body = JsonDocument.Parse(text).RootElement;
            text = JsonSerializer.Serialize(new { org = OrgClaim(body.GetProperty("access_token").GetString()!),
                expires_at_utc = body.GetProperty("expires_at_utc").GetString() });
        }
        TestContext.Out.WriteLine(name + " HTTP " + (int)response.StatusCode + " " +
            (text.Length == 0 ? "{}" : text));
    }

    [Test]
    public async Task CorrectionPreviewAndCrossOrganizationImport_KeepDestinationAndBothAuditTrails()
    {
        const string csv = "DESCRIPCION,SUB CATEGORIA,MARCA,CODIGO\n,BASE TECLADO SENCILLA,YAMAHA,ORG-CORR-001\n";
        const string label = "CROSS ORG CORRECTED";
        var corrections = JsonSerializer.Serialize(new[] { new { row = 2, label } });
        var baselineResponse = await SendFile("/api/import/preview", Persona, csv: csv);
        Assert.That(baselineResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var baseline = await Body(baselineResponse);
        Assert.That(baseline.GetProperty("summary").GetProperty("red").GetInt32(), Is.EqualTo(1));

        var previewResponse = await SendFile("/api/import/preview", LiveRoom, csv: csv, corrections: corrections);
        Assert.That(previewResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), await previewResponse.Content.ReadAsStringAsync());
        var preview = await Body(previewResponse);
        var corrected = preview.GetProperty("sheets").EnumerateArray()
            .SelectMany(sheet => sheet.GetProperty("groups").EnumerateArray())
            .SelectMany(group => group.GetProperty("items").EnumerateArray())
            .Single(row => row.GetProperty("label").GetString() == label);
        Assert.That(corrected.GetProperty("status").GetString(), Is.EqualTo("green"));
        Assert.That(preview.GetProperty("correctionIssues").GetArrayLength(), Is.Zero);

        var importResponse = await SendFile("/api/import", LiveRoom, csv: csv, corrections: corrections);
        Assert.That(importResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK), await importResponse.Content.ReadAsStringAsync());
        var result = await Body(importResponse);
        Assert.That(result.GetProperty("createdItems").GetInt32(), Is.EqualTo(1));
        Assert.That(result.GetProperty("corrected").GetInt32(), Is.EqualTo(1));
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var rows = await db.Items.IgnoreQueryFilters().Where(row => row.Label == label).ToListAsync();
        Assert.That(rows.Select(row => row.TenantId), Is.EqualTo(new[] { LiveRoom }));
        Assert.That(JsonDocument.Parse(rows.Single().Attributes!).RootElement.GetProperty("Code").GetString(),
            Is.EqualTo("ORG-CORR-001"));
        var audits = await db.AuditLogs.IgnoreQueryFilters().Where(row => row.Action == "ItemImport")
            .OrderBy(row => row.TenantId).ToListAsync();
        Assert.That(audits.Select(row => row.TenantId), Is.EqualTo(new[] { Persona, LiveRoom }));
        foreach (var audit in audits)
        {
            var details = JsonDocument.Parse(audit.AfterJson!).RootElement;
            Assert.That(details.GetProperty("actingOrganizationId").GetInt32(), Is.EqualTo(Persona));
            Assert.That(details.GetProperty("organizationId").GetInt32(), Is.EqualTo(LiveRoom));
            Assert.That(details.GetProperty("corrected").GetInt32(), Is.EqualTo(1));
        }
        TestContext.Out.WriteLine("combined-correction " + JsonSerializer.Serialize(new
        {
            baselineStatus = (int)baselineResponse.StatusCode,
            baselineRed = baseline.GetProperty("summary").GetProperty("red").GetInt32(),
            previewStatus = (int)previewResponse.StatusCode, verdict = corrected.GetProperty("status").GetString(),
            importStatus = (int)importResponse.StatusCode, createdItems = result.GetProperty("createdItems").GetInt32(),
            corrected = result.GetProperty("corrected").GetInt32(),
            rows = rows.Select(row => new { row.Label, row.TenantId }),
            audits = audits.Select(row => new
            {
                row.TenantId,
                actingOrganizationId = JsonDocument.Parse(row.AfterJson!).RootElement.GetProperty("actingOrganizationId").GetInt32(),
                organizationId = JsonDocument.Parse(row.AfterJson!).RootElement.GetProperty("organizationId").GetInt32(),
                corrected = JsonDocument.Parse(row.AfterJson!).RootElement.GetProperty("corrected").GetInt32()
            })
        }));
    }

    [Test]
    public async Task ImportAndPreview_UseOnlyTargetTenant_AuditBoth_AndOverrideDoesNotLeak()
    {
        var preview = await SendFile("/api/import/preview", LiveRoom);
        await Print("target-preview", preview);
        Assert.That(preview.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var import = await SendFile("/api/import", LiveRoom);
        await Print("target-import", import);
        Assert.That(import.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await Body(import)).GetProperty("createdItems").GetInt32(), Is.EqualTo(1));

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
            var rows = await db.Items.IgnoreQueryFilters().Where(row => row.Label == "TARGET IMPORT").ToListAsync();
            Assert.That(rows.Select(row => row.TenantId), Is.EqualTo(new[] { LiveRoom }));
            var audits = await db.AuditLogs.IgnoreQueryFilters().Where(row => row.Action == "ItemImport")
                .OrderBy(row => row.TenantId).ToListAsync();
            Assert.That(audits.Select(row => row.TenantId), Is.EqualTo(new[] { Persona, LiveRoom }));
            foreach (var audit in audits)
            {
                var details = JsonDocument.Parse(audit.AfterJson!).RootElement;
                Assert.That(details.GetProperty("actingOrganizationId").GetInt32(), Is.EqualTo(Persona));
                Assert.That(details.GetProperty("organizationId").GetInt32(), Is.EqualTo(LiveRoom));
            }
            TestContext.Out.WriteLine("target-rows " + JsonSerializer.Serialize(rows.Select(row => new { row.Label, row.TenantId })));
            TestContext.Out.WriteLine("import-audits " + JsonSerializer.Serialize(audits.Select(row => new { row.TenantId, row.AfterJson })));
        }

        await CreateItem(Client, "AFTER IMPORT");
        using var check = Factory.Services.CreateScope();
        var context = check.ServiceProvider.GetRequiredService<ApplicationContext>();
        Assert.That(await context.Items.IgnoreQueryFilters().Where(row => row.Label == "AFTER IMPORT")
            .Select(row => row.TenantId).SingleAsync(), Is.EqualTo(Persona));
    }

    [Test]
    public async Task Validation_MissingUnknownInactive_AndNonAdmin()
    {
        foreach (var route in new[] { "/api/import/preview", "/api/import" })
        {
            var missing = await SendFile(route, null);
            await Print(route + "-missing", missing);
            Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            var unknown = await SendFile(route, 999999);
            await Print(route + "-unknown", unknown);
            Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
            await db.Constants.IgnoreQueryFilters().Where(row => row.Id == 900003)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.IsActive, false));
        }
        var inactive = await SendFile("/api/import", 900003);
        await Print("inactive", inactive);
        Assert.That(inactive.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        using var reader = await CreateAccountAndLogin("import-reader@example.test", "lector");
        var refused = await SendFile("/api/import", LiveRoom, reader);
        await Print("lector-import", refused);
        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task ConfigurationAndCodes_AreScopedToDestination()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<TenantScope>().SelectSystemTenant(Persona);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
            db.Items.Add(new Item { Label = "EXISTING PERSONA", Attributes = "{\"Code\":\"ORG-TEST-001\"}" });
            await db.SaveChangesAsync();
            db.Constants.Add(new Constants { TenantId = Persona, Group = "Category", Label = "SPECIAL CONFIG" });
            await db.SaveChangesAsync();
            db.Constants.Add(new Constants { TenantId = Persona, Group = "SPECIAL CONFIG", Label = "UNIQUE GROUP" });
            await db.SaveChangesAsync();
        }
        const string csv = "DESCRIPCION,SUB CATEGORIA,MARCA,CODIGO\nTARGET IMPORT,UNIQUE GROUP,YAMAHA,ORG-TEST-001\n";
        var persona = await SendFile("/api/import/preview", Persona, csv: csv);
        var live = await SendFile("/api/import/preview", LiveRoom, csv: csv);
        await Print("persona-config", persona);
        await Print("live-config", live);
        Assert.That(persona.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(live.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await Body(persona)).GetProperty("summary").GetProperty("red").GetInt32(), Is.EqualTo(1));
        Assert.That((await Body(live)).GetProperty("summary").GetProperty("red").GetInt32(), Is.EqualTo(0));
        var imported = await SendFile("/api/import", LiveRoom, csv: csv);
        await Print("target-with-persona-code", imported);
        Assert.That(imported.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await Body(imported)).GetProperty("createdItems").GetInt32(), Is.EqualTo(1));
    }

    [Test]
    public async Task Switching_AuthorizesRolesAndImpersonation()
    {
        using var lector = await CreateAccountAndLogin("switch-reader@example.test", "lector");
        var denied = await lector.PostAsJsonAsync("/api/auth/switch-organization", new { organizationId = LiveRoom });
        await Print("lector-switch", denied);
        Assert.That(denied.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        using var admin = await CreateAccountAndLogin("switch-admin@example.test", "admin");
        var allowed = await admin.PostAsJsonAsync("/api/auth/switch-organization", new { organizationId = LiveRoom });
        await Print("admin-switch", allowed);
        Assert.That(allowed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var token = (await Body(allowed)).GetProperty("access_token").GetString()!;
        Assert.That(OrgClaim(token), Is.EqualTo(LiveRoom));
        var superadmin = await Client.PostAsJsonAsync("/api/auth/switch-organization", new { organizationId = LiveRoom });
        await Print("superadmin-switch", superadmin);
        Assert.That(superadmin.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var borrowed = await Client.PostAsJsonAsync("/api/users/impersonate", new { role = "lector" });
        Assert.That(borrowed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            (await Body(borrowed)).GetProperty("access_token").GetString());
        var refused = await Client.PostAsJsonAsync("/api/auth/switch-organization", new { organizationId = Persona });
        await Print("impersonated-switch", refused);
        Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
        using var check = await LoginAsSuperadmin();
        Assert.That((await check.GetAsync("/api/audit")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await admin.GetAsync("/api/audit")).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
    }

    private static int OrgClaim(string token)
    {
        var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight((payload.Length + 3) / 4 * 4, '=');
        return int.Parse(JsonDocument.Parse(Convert.FromBase64String(payload)).RootElement.GetProperty("org").GetString()!);
    }

    [Test]
    public async Task ConcurrentSwitches_OnlyOneRotatesTheCookie()
    {
        using var signedIn = Factory.CreateClient();
        var login = await signedIn.PostAsJsonAsync("/api/auth/login", new { email = SuperadminEmail, password = Password });
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var cookie = login.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("imc_refresh="))
            .Split(';')[0];
        var token = (await Body(login)).GetProperty("access_token").GetString();
        async Task<HttpResponseMessage> Switch()
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/switch-organization");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("Cookie", cookie);
            request.Content = JsonContent.Create(new { organizationId = LiveRoom });
            return await signedIn.SendAsync(request);
        }
        var responses = await Task.WhenAll(Switch(), Switch());
        foreach (var response in responses) await Print("concurrent-switch", response);
        Assert.That(responses.Select(response => (int)response.StatusCode).OrderBy(code => code),
            Is.EqualTo(new[] { 200, 401 }));
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var hash = AuthTokenService.HashRefreshToken(cookie["imc_refresh=".Length..]);
        var old = await db.RefreshTokens.SingleAsync(row => row.TokenHash == hash);
        var live = await db.RefreshTokens.Where(row => row.TokenHash == old.ReplacedByTokenHash && row.RevokedAtUtc == null)
            .ToListAsync();
        Assert.That(live, Has.Count.EqualTo(1));
        Assert.That(live[0].TenantId, Is.EqualTo(LiveRoom));
        TestContext.Out.WriteLine("live-refresh " + JsonSerializer.Serialize(new { count = live.Count, organizationId = live[0].TenantId }));
    }
}
