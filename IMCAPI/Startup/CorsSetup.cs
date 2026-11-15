namespace IMCAPI.Startup;

public static class CorsSetup
{
    public static void AddConfiguredCors(this WebApplicationBuilder builder)
    {
        var allowedOrigins = (builder.Configuration["CORS:AllowedCors"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var allowAnyOrigin = allowedOrigins.Contains("*", StringComparer.Ordinal);
        if (allowAnyOrigin)
        {
            // `*` is what an operator writes when they mean "everything". With credentials the
            // browser forbids the literal wildcard, so it has to become the predicate the policy
            // uses anyway — and it stops being an implicit decision.
            allowedOrigins = allowedOrigins.Where(origin => origin != "*").ToArray();
        }
        if (allowedOrigins.Length == 0 && !allowAnyOrigin && !builder.Environment.IsDevelopment())
        {
            // The refresh token travels in a cookie and the policy allows credentials, so an empty
            // list outside Development cannot be widened to "any origin": refuse to boot instead.
            throw new InvalidOperationException(
                "CORS:AllowedCors must name at least one origin, or '*' to accept any origin, outside Development.");
        }

        builder.Services.AddCors(confg =>
            confg.AddPolicy("AllowCors",
                p =>
                {
                    if (builder.Environment.IsDevelopment())
                    {
                        // A dev box is reached from other machines (LAN IP, dev-server port), and a
                        // pinned list turns that into an opaque browser failure — measured: the
                        // phone on the workshop network stopped seeing CORS headers the moment the
                        // list won. So Development reflects — but only origins that are this
                        // machine or its private network: reflecting any origin *with credentials*
                        // lets a page on the internet read the API with the tester's cookie.
                        p.SetIsOriginAllowed(origin =>
                            IsLocalDevelopmentOrigin(origin)
                            || allowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase));
                    }
                    else if (allowAnyOrigin)
                    {
                        p.SetIsOriginAllowed(_ => true);
                    }
                    else
                    {
                        p.WithOrigins(allowedOrigins);
                    }

                    // Every verb the routes actually use: the archive's restore is a PATCH, and a
                    // verb missing here is a call a browser refuses before it reaches the server,
                    // while curl keeps working — which is why it survives review. Pinned by
                    // Testing/CorsPreflightTests.
                    p.WithMethods("POST", "PUT", "PATCH", "GET", "DELETE")
                     .AllowAnyHeader()
                     .AllowCredentials();

                }));
    }

    /// <summary>
    /// The same reading of <c>CORS:AllowedCors</c> the policy uses, for the boot-time warning the
    /// host logs after it is built.
    /// </summary>
    internal static bool AllowsAnyOrigin(IConfiguration configuration) =>
        (configuration["CORS:AllowedCors"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains("*", StringComparer.Ordinal);

    /// <summary>
    /// True for the origins a development box is actually reached from: loopback, a private
    /// network address (RFC 1918, link-local, IPv6 unique-local) — the phone on the workshop wifi
    /// or the laptop next to it. `<origin>` is what the browser sent, so an unparseable one is not
    /// trusted.
    /// </summary>
    private static bool IsLocalDevelopmentOrigin(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (!System.Net.IPAddress.TryParse(uri.Host, out var address)) return false;
        if (System.Net.IPAddress.IsLoopback(address)) return true;

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return bytes[0] == 10
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 169 && bytes[1] == 254);
        }

        return address.IsIPv6LinkLocal || (bytes[0] & 0xFE) == 0xFC;
    }
}
