using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IMCAPI.DB;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Testing;

/// <summary>
/// Two things the token path must not depend on: that a token carries the claims the validation
/// expects, and that only one caller can close a borrow.
///
/// A token written by hand — with a leaked key, or by an older build — used to skip the security-stamp
/// check simply by omitting the claim, so a password reset did not end it. And two stops arriving
/// together both closed the same borrow and both wrote an end row to the audit.
/// </summary>
[NonParallelizable]
[TestFixture]
public sealed class TokenHardeningTests : ApiTestBase
{
    private const string Key = "test-signing-key-with-at-least-32-bytes-123456";
    private const string Issuer = "InventoryManagementContainers.Tests";

    /// <summary>
    /// A token this API did not mint: same key, same issuer, plausible claims — and whatever the test
    /// asks for.
    /// </summary>
    private static string Forge(string subject, string? stamp, string role = "superadmin")
    {
        static string B64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var header = B64(Encoding.UTF8.GetBytes("""{"alg":"HS256","typ":"JWT"}"""));
        var claims = new Dictionary<string, object>
        {
            ["sub"] = subject,
            ["org"] = "900001",
            ["iss"] = Issuer,
            ["aud"] = Issuer,
            ["exp"] = DateTimeOffset.UtcNow.AddMinutes(15).ToUnixTimeSeconds(),
            ["http://schemas.microsoft.com/ws/2008/06/identity/claims/role"] = role
        };
        if (stamp is not null) claims["sstamp"] = stamp;
        var payload = B64(JsonSerializer.SerializeToUtf8Bytes(claims));
        var signature = B64(new HMACSHA256(Encoding.UTF8.GetBytes(Key)).ComputeHash(Encoding.UTF8.GetBytes($"{header}.{payload}")));
        return $"{header}.{payload}.{signature}";
    }

    private async Task<(string Subject, string Stamp)> BootstrapClaims()
    {
        using var scope = PersonaScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var user = await context.Users.SingleAsync(row => row.Email == "bootstrap@example.test");
        return (user.Id, user.SecurityStamp!);
    }

    [Test]
    public async Task ATokenWithoutTheSecurityStamp_IsRefused()
    {
        var (subject, stamp) = await BootstrapClaims();

        // The control: the same forged token, with the stamp the account has, authenticates.
        using var withStamp = new HttpRequestMessage(HttpMethod.Get, "/api/item");
        withStamp.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Forge(subject, stamp));
        Assert.That((await Client.SendAsync(withStamp)).StatusCode, Is.EqualTo(HttpStatusCode.OK),
            "the forging itself is not what is being tested: a complete token works");

        using var without = new HttpRequestMessage(HttpMethod.Get, "/api/item");
        without.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Forge(subject, stamp: null));
        Assert.That((await Client.SendAsync(without)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            "a token that omits the stamp would skip the check that a password reset relies on");

        using var empty = new HttpRequestMessage(HttpMethod.Get, "/api/item");
        empty.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Forge(subject, string.Empty));
        Assert.That((await Client.SendAsync(empty)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized),
            "an empty stamp is not a stamp");
    }

    [Test]
    public async Task AStaleStamp_IsRefused()
    {
        var (subject, _) = await BootstrapClaims();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/item");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Forge(subject, "stamp-from-before-the-reset"));
        Assert.That((await Client.SendAsync(request)).StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task TwoStopsAtOnce_EndTheBorrowOnce_AndAuditItOnce()
    {
        var start = await Client.PostAsJsonAsync("/api/users/impersonate", new { role = "lector" });
        var payload = await start.Content.ReadFromJsonAsync<JsonElement>(Json);
        var borrowed = payload.GetProperty("access_token").GetString()!;
        var sessionId = payload.GetProperty("impersonation_session").GetString()!;

        // The actor's cookie is already in the handler's cookie jar from the login in Start().
        using var one = new HttpRequestMessage(HttpMethod.Post, "/api/auth/stop-impersonation");
        using var two = new HttpRequestMessage(HttpMethod.Post, "/api/auth/stop-impersonation");
        one.Headers.Authorization = new AuthenticationHeaderValue("Bearer", borrowed);
        two.Headers.Authorization = new AuthenticationHeaderValue("Bearer", borrowed);

        var responses = await Task.WhenAll(Client.SendAsync(one), Client.SendAsync(two));

        using var scope = PersonaScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationContext>();
        var ends = await context.AuditLogs.CountAsync(row =>
            row.Action == "ImpersonateEnd" && row.ImpersonationSessionId == sessionId);
        var session = await context.ImpersonationSessions.SingleAsync(row => row.Id == sessionId);

        Assert.Multiple(() =>
        {
            Assert.That(ends, Is.EqualTo(1), "the borrow ends once, so it is audited once");
            Assert.That(session.EndedAtUtc, Is.Not.Null);
            Assert.That(responses.Count(response => response.IsSuccessStatusCode), Is.LessThanOrEqualTo(1),
                "both answers cannot be the same success");
        });
    }
}
