using Microsoft.AspNetCore.Mvc;
using SoftcodeUnicontaMiddleware.Filters;
using SoftcodeUnicontaMiddleware.Models.Auth;
using SoftcodeUnicontaMiddleware.Services;
using SoftcodeUnicontaMiddleware.UnicontaService;

namespace SoftcodeUnicontaMiddleware.Controllers
{
    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly JwtTokenService _jwt;
        private readonly IUnicontaCredentialStore _store;
        private readonly IRefreshTokenStore _refreshStore;

        public AuthController(
            JwtTokenService jwt,
            IUnicontaCredentialStore store,
            IRefreshTokenStore refreshStore)
        {
            _jwt = jwt;
            _store = store;
            _refreshStore = refreshStore;
        }

        [HttpPost("login")]
        [ServiceFilter(typeof(ClientAuthFilter))]
        public async Task<IActionResult> Login(
            [FromBody] UnicontaLoginRequest request)
        {
            // 1️⃣ Validate input (basic safety)
            if (string.IsNullOrWhiteSpace(request.UserName) ||
                string.IsNullOrWhiteSpace(request.Password) ||
                string.IsNullOrWhiteSpace(request.ApiKey))
            {
                return BadRequest("Missing credentials");
            }

            // 2️⃣ Login to Uniconta ONCE (no cache yet)
            var client = new UnicontaServiceClient(
                request.UserName,
                request.Password,
                request.ApiKey,
                HttpContext.RequestServices
                    .GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>()
            );

            await client.InitializeAsync();

            var refreshToken = _jwt.GenerateRefreshToken();

            _refreshStore.Store(new RefreshToken
            {
                Token = refreshToken,
                Username = request.UserName,
                CompanyId = client.CompanyId,
                ExpiresAt = DateTime.UtcNow.AddDays(14)
            });

            // 4️⃣ Issue JWT (NO password inside)
            var token = _jwt.CreateToken(
                request.UserName,
                request.ApiKey,
                client.CompanyId
            );

            // 5️⃣ Return token only
            return Ok(new
            {
                access_token = token,
                token_type = "Bearer"
            });
        }

        [HttpPost("refresh")]
        public IActionResult Refresh([FromBody] string refreshToken)
        {
            var stored = _refreshStore.Get(refreshToken);

            if (stored == null || stored.ExpiresAt < DateTime.UtcNow)
                return Unauthorized("Invalid refresh token");

            // Rotate refresh token (important)
            _refreshStore.Revoke(refreshToken);

            var newRefreshToken = _jwt.GenerateRefreshToken();

            _refreshStore.Store(new RefreshToken
            {
                Token = newRefreshToken,
                Username = stored.Username,
                CompanyId = stored.CompanyId,
                ExpiresAt = DateTime.UtcNow.AddDays(14)
            });

            var newAccessToken = _jwt.CreateToken(
                stored.Username,
                apiKey: string.Empty, // already cached
                stored.CompanyId
            );

            return Ok(new
            {
                access_token = newAccessToken,
                refresh_token = newRefreshToken,
                token_type = "Bearer"
            });
        }
    }
}
