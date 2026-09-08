using System.Text.Json;
using SoftcodeUnicontaMiddleware.Data.Entities;

namespace SoftcodeUnicontaMiddleware.Services;

/// <summary>
/// Singleton file logger exclusively for Uniconta order submissions.
///
/// Writes each event twice, on purpose:
///   logs/uniconta_orders.jsonl  — one JSON object per event, carrying the full
///                                 request and response so the admin can expand
///                                 any row and see exactly what was exchanged.
///   logs/uniconta_orders.log    — the original one-line-per-event text log, kept
///                                 so `tail -f` and the entries written before the
///                                 JSON log existed keep working.
///
/// Each event is attributed to the company whose API client made the call, read
/// from the authenticated client the ClientAuthFilter puts on the request.
/// </summary>
public class OrderLogger : IOrderLogger
{
    private readonly string _path;
    private readonly string _jsonPath;
    private readonly string _errorPath;
    private readonly IHttpContextAccessor _http;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions PayloadJson = new() { WriteIndented = true };

    public OrderLogger(IWebHostEnvironment env, IHttpContextAccessor http)
    {
        _http = http;
        var dir = Path.Combine(env.ContentRootPath, "logs");
        Directory.CreateDirectory(dir);
        _path      = Path.Combine(dir, "uniconta_orders.log");
        _jsonPath  = Path.Combine(dir, "uniconta_orders.jsonl");
        _errorPath = Path.Combine(dir, "uniconta_errors.log");
    }

    public void LogReceived(int orderId, string customerType, string email, string summary, object? request = null)
        => Write("RECEIVED ", $"orderId={orderId} type={customerType} email={email} {summary}",
                 orderId, customerType, email, request, null, null);

    public void LogSubmitted(int orderId, string customerType, string email, string debtorAccount,
                             object? request = null, object? response = null)
        => Write("SUBMITTED", $"orderId={orderId} type={customerType} email={email} debtor={debtorAccount}",
                 orderId, customerType, email, request, response, null);

    public void LogFailed(int orderId, string customerType, string email, string reason, string? detail = null,
                          object? request = null, object? response = null)
    {
        Write("FAILED   ", $"orderId={orderId} type={customerType} email={email} error={reason}",
              orderId, customerType, email, request, response, detail);

        if (!string.IsNullOrWhiteSpace(detail))
            WriteError(orderId, customerType, email, reason, detail!);
    }

    public void LogLineWarning(int orderId, string sku, string reason)
        => Write("LINE_WARN", $"orderId={orderId} sku={sku} reason={reason}",
                 orderId, null, null, null, null, reason);

    private void Write(string level, string message, int orderId, string? customerType, string? email,
                       object? request, object? response, string? detail)
    {
        var now  = DateTime.UtcNow;
        var line = $"{now:yyyy-MM-dd HH:mm:ss} UTC [{level}] {message}{Environment.NewLine}";

        var (clientId, company) = CurrentClient();

        var entry = new
        {
            ts       = now.ToString("yyyy-MM-dd HH:mm:ss"),
            level    = level.Trim(),
            orderId  = orderId > 0 ? orderId : (int?)null,
            type     = customerType,
            email,
            clientId,
            company,
            details  = message,
            request  = Serialize(request),
            response = Serialize(response),
            detail
        };

        lock (_lock)
        {
            File.AppendAllText(_path, line);
            File.AppendAllText(_jsonPath, JsonSerializer.Serialize(entry) + Environment.NewLine);
        }
    }

    /// <summary>
    /// The company behind the call, from the client the ClientAuthFilter authenticated.
    /// Null outside an HTTP request (nothing else writes order events today).
    /// </summary>
    private (string? ClientId, string? Company) CurrentClient()
    {
        if (_http.HttpContext?.Items.TryGetValue("ApiClient", out var raw) == true && raw is ApiClient c)
            return (c.ClientId, c.Tenant?.Name ?? c.ClientId);

        return (null, null);
    }

    // Payloads are stored as pretty-printed JSON text rather than nested objects, so a
    // change to the request/response shape can never break parsing of the log.
    private static string? Serialize(object? value)
    {
        if (value == null)
            return null;

        try
        {
            return value as string ?? JsonSerializer.Serialize(value, PayloadJson);
        }
        catch (Exception ex)
        {
            return "(kunne ikke serialiseres: " + ex.Message + ")";
        }
    }

    // Full, multi-line failure detail (exception + stack + the exact fields that
    // were rejected). Also carried per-entry in the JSON log; this file remains for
    // reading the failures on the server without the admin interface.
    private void WriteError(int orderId, string type, string email, string reason, string detail)
    {
        var nl    = Environment.NewLine;
        var block =
            "================================================================" + nl +
            $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC  orderId={orderId} type={type} email={email}" + nl +
            $"REASON: {reason}" + nl + nl +
            detail + nl + nl;
        lock (_lock)
        {
            File.AppendAllText(_errorPath, block);
        }
    }
}
