
using IMCAPI.Services;
using IMCAPI.Startup;

namespace IMCAPI
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(new FileLoggerProvider(builder.Environment));

            builder.AddConfiguredCors();
            builder.AddApiServices();
            builder.AddJwtAuthentication();
            builder.AddAppAuthorization();
            builder.AddAuthRateLimiting();

            var app = builder.Build();

            app.LogConfigurationWarnings();

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
            app.UseReadOnlyImpersonation();
            app.UseAuthorization();

            app.MapControllers();

            await app.RunStartupTasksAsync();

            app.Run();
        }
    }
}
