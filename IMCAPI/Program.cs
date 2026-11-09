
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
            #endregion

            var app = builder.Build();

            #region Migrations
            using (var serviceScope = app.Services.CreateScope())
            {
                var context = serviceScope.ServiceProvider.GetService<ApplicationContext>();
                context?.Database.Migrate();
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
