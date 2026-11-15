namespace IMCAPI.Startup;

public static class RateLimitingSetup
{
    // The login form is the one door an unauthenticated caller can knock on, and until now the
    // only brake was Identity's lockout. A fixed window per client address is enough to stop a
    // credential-stuffing script without punishing a shared workshop network: the limit is
    // configuration, and the suite raises it so a run is never throttled.
    public static void AddAuthRateLimiting(this WebApplicationBuilder builder)
    {
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("auth", context =>
            {
                // Read from the request's configuration, not from the builder: a test host adds its
                // own settings after this line runs, and limits captured here would ignore them.
                var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
                var permitLimit = configuration.GetValue<int?>("Auth:RateLimit:PermitLimit") ?? 10;
                var windowSeconds = configuration.GetValue<int?>("Auth:RateLimit:WindowSeconds") ?? 60;
                return System.Threading.RateLimiting.RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new System.Threading.RateLimiting.FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = TimeSpan.FromSeconds(windowSeconds),
                        QueueLimit = 0,
                    });
            });
        });
    }
}
