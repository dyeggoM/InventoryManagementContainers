namespace IMCAPI.Startup;

public static class ImpersonationSetup
{
    /// <summary>
    /// A borrowed token is read-only: anything that would change state is refused before it reaches
    /// a controller, with the two doors that have to stay open (ending the borrow and refreshing the
    /// caller's own token).
    /// </summary>
    public static void UseReadOnlyImpersonation(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var isImpersonating = context.User.HasClaim(claim => claim.Type == "act");
            var path = context.Request.Path.Value?.TrimEnd('/');
            var isStopImpersonation = string.Equals(path, "/api/auth/stop-impersonation",
                StringComparison.OrdinalIgnoreCase);
            // A non-browser client that refreshes with the borrowed bearer in hand must not be
            // answered 403: the refresh trades the cookie for the caller's own token.
            var isRefresh = string.Equals(path, "/api/auth/refresh", StringComparison.OrdinalIgnoreCase);
            var isSafeMethod = HttpMethods.IsGet(context.Request.Method)
                || HttpMethods.IsHead(context.Request.Method) || HttpMethods.IsOptions(context.Request.Method);
            if (isImpersonating && !isSafeMethod && !isStopImpersonation && !isRefresh)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            await next();
        });
    }
}
