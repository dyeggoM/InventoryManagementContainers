using IMCAPI.DB.Entities;
using IMCAPI.DB.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace IMCAPI.Controllers
{

    public class BaseController<T> : ControllerBase where T : Base, new()
    {
        private readonly IRepository<T> _repository;
        private readonly ILogger<BaseController<T>> _logger;

        public BaseController(IRepository<T> repository, ILogger<BaseController<T>> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        [AllowAnonymous]
        [HttpGet("test")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        //[AllowAnonymous]
        public virtual IActionResult TestEndpoint()
        {
            var methodName = $"{GetType().Name}.{MethodBase.GetCurrentMethod()!.Name}";
            try
            {
                _logger.Log(LogLevel.Information, $"Inicio: {methodName}");
                var data = new T();
                return Ok(data);
            }
            catch (Exception e)
            {
                var logMessage = $"{methodName}.Message: {e.Message}" +
                                 (e.InnerException != null ? $"\n{methodName}.InnerException: {e.InnerException}" : "");
                _logger.Log(LogLevel.Error, logMessage);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
            finally
            {
                _logger.Log(LogLevel.Information, $"Fin: {methodName}");
            }
        }

        [HttpGet]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        [Authorize(Policy = "RequiereLectura")]
        public virtual async Task<IActionResult> Get()
        {
            var methodName = $"{GetType().Name}.{MethodBase.GetCurrentMethod()!.Name}";
            try
            {
                _logger.Log(LogLevel.Information, $"Inicio: {methodName}");
                var data = await _repository.GetAll();
                return Ok(data);
            }
            catch (Exception e)
            {
                var logMessage = $"{methodName}.Message: {e.Message}" +
                                 (e.InnerException != null ? $"\n{methodName}.InnerException: {e.InnerException}" : "");
                _logger.Log(LogLevel.Error, logMessage);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
            finally
            {
                _logger.Log(LogLevel.Information, $"Fin: {methodName}");
            }
        }

        [HttpGet("{id:int}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        [Authorize(Policy = "RequiereLectura")]
        public virtual async Task<IActionResult> Get(int id)
        {
            var methodName = $"{GetType().Name}.{MethodBase.GetCurrentMethod()!.Name}";
            try
            {
                _logger.Log(LogLevel.Information, $"Inicio: {methodName}");
                var data = await _repository.GetById(id);

                if (data == null) return NotFound();

                return Ok(data);
            }
            catch (Exception e)
            {
                var logMessage = $"{methodName}.Message: {e.Message}" +
                                 (e.InnerException != null ? $"\n{methodName}.InnerException: {e.InnerException}" : "");
                _logger.Log(LogLevel.Error, logMessage);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
            finally
            {
                _logger.Log(LogLevel.Information, $"Fin: {methodName}");
            }
        }

        [HttpPut("{id:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        [Authorize(Policy = "RequiereEscritura")]
        public virtual async Task<IActionResult> Put(int id, T data)
        {
            var methodName = $"{GetType().Name}.{MethodBase.GetCurrentMethod()!.Name}";
            try
            {
                _logger.Log(LogLevel.Information, $"Inicio: {methodName}");
                if (id != data.Id) return BadRequest();

                await _repository.UpdateAsync(id, data);

                return NoContent();
            }
            catch (DbUpdateConcurrencyException e)
            {
                var logMessage = $"{methodName}.Message: {e.Message}" +
                                 (e.InnerException != null ? $"\n{methodName}.InnerException: {e.InnerException}" : "");
                _logger.Log(LogLevel.Error, logMessage);
                if (_repository.GetById(id) == null) return NotFound();
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
            catch (Exception e)
            {
                var logMessage = $"{methodName}.Message: {e.Message}" +
                                 (e.InnerException != null ? $"\n{methodName}.InnerException: {e.InnerException}" : "");
                _logger.Log(LogLevel.Error, logMessage);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
            finally
            {
                _logger.Log(LogLevel.Information, $"Fin: {methodName}");
            }
        }

        [HttpPost]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        [Authorize(Policy = "RequiereEscritura")]
        public virtual async Task<IActionResult> Post(T data)
        {
            var methodName = $"{GetType().Name}.{MethodBase.GetCurrentMethod()!.Name}";
            try
            {
                _logger.Log(LogLevel.Information, $"Inicio: {methodName}");
                await _repository.Add(data);
                return Ok(data);
            }
            catch (Exception e)
            {
                var logMessage = $"{methodName}.Message: {e.Message}" +
                                 (e.InnerException != null ? $"\n{methodName}.InnerException: {e.InnerException}" : "");
                _logger.Log(LogLevel.Error, logMessage);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
            finally
            {
                _logger.Log(LogLevel.Information, $"Fin: {methodName}");
            }
        }

        [HttpDelete("{id:int}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        [Authorize(Policy = "RequiereEscritura")]
        public virtual async Task<IActionResult> Delete(int id)
        {
            var methodName = $"{GetType().Name}.{MethodBase.GetCurrentMethod()!.Name}";
            try
            {
                _logger.Log(LogLevel.Information, $"Inicio: {methodName}");
                await _repository.Delete(id);
                return NoContent();
            }
            catch (Exception e)
            {
                var logMessage = $"{methodName}.Message: {e.Message}" +
                                 (e.InnerException != null ? $"\n{methodName}.InnerException: {e.InnerException}" : "");
                _logger.Log(LogLevel.Error, logMessage);
                return StatusCode(StatusCodes.Status500InternalServerError);
            }
            finally
            {
                _logger.Log(LogLevel.Information, $"Fin: {methodName}");
            }
        }
    }

}
