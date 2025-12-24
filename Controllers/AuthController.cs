using Microsoft.AspNetCore.Mvc;
using SoftcodeUnicontaMiddleware.Models.Auth;
using SoftcodeUnicontaMiddleware.UnicontaService;
using System;
using System.Threading.Tasks;

namespace SoftcodeUnicontaMiddleware.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly UnicontaSessionService _sessionService;

        public AuthController(UnicontaSessionService sessionService)
        {
            _sessionService = sessionService;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] UnicontaLoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.UserName) ||
                string.IsNullOrWhiteSpace(request.Password) ||
                string.IsNullOrWhiteSpace(request.ApiKey))
                return BadRequest();

            await _sessionService.InitializeAsync(
                request.UserName,
                request.Password,
                request.ApiKey
            );

            return Ok(new { success = true });
        }
    }
}
