using System.Text.Json.Serialization;
using IMCAPI.DB;
using IMCAPI.DB.Repositories;
using IMCAPI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;

namespace IMCAPI.Startup;

public static class ApiServices
{
    public static void AddApiServices(this WebApplicationBuilder builder)
    {
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

        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<TenantScope>();
        builder.Services.AddSingleton<AuditSaveChangesInterceptor>();
        builder.Services.AddDbContext<ApplicationContext>((services, options) =>
            options.UseSqlite(connectionString)
                .AddInterceptors(services.GetRequiredService<AuditSaveChangesInterceptor>()));

        builder.Services.AddProblemDetails();
        builder.Services.AddScoped<AuthTokenService>();
        builder.Services.AddScoped<AuditLogWriter>();

        builder.Services.AddTransient(typeof(IRepository<>),typeof(BaseRepository<>));
        // Attachment bytes live on disk; only a compressed preview stays in the row.
        builder.Services.AddSingleton<AttachmentStorage>();
        builder.Services.AddScoped<ItemImporter>();
        builder.Services.AddScoped<ArchiveRetention>();
        // The clock half of the retention policy: the endpoint purges when asked, this runs daily.
        // Registered by its own type first so a test can ask for one pass instead of waiting a day.
        builder.Services.AddSingleton<ArchiveRetentionSweeper>();
        builder.Services.AddHostedService(provider => provider.GetRequiredService<ArchiveRetentionSweeper>());
        builder.Services.AddScoped<SubcategoryPlacement>();
    }
}
