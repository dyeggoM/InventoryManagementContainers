using System.Net;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Testing;

/// <summary>
/// Development used to reflect any origin *with credentials*, which is how a page on the internet
/// could read this API with the tester's refresh cookie. It now reflects only the origins a dev box is
/// reached from — loopback and private networks — plus whatever the configuration names. These tests
/// pin both sides: the LAN origin that must keep working, and the public one that must not.
/// </summary>
[NonParallelizable]
[TestFixture]
public sealed class DevCorsOriginTests : ApiTestBase
{
    protected override bool SignsInAtStartup => false;

    [TestCase("http://localhost:9000", true)]
    [TestCase("http://127.0.0.1:9000", true)]
    [TestCase("http://192.168.1.50:9000", true, Description = "the phone on the workshop wifi")]
    [TestCase("http://10.0.0.7:9000", true)]
    [TestCase("http://172.16.4.4:9000", true)]
    [TestCase("http://[::1]:9000", true)]
    [TestCase("https://evil.test", false, Description = "a page on the internet must not read the API with the tester's cookie")]
    [TestCase("http://8.8.8.8:9000", false)]
    [TestCase("http://172.32.0.1:9000", false, Description = "just outside the private range")]
    public async Task OnlyLocalOriginsAreReflected(string origin, bool allowed)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/item");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization");

        var response = await Client.SendAsync(request);
        var reflected = response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values)
            ? values.SingleOrDefault()
            : null;

        Assert.That(reflected, allowed ? Is.EqualTo(origin) : Is.Null,
            allowed ? $"{origin} is a local development origin" : $"{origin} is not local and must not be reflected");
    }
}
