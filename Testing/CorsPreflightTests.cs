using System.Net;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Testing;

/// <summary>
/// The browser is stricter than curl: a verb missing from <c>Access-Control-Allow-Methods</c> makes
/// the preflight fail and the real call never leaves the page, while the same call from the command
/// line succeeds — which is exactly how a missing PATCH survived review. This test takes the verbs
/// from the routes the API actually exposes and asks the CORS policy about each one, so adding a verb
/// to a controller without teaching the policy fails here instead of in a browser console.
/// </summary>
[NonParallelizable]
[TestFixture]
public sealed class CorsPreflightTests
{
    private ApiFactory _factory = null!;

    [OneTimeSetUp]
    public void Start()
    {
        _factory = new ApiFactory();
        // Build the host before touching Services, so it opens this run's throwaway database.
        using var warmup = _factory.CreateClient();
    }

    [OneTimeTearDown]
    public void Stop() => _factory.Dispose();

    [Test]
    public async Task EveryVerbTheApiRoutesUses_SurvivesThePreflight()
    {
        var provider = _factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>();
        var verbs = provider.ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .SelectMany(action => action.ActionConstraints?.OfType<HttpMethodActionConstraint>()
                ?? Enumerable.Empty<HttpMethodActionConstraint>())
            .SelectMany(constraint => constraint.HttpMethods)
            .Where(method => !method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase)
                && !method.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.That(verbs, Is.Not.Empty, "the app exposes no routed verbs at all?");

        using var client = _factory.CreateClient();
        var refused = new List<string>();

        foreach (var verb in verbs)
        {
            using var request = new HttpRequestMessage(HttpMethod.Options, "/api/item");
            request.Headers.Add("Origin", "http://localhost:9000");
            request.Headers.Add("Access-Control-Request-Method", verb);
            request.Headers.Add("Access-Control-Request-Headers", "authorization");

            var response = await client.SendAsync(request);
            var allowed = response.Headers.TryGetValues("Access-Control-Allow-Methods", out var values)
                ? string.Join(",", values)
                : string.Empty;

            if (!allowed.Contains(verb, StringComparison.OrdinalIgnoreCase)) refused.Add(verb);
        }

        Assert.That(refused, Is.Empty,
            "these verbs are routed but a browser would refuse them: " + string.Join(", ", refused));
    }

    [Test]
    public async Task ThePreflightAnswersTheOriginAndTheHeadersTheAppSends()
    {
        using var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/item/1/activate");
        request.Headers.Add("Origin", "http://localhost:9000");
        request.Headers.Add("Access-Control-Request-Method", "PATCH");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");

        var response = await client.SendAsync(request);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(response.Headers.GetValues("Access-Control-Allow-Methods").Single(), Does.Contain("PATCH"),
                "the archive's restore is a PATCH: without it, Restaurar fails in the browser only");
            Assert.That(response.Headers.GetValues("Access-Control-Allow-Origin").Single(),
                Is.EqualTo("http://localhost:9000"));
        });
    }
}
