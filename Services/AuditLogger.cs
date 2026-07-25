using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Security.Claims;

namespace SoftcodeUnicontaMiddleware.Services
{
    public class AuditLogger : IAuditLogger
    {
        private readonly ILogger<AuditLogger> _logger;
        private readonly IHttpContextAccessor _http;

        public AuditLogger(
            ILogger<AuditLogger> logger,
            IHttpContextAccessor http)
        {
            _logger = logger;
            _http = http;
        }

        public void Info(string action, object? data = null)
        {
            Log(LogLevel.Information, action, data);
        }

        public void Warn(string action, object? data = null)
        {
            Log(LogLevel.Warning, action, data);
        }

        private void Log(LogLevel level, string action, object? data)
        {
            var ctx = _http.HttpContext;

            var log = new
            {
                action,
                clientId = ctx?.Request.Headers["X-Client-Id"].FirstOrDefault(),
                companyId = ctx?.User.FindFirstValue("companyId"),
                path = ctx?.Request.Path.Value,
                traceId = ctx?.TraceIdentifier,
                data
            };

            _logger.Log(level, "AUDIT {@log}", log);
        }
    }
}
