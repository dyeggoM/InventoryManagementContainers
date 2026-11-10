using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using IMCAPI.DB;
using IMCAPI.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Testing;

[NonParallelizable]
[TestFixture]
public sealed class AuthTests
{
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;
    private const string Password = "AuthTest!Secure123";

    [OneTimeSetUp]
    public void Start()
    {
        _factory = new ApiFactory();
        _client = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public void Stop()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Test]
    public async Task Login_WithValidCredentials_ReturnsAccessTokenAndRefreshCookie()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = "bootstrap@example.test", password = Password });
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(payload.GetProperty("access_token").GetString(), Is.Not.Empty);
            Assert.That(response.Headers.GetValues("Set-Cookie").Single(), Does.Contain("httponly").And.Contain("samesite=lax"));
        });
    }

    [Test]
    public async Task Refresh_RotatesCookieAndPersistsOnlyTokenHashes()
    {
        using var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "bootstrap@example.test", password = Password });
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var oldCookie = CookieValue(login);

        var refresh = await client.PostAsync("/api/auth/refresh", content: null);
        Assert.That(refresh.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var newCookie = CookieValue(refresh);
        Assert.That(newCookie, Is.Not.EqualTo(oldCookie));

        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var storedTokens = (await context.RefreshTokens.OrderBy(token => token.Id).ToListAsync()).TakeLast(2).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(storedTokens.Length, Is.EqualTo(2));
            Assert.That(storedTokens[0].TokenHash, Is.EqualTo(AuthTokenService.HashRefreshToken(oldCookie)));
            Assert.That(storedTokens[0].RevokedAtUtc, Is.Not.Null);
            Assert.That(storedTokens[0].ReplacedByTokenHash, Is.EqualTo(storedTokens[1].TokenHash));
            Assert.That(storedTokens[1].TokenHash, Is.EqualTo(AuthTokenService.HashRefreshToken(newCookie)));
        });
    }

    [Test]
    public async Task Logout_RevokesRefreshToken()
    {
        using var client = _factory.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/login", new { email = "bootstrap@example.test", password = Password })).EnsureSuccessStatusCode();
        Assert.That((await client.PostAsync("/api/auth/logout", content: null)).StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        Assert.That((await client.PostAsync("/api/auth/refresh", content: null)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    private static string CookieValue(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single().Split(';', 2)[0].Split('=', 2)[1];

    [Test]
    public async Task Login_WithInvalidCredentials_ReturnsUnauthorized()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { email = "bootstrap@example.test", password = "wrong" });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task ProtectedItemRoute_WithoutToken_ReturnsUnauthorized()
    {
        using var anonymous = _factory.CreateClient();
        Assert.That((await anonymous.GetAsync("/api/item")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task AdminRoute_WithOperatorToken_ReturnsForbidden()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var user = new IdentityUser { UserName = "operator@example.test", Email = "operator@example.test", EmailConfirmed = true };
        var created = await userManager.CreateAsync(user, Password);
        Assert.That(created.Succeeded, Is.True, string.Join("; ", created.Errors.Select(e => e.Description)));
        Assert.That((await userManager.AddToRoleAsync(user, "operador")).Succeeded, Is.True);

        using var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = user.Email, password = Password });
        var payload = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", payload.GetProperty("access_token").GetString());
        Assert.That((await client.PostAsJsonAsync("/api/constants", new { label = "Nope" })).StatusCode,
            Is.EqualTo(HttpStatusCode.Forbidden));
    }

    [Test]
    public async Task DeclaredAnonymousRoute_WithoutToken_ReturnsOk()
    {
        using var anonymous = _factory.CreateClient();
        Assert.That((await anonymous.GetAsync("/api/constants")).StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    [Test]
    public void AnonymousEndpoints_MatchTheAllowAnonymousManifest()
    {
        var source = _factory.Services.GetRequiredService<EndpointDataSource>();
        var actual = source.Endpoints
            .Where(endpoint => endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null
                || !endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any())
            .OfType<RouteEndpoint>()
            .SelectMany(endpoint =>
            {
                var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? new[] { "*" };
                return methods.Select(method => $"{method} /{endpoint.RoutePattern.RawText}".ToLowerInvariant());
            })
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var manifestPath = Path.Combine(AppContext.BaseDirectory, "endpoints.allow-anonymous.txt");
        var declared = File.ReadAllLines(manifestPath)
            .Select(line => line.Trim().ToLowerInvariant())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.That(actual, Is.EquivalentTo(declared));
    }

    [Test]
    public async Task StartupInvariant_RejectsMoreThanOneSuperadmin()
    {
        using var scope = _factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var duplicate = new IdentityUser { UserName = "another-root@example.test", Email = "another-root@example.test", EmailConfirmed = true };
        Assert.That((await userManager.CreateAsync(duplicate, Password)).Succeeded, Is.True);
        Assert.That((await userManager.AddToRoleAsync(duplicate, "superadmin")).Succeeded, Is.True);

        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await DatabaseSeeder.EnsureSingleSuperadminAsync(userManager));
    }
}
