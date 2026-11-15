namespace IMCAPI.Startup;

public static class BootWarnings
{
    /// <summary>
    /// Configuration that quietly removes a control has to say so in the log the operator reads.
    /// </summary>
    public static void LogConfigurationWarnings(this WebApplication app)
    {
        var authEnforce = AuthorizationSetup.IsEnforced(app.Configuration);
        var allowAnyOrigin = CorsSetup.AllowsAnyOrigin(app.Configuration);

        if (!authEnforce)
        {
            app.Logger.LogWarning(
                "Auth:Enforce is FALSE: every role policy is a no-op and any authenticated caller can " +
                "perform any action. This is a local diagnostic switch, never a deployment setting.");
            if (!app.Environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    "Auth:Enforce=false is refused outside Development: it disables every role check.");
            }
        }
        if (app.Environment.IsDevelopment() || allowAnyOrigin)
        {
            app.Logger.LogWarning(
                "CORS accepts any origin with credentials enabled ({Reason}). Pin CORS:AllowedCors " +
                "before this leaves a dev box.",
                allowAnyOrigin ? "CORS:AllowedCors is '*'" : "Development");
        }
    }
}
