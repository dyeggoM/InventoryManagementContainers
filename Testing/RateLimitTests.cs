using System.Net;
using System.Net.Http.Json;

namespace Testing;

/// <summary>
/// The login door is the only one an unauthenticated caller can knock on, so it carries a fixed
/// window per client address. These tests raise the limit down to three so the window can be walked
/// in a second, and prove the rest of the API is not inside that window.
///
/// The window is spent by the first test that walks it, so each test raises an API of its own — the
/// reason this fixture overrides the factory and the per-test lifetime instead of taking the base's.
/// </summary>
[NonParallelizable]
[TestFixture]
public sealed class RateLimitTests : ApiTestBase
{
    protected override bool SignsInAtStartup => false;

    protected override bool FreshApiPerTest => true;

    protected override ApiFactory CreateFactory() => new(authPermitLimit: 3);

    [Test]
    public async Task Login_IsThrottled_AndAnswers429OnceTheWindowIsSpent()
    {
        var statuses = new List<HttpStatusCode>();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var response = await Client.PostAsJsonAsync("/api/auth/login",
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
        await Authenticate(Client, SuperadminEmail);

        for (var attempt = 0; attempt < 6; attempt++)
        {
            Assert.That((await Client.GetAsync("/api/item")).StatusCode, Is.EqualTo(HttpStatusCode.OK),
                "reading the inventory is not part of the login window");
        }
    }
}
