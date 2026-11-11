
using IMCAPI.DB;
using IMCAPI.DB.Repositories;
using IMCAPI.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using System.Text.Json.Serialization;

namespace IMCAPI
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(new FileLoggerProvider(builder.Environment));

            #region CORS
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
                            // list won. Development reflects; a deployment pins the list or says `*`.
                            p.SetIsOriginAllowed(_ => true);
                        }
                        else if (allowAnyOrigin)
                        {
                            p.SetIsOriginAllowed(_ => true);
                        }
                        else
                        {
                            p.WithOrigins(allowedOrigins);
                        }

                        p.WithMethods("POST", "PUT", "GET", "DELETE")
                         .AllowAnyHeader()
                         .AllowCredentials();

                    }));
            #endregion

            builder.Services.AddControllers().AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
                options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
            });
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c =>
            {
                //c.AddSecurityDefinition(JwtBearerDefaults.AuthenticationScheme, new OpenApiSecurityScheme
                //{
                //    Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
                //    Name = "Bearer",
                //    In = ParameterLocation.Header,
                //    Type = SecuritySchemeType.Http,
                //    Scheme = JwtBearerDefaults.AuthenticationScheme,
                //    BearerFormat = "JTW"
                //});
                //c.AddSecurityRequirement(new OpenApiSecurityRequirement {
                //    {
                //        new OpenApiSecurityScheme
                //        {
                //            Reference = new OpenApiReference
                //            {
                //                Type = ReferenceType.SecurityScheme,
                //                Id = JwtBearerDefaults.AuthenticationScheme,
                //            }
                //        },
                //        new string[] { }
                //    }
                //});
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "IMCAPI", Version = "v1" });
            });

            #region DB
            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddSingleton<AuditSaveChangesInterceptor>();
            builder.Services.AddDbContext<ApplicationContext>((services, options) =>
                options.UseSqlite(connectionString)
                    .AddInterceptors(services.GetRequiredService<AuditSaveChangesInterceptor>()));

            var jwtKey = builder.Configuration["Auth:Jwt:Key"] ?? Environment.GetEnvironmentVariable("AUTH_JWT_KEY")
                ?? throw new InvalidOperationException("Configure Auth:Jwt:Key or AUTH_JWT_KEY.");
            var jwtIssuer = builder.Configuration["Auth:Jwt:Issuer"] ?? Environment.GetEnvironmentVariable("AUTH_JWT_ISSUER")
                ?? throw new InvalidOperationException("Configure Auth:Jwt:Issuer or AUTH_JWT_ISSUER.");
            if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
                throw new InvalidOperationException("Auth:Jwt:Key must contain at least 32 UTF-8 bytes.");
            var authEnforce = !bool.TryParse(builder.Configuration["Auth:Enforce"], out var configuredEnforce) || configuredEnforce;

            builder.Services.AddIdentityCore<IdentityUser>(options => options.User.RequireUniqueEmail = true)
                .AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<ApplicationContext>()
                .AddSignInManager()
                .AddDefaultTokenProviders();
            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = jwtIssuer,
                        ValidateAudience = true,
                        ValidAudience = jwtIssuer,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.FromSeconds(30),
                        NameClaimType = System.Security.Claims.ClaimTypes.Name,
                        RoleClaimType = System.Security.Claims.ClaimTypes.Role,
                    };
                    options.Events = new JwtBearerEvents
                    {
                        OnTokenValidated = async context =>
                        {
                            var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationContext>();
                            var principal = context.Principal!;
                            var subjectId = principal.FindFirst("sub")?.Value
                                ?? principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                            var actorId = principal.FindFirst("act")?.Value;
                            var stoppingImpersonation = string.Equals(context.Request.Path.Value?.TrimEnd('/'),
                                "/api/auth/stop-impersonation", StringComparison.OrdinalIgnoreCase);
                            var ids = (stoppingImpersonation && actorId is not null ? new[] { actorId }
                                : new[] { subjectId, actorId })
                                .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToArray();
                            var inactive = await db.UserProfiles.AsNoTracking()
                                .AnyAsync(profile => ids.Contains(profile.UserId) && !profile.IsActive);
                            if (inactive) context.Fail("The user account is inactive.");

                            // A rotated security stamp (password reset, forced sign-out) has to kill the
                            // tokens that were minted against the old one. Without this a reset revokes
                            // the refresh tokens and leaves every live access token working.
                            var stamp = principal.FindFirst("sstamp")?.Value;
                            if (stamp is not null && subjectId is not null)
                            {
                                var users = context.HttpContext.RequestServices
                                    .GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<Microsoft.AspNetCore.Identity.IdentityUser>>();
                                var subject = await users.FindByIdAsync(subjectId);
                                if (subject is null
                                    || !string.Equals(subject.SecurityStamp, stamp, StringComparison.Ordinal))
                                    context.Fail("The session is no longer valid.");
                            }
                        }
                    };
                });
            builder.Services.AddAuthorization(options =>
            {
                AddRolePolicy(options, "RequiereLectura", ["superadmin", "admin", "operador", "lector"], authEnforce, allowImpersonationRead: true);
                AddRolePolicy(options, "RequiereEscritura", ["superadmin", "admin", "operador"], authEnforce);
                AddRolePolicy(options, "RequiereAdmin", ["superadmin", "admin"], authEnforce);
                AddRolePolicy(options, "RequiereSuperadmin", ["superadmin"], authEnforce);
            });
            builder.Services.AddProblemDetails();

            // The login form is the one door an unauthenticated caller can knock on, and until now the
            // only brake was Identity's lockout. A fixed window per client address is enough to stop a
            // credential-stuffing script without punishing a shared workshop network: the limit is
            // configuration, and the suite raises it so a run is never throttled.
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
            builder.Services.AddScoped<AuthTokenService>();
            builder.Services.AddScoped<AuditLogWriter>();


            #endregion

            #region Services
            builder.Services.AddTransient(typeof(IRepository<>),typeof(BaseRepository<>));
            // Attachment bytes live on disk; only a compressed preview stays in the row.
            builder.Services.AddSingleton<AttachmentStorage>();
            #endregion

            var app = builder.Build();

            // Configuration that quietly removes a control has to say so in the log the operator reads.
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
            if (builder.Environment.IsDevelopment() || allowAnyOrigin)
            {
                app.Logger.LogWarning(
                    "CORS accepts any origin with credentials enabled ({Reason}). Pin CORS:AllowedCors " +
                    "before this leaves a dev box.",
                    allowAnyOrigin ? "CORS:AllowedCors is '*'" : "Development");
            }

            #region Migrations
            using (var serviceScope = app.Services.CreateScope())
            {
                var context = serviceScope.ServiceProvider.GetService<ApplicationContext>();
                context?.Database.Migrate();

                // The database is not in source control: build the configuration tree on first run.
                if (context is not null)
                {
                    DatabaseSeeder.SeedConfiguration(context, app.Environment.ContentRootPath);
                    var userManager = serviceScope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
                    var roleManager = serviceScope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                    DatabaseSeeder.SeedIdentityAsync(userManager, roleManager, app.Configuration).GetAwaiter().GetResult();
                    DatabaseSeeder.EnsureUserProfilesAsync(context, userManager).GetAwaiter().GetResult();

                    // Older rows carry the whole image; move it to disk once.
                    var storage = serviceScope.ServiceProvider.GetRequiredService<AttachmentStorage>();
                    DatabaseSeeder.BackfillAttachmentsAsync(context, storage).GetAwaiter().GetResult();

                    // Attribute definitions predate typed values; give them sensible types once.
                    DatabaseSeeder.BackfillAttributeTypesAsync(context).GetAwaiter().GetResult();
                }
            }
            #endregion

            if (app.Environment.IsDevelopment())
            {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler();
            }

            app.UseCors("AllowCors");

            app.UseHttpsRedirection();

            app.UseRateLimiter();

            app.UseAuthentication();
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
            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
        private static void AddRolePolicy(Microsoft.AspNetCore.Authorization.AuthorizationOptions options,
            string name, string[] roles, bool enforce, bool allowImpersonationRead = false)
        {
            options.AddPolicy(name, policy => policy.RequireAssertion(context =>
            {
                var impersonating = context.User.HasClaim(claim => claim.Type == "act");
                return (!impersonating || allowImpersonationRead) && (!enforce || roles.Any(context.User.IsInRole));
            }));
        }
    }
}
