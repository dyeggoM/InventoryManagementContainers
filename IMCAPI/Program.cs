
using IMCAPI.DB;
using IMCAPI.DB.Repositories;
using IMCAPI.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
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
                            // pinning one origin only produces opaque browser failures.
                            p.SetIsOriginAllowed(_ => true);
                        }
                        else
                        {
                            p.WithOrigins(builder.Configuration["CORS:AllowedCors"]?.Split(",") ?? Array.Empty<string>());
                        }

                        p.WithMethods("POST", "PUT", "GET", "DELETE")
                         .AllowAnyHeader();
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
            builder.Services.AddDbContext<ApplicationContext>(options => options.UseSqlite(connectionString));
            //builder.Services.AddDbContext<ApplicationContext>(options => options.UseInMemoryDatabase(connectionString));

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

            app.UseCors("AllowCors");

            app.UseHttpsRedirection();

            app.UseAuthorization();


            app.MapControllers();

            app.Run();
        }
    }
}
