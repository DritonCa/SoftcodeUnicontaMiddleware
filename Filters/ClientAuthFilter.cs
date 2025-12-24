using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SoftcodeUnicontaMiddleware.Services;

namespace SoftcodeUnicontaMiddleware.Filters
{
    public class ClientAuthFilter : IAsyncActionFilter
    {
        private readonly IClientAuthService _auth;

        public ClientAuthFilter(IClientAuthService auth)
        {
            _auth = auth;
        }

        public async Task OnActionExecutionAsync(
            ActionExecutingContext context,
            ActionExecutionDelegate next)
        {
            var headers = context.HttpContext.Request.Headers;

            if (!headers.TryGetValue("X-Client-Id", out var clientId) ||
                !headers.TryGetValue("X-Client-Secret", out var clientSecret))
            {
                context.Result = new UnauthorizedObjectResult(
                    "Missing client credentials");
                return;
            }

            var client = await _auth.ValidateAsync(
                clientId!, clientSecret!);

            if (client == null)
            {
                context.Result = new UnauthorizedObjectResult(
                    "Invalid client credentials");
                return;
            }

            // Make tenant/client available downstream if needed
            context.HttpContext.Items["ApiClient"] = client;

            await next();
        }
    }
}
