using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using IMCAPI.DB;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Testing;

/// <summary>
/// The authorization matrix in [SECURITY_AUTH_MATRIX.md](../../SECURITY_AUTH_MATRIX.md), executed
/// instead of described: every protected route is called with no credential and with each role below
/// its minimum, and the token/refresh abuse cases that a single happy-path test never reaches.
///
/// Role ladder used here: superadmin > admin > operador > lector. A route's minimum role is the one
/// the matrix declares for it; the fixture logs in as each role once and reuses those clients.
/// </summary>
[NonParallelizable]
[TestFixture]
public sealed class AuthMatrixTests
{
    private const string Password = "AuthTest!Secure123";
    // Same values ApiFactory injects, so tokens minted here validate against the running app.
    private const string SigningKey = "test-signing-key-with-at-least-32-bytes-123456";
    private const string Issuer = "InventoryManagementContainers.Tests";

    private ApiFactory _factory = null!;
    private HttpClient _anonymous = null!;
    private HttpClient _lector = null!;
    private HttpClient _operador = null!;
    private HttpClient _admin = null!;
    private HttpClient _superadmin = null!;

    /// <summary>Verb, path and the cheapest role that may reach it.</summary>
    private static readonly (string Method, string Path, string Minimum)[] Matrix =
    [
        ("GET", "/api/item", "lector"),
        ("GET", "/api/item/archive", "superadmin"),
        ("GET", "/api/item/1", "lector"),
        ("POST", "/api/item", "operador"),
        ("PUT", "/api/item/1", "operador"),
        ("DELETE", "/api/item/1", "admin"),
        ("PATCH", "/api/item/1/activate", "admin"),
        ("GET", "/api/attachment", "lector"),
        ("GET", "/api/attachment/1", "lector"),
        ("GET", "/api/attachment/1/file", "lector"),
        ("GET", "/api/attachment/item/1", "lector"),
        ("POST", "/api/attachment", "operador"),
        ("PUT", "/api/attachment/1", "operador"),
        ("DELETE", "/api/attachment/1", "operador"),
        ("POST", "/api/attachment/upload", "operador"),
        ("GET", "/api/logs/20260101", "admin"),
        ("POST", "/api/constants", "admin"),
        ("PUT", "/api/constants/1", "admin"),
        ("DELETE", "/api/constants/1", "admin"),
    ];

    [OneTimeSetUp]
    public async Task Start()
    {
        _factory = new ApiFactory();
        _anonymous = _factory.CreateClient();
        _lector = await LoginAs("lector@example.test", "lector");
        _operador = await LoginAs("operador@example.test", "operador");
        _admin = await LoginAs("admin@example.test", "admin");
        _superadmin = _factory.CreateClient();
        var rootLogin = await _superadmin.PostAsJsonAsync("/api/auth/login", new { email = "bootstrap@example.test", password = Password });
        Assert.That(rootLogin.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var rootPayload = await rootLogin.Content.ReadFromJsonAsync<JsonElement>();
        _superadmin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", rootPayload.GetProperty("access_token").GetString());
    }

    [OneTimeTearDown]
    public void Stop()
    {
        // Listed one by one: the NUnit analyzer only credits a direct Dispose call here.
        _anonymous.Dispose();
        _lector.Dispose();
        _operador.Dispose();
        _admin.Dispose();
        _superadmin.Dispose();
        _factory.Dispose();
    }

    /// <summary>Creates the account with that role and returns a client already carrying its token.</summary>
    private async Task<HttpClient> LoginAs(string email, string role)
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            var created = await users.CreateAsync(user, Password);
            Assert.That(created.Succeeded, Is.True, string.Join("; ", created.Errors.Select(error => error.Description)));
            Assert.That((await users.AddToRoleAsync(user, role)).Succeeded, Is.True);
        }

        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password = Password });
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"login as {role}");
        var payload = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", payload.GetProperty("access_token").GetString());
        return client;
    }

    private static HttpRequestMessage Request(string method, string path) => method switch
    {
        "GET" => new HttpRequestMessage(HttpMethod.Get, path),
        // A body is sent because a 415/400 would hide the 401/403 this test is about: authorization
        // runs before model binding, so the status still has to be the authorization one.
        "POST" => new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = path.EndsWith("/upload", StringComparison.Ordinal)
                ? new MultipartFormDataContent { { new StringContent("999999"), "itemId" }, { new ByteArrayContent([1, 2, 3]), "file", "probe.bin" } }
                : JsonContent.Create(new { label = "matrix", icon = "", parentId = (int?)null, attributes = "{}" }),
        },
        "PUT" => new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = JsonContent.Create(new { id = 1, label = "matrix", icon = "", parentId = (int?)null, attributes = "{}" }),
        },
        "DELETE" => new HttpRequestMessage(HttpMethod.Delete, path),
        "PATCH" => new HttpRequestMessage(HttpMethod.Patch, path),
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
    };

    private static IEnumerable<string> RolesBelow(string minimum) => minimum switch
    {
        "lector" => [],
        "operador" => ["lector"],
        "admin" => ["lector", "operador"],
        "superadmin" => ["lector", "operador", "admin"],
        _ => [],
    };

    [Test]
    public async Task EveryProtectedRoute_WithoutACredential_IsUnauthorized()
    {
        var failures = new List<string>();
        foreach (var (method, path, _) in Matrix)
        {
            using var request = Request(method, path);
            var response = await _anonymous.SendAsync(request);
            if (response.StatusCode != HttpStatusCode.Unauthorized)
                failures.Add($"{method} {path} -> {(int)response.StatusCode}");
        }

        Assert.That(failures, Is.Empty,
            "these routes answered a caller with no token and are not in endpoints.allow-anonymous.txt: "
            + string.Join(", ", failures));
    }

    [Test]
    public async Task EveryProtectedRoute_RejectsTheRolesBelowItsMinimum()
    {
        var clients = new Dictionary<string, HttpClient>
        {
            ["lector"] = _lector,
            ["operador"] = _operador,
            ["admin"] = _admin,
        };

        var failures = new List<string>();
        foreach (var (method, path, minimum) in Matrix)
        {
            foreach (var role in RolesBelow(minimum))
            {
                using var request = Request(method, path);
                var response = await clients[role].SendAsync(request);
                if (response.StatusCode != HttpStatusCode.Forbidden)
                    failures.Add($"{method} {path} as {role} (needs {minimum}) -> {(int)response.StatusCode}");
            }
        }

        Assert.That(failures, Is.Empty,
            "a role reached a route above its level: " + string.Join(", ", failures));
    }

    [Test]
    public async Task EveryProtectedRoute_AcceptsTheRoleAtItsMinimum()
    {
        var clients = new Dictionary<string, HttpClient>
        {
            ["lector"] = _lector,
            ["operador"] = _operador,
            ["admin"] = _admin,
            ["superadmin"] = _superadmin,
        };

        var failures = new List<string>();
        foreach (var (method, path, minimum) in Matrix)
        {
            // upload/attachment paths need a real record; a 4xx that is not 401/403 still proves the
            // request got past authorization, which is what the matrix decides.
            using var request = Request(method, path);
            var response = await clients[minimum].SendAsync(request);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                failures.Add($"{method} {path} as {minimum} -> {(int)response.StatusCode}");
        }

        Assert.That(failures, Is.Empty,
            "the minimum role declared in the matrix was refused: " + string.Join(", ", failures));
    }

    [Test]
    public async Task TokenSignedWithAnotherKey_IsRejected()
    {
        var forged = MintToken(SigningKey: new string('k', 64), expires: DateTime.UtcNow.AddMinutes(10), role: "superadmin");
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", forged);

        Assert.That((await client.GetAsync("/api/item")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task ExpiredToken_IsRejected()
    {
        var expired = MintToken(SigningKey, DateTime.UtcNow.AddMinutes(-10), "superadmin");
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expired);

        Assert.That((await client.GetAsync("/api/item")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task TamperedToken_IsRejected()
    {
        var valid = MintToken(SigningKey, DateTime.UtcNow.AddMinutes(10), "superadmin");
        // Flip one character of the signature: the header and payload stay valid JSON.
        var parts = valid.Split('.');
        var lastChar = parts[2][^1];
        parts[2] = parts[2][..^1] + (lastChar == 'A' ? 'B' : 'A');

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", string.Join('.', parts));

        Assert.That((await client.GetAsync("/api/item")).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task RotatedRefreshToken_CannotBeReplayed()
    {
        using var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin@example.test", password = Password });
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var firstCookie = login.Headers.GetValues("Set-Cookie").Single().Split(';', 2)[0];

        var refresh = await client.PostAsync("/api/auth/refresh", content: null);
        Assert.That(refresh.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Replay the token that the rotation just retired, on a client that carries nothing else.
        using var replayer = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add("Cookie", firstCookie);
        Assert.That((await replayer.SendAsync(request)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            "a rotated refresh token must not mint a new session");
    }

    [Test]
    public async Task Refresh_WithoutACookie_IsUnauthorized()
    {
        using var client = _factory.CreateClient();
        Assert.That((await client.PostAsync("/api/auth/refresh", content: null)).StatusCode,
            Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task Login_ForAnAccountWithNoRole_CanStillNotReadBusinessData()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
            var user = new IdentityUser { UserName = "norole@example.test", Email = "norole@example.test", EmailConfirmed = true };
            Assert.That((await users.CreateAsync(user, Password)).Succeeded, Is.True);
        }

        using var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "norole@example.test", password = Password });
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK), "credentials are valid, the account simply has no role");
        var payload = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", payload.GetProperty("access_token").GetString());

        Assert.That((await client.GetAsync("/api/item")).StatusCode, Is.EqualTo(HttpStatusCode.Forbidden),
            "an authenticated account with no role gets 403, not the inventory");
    }

    private static string MintToken(string SigningKey, DateTime expires, string role)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(SigningKey));
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "forged-user"),
            new Claim(ClaimTypes.Name, "forged@example.test"),
            new Claim(ClaimTypes.Role, role),
        };
        var token = new JwtSecurityToken(Issuer, Issuer, claims, notBefore: expires.AddMinutes(-5), expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
