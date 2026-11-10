using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Reflection;

namespace IMCAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LogsController : ControllerBase
    {
        private readonly ILogger<LogsController> _logger;
        private readonly IHostEnvironment _env;
        public LogsController(ILogger<LogsController> logger, IHostEnvironment env)
        {
            _logger = logger;
            _env = env;
        }

        [HttpGet("{date}")]
        [Authorize(Policy = "RequiereAdmin")]
        public IActionResult Logs(string date)
        {
            var methodName = $"{GetType().Name}.{MethodBase.GetCurrentMethod()!.Name}";
            try
            {
                _logger.Log(LogLevel.Information, $"Inicio: {methodName}");

                var directoryPath = Path.Combine(_env.ContentRootPath, "Logs");
                var fileName = $"{date}_Logs.txt";
                var filePath = Path.Combine(directoryPath, fileName);

                if (!System.IO.File.Exists(filePath))
                {
                    return NotFound();
                }
                var file = System.IO.File.ReadAllText(filePath);

                return Ok(file);
            }
            catch (Exception e)
            {
                var logMessage = $"{methodName}.Message: {e.Message}" +
                                 (e.InnerException != null ? $"\n{methodName}.InnerException: {e.InnerException}" : "");
                _logger.Log(LogLevel.Error, logMessage);
                return StatusCode((int)HttpStatusCode.InternalServerError);
            }
            finally
            {
                _logger.Log(LogLevel.Information, $"Fin: {methodName}");
            }

        }
    }

}
