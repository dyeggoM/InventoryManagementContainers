
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
            builder.Services.AddCors(confg =>
                confg.AddPolicy("AllowCors",
                    p =>
                    {
                        if (builder.Environment.IsDevelopment())
                        {
                            // A dev box is reached from other machines (LAN IP, dev-server port), so
                            // pinning one origin only produces opaque browser failures. Credentials
                            // are allowed because the refresh token travels in an HttpOnly cookie:
                            // without AllowCredentials the browser throws away that Set-Cookie on a
                            // cross-origin call, and the silent refresh the PWA relies on never works.
                            p.SetIsOriginAllowed(_ => true);
                        }
                        else
                        {
                            p.WithOrigins(builder.Configuration["CORS:AllowedCors"]?.Split(",") ?? Array.Empty<string>());
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
            builder.Services.AddScoped<AuthTokenService>();
            builder.Services.AddScoped<AuditLogWriter>();


            #endregion

            #region Services
            builder.Services.AddTransient(typeof(IRepository<>),typeof(BaseRepository<>));
            // Attachment bytes live on disk; only a compressed preview stays in the row.
            builder.Services.AddSingleton<AttachmentStorage>();
            #endregion

            var app = builder.Build();

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

            app.UseAuthentication();
            app.Use(async (context, next) =>
            {
                var isImpersonating = context.User.HasClaim(claim => claim.Type == "impersonated_user_id");
                var isStopImpersonation = string.Equals(context.Request.Path.Value?.TrimEnd('/'),
                    "/api/auth/stop-impersonation", StringComparison.OrdinalIgnoreCase);
                var isSafeMethod = HttpMethods.IsGet(context.Request.Method)
                    || HttpMethods.IsHead(context.Request.Method) || HttpMethods.IsOptions(context.Request.Method);
                if (isImpersonating && !isSafeMethod && !isStopImpersonation)
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
                var impersonating = context.User.HasClaim(claim => claim.Type == "impersonated_user_id");
                return (!impersonating || allowImpersonationRead) && (!enforce || roles.Any(context.User.IsInRole));
            }));
        }
    }
}
