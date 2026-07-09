namespace SoftcodeUnicontaMiddleware.Services;

/// <summary>
/// Singleton file logger exclusively for Uniconta order submissions.
/// Writes to logs/uniconta_orders.log — one line per event, append-only.
///
/// Format:
///   2026-07-09 10:23:45 UTC [SUBMITTED] orderId=1001 type=cvr email=firm@example.dk debtor=D0042
///   2026-07-09 10:24:01 UTC [FAILED]    orderId=1002 type=privat email=kunde@example.dk error=CreateOrderHeader returned ...
///   2026-07-09 10:24:02 UTC [LINE_WARN] orderId=1002 sku=55 reason=CreateOrderLine returned NotFound
/// </summary>
public class OrderLogger : IOrderLogger
{
    private readonly string _path;
    private readonly object _lock = new();

    public OrderLogger(IConfiguration config, IWebHostEnvironment env)
    {
        var dir = Path.Combine(env.ContentRootPath, "logs");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "uniconta_orders.log");
    }

    public void LogSubmitted(int orderId, string customerType, string email, string debtorAccount)
        => Write("SUBMITTED", $"orderId={orderId} type={customerType} email={email} debtor={debtorAccount}");

    public void LogFailed(int orderId, string customerType, string email, string reason)
        => Write("FAILED   ", $"orderId={orderId} type={customerType} email={email} error={reason}");

    public void LogLineWarning(int orderId, string sku, string reason)
        => Write("LINE_WARN", $"orderId={orderId} sku={sku} reason={reason}");

    private void Write(string level, string message)
    {
        var line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC [{level}] {message}{Environment.NewLine}";
        lock (_lock)
        {
            File.AppendAllText(_path, line);
        }
    }
}
