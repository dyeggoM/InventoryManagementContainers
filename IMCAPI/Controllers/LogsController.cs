using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
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

                // The segment is part of a path, so it is parsed, not trusted: a date shaped like a
                // path (`..`, an encoded separator, a drive letter) must never reach Path.Combine.
                if (!DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var parsed))
                {
                    return BadRequest("The date must be formatted yyyy-MM-dd.");
                }

                var directoryPath = Path.GetFullPath(Path.Combine(_env.ContentRootPath, "Logs"));
                // The writer names its files `yyyyMMdd_Logs.txt` (FileLoggerProvider). Building the name
                // from the raw segment asked for `yyyy-MM-dd_Logs.txt`, a file that never exists, so
                // every well-formed date answered 404 while the log sat next to it.
                var fileName = $"{parsed:yyyyMMdd}_Logs.txt";
                var filePath = Path.GetFullPath(Path.Combine(directoryPath, fileName));
                if (!filePath.StartsWith(directoryPath + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                {
                    return BadRequest("The date does not name a log file.");
                }

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
