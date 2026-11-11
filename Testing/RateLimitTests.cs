using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Testing;

/// <summary>
/// The login door is the only one an unauthenticated caller can knock on, so it carries a fixed
/// window per client address. These tests raise the limit down to three so the window can be walked
/// in a second, and prove the rest of the API is not inside that window.
/// </summary>
[NonParallelizable]
[TestFixture]
public sealed class RateLimitTests
{
    [Test]
    public async Task Login_IsThrottled_AndAnswers429OnceTheWindowIsSpent()
    {
        using var factory = new ApiFactory(authPermitLimit: 3);
        using var client = factory.CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login",
                new { email = "nobody@example.test", password = "WrongPass!1" });
            statuses.Add(response.StatusCode);
        }

        Assert.Multiple(() =>
        {
            Assert.That(statuses.Take(3), Is.All.EqualTo(HttpStatusCode.Unauthorized),
                "the first attempts reach the handler and are answered as bad credentials");
            Assert.That(statuses.Skip(3), Is.All.EqualTo(HttpStatusCode.TooManyRequests),
                "once the window is spent the address is refused before the passwords are checked");
        });
    }

    [Test]
    public async Task BusinessRoutes_AreOutsideTheLoginWindow()
    {
        using var factory = new ApiFactory(authPermitLimit: 3);
        using var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "bootstrap@example.test", password = "AuthTest!Secure123" });
        Assert.That(login.StatusCode, Is.EqualTo(HttpStatusCode.OK), await login.Content.ReadAsStringAsync());
        var payload = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", payload.GetProperty("access_token").GetString());

        for (var attempt = 0; attempt < 6; attempt++)
        {
            Assert.That((await client.GetAsync("/api/item")).StatusCode, Is.EqualTo(HttpStatusCode.OK),
                "reading the inventory is not part of the login window");
        }
    }
}
