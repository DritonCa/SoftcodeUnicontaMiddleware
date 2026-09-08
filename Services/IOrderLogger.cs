namespace SoftcodeUnicontaMiddleware.Services;

public interface IOrderLogger
{
    void LogReceived(int orderId, string customerType, string email, string summary, object? request = null);
    void LogSubmitted(int orderId, string customerType, string email, string debtorAccount, object? request = null, object? response = null);
    void LogFailed(int orderId, string customerType, string email, string reason, string? detail = null, object? request = null, object? response = null);
    void LogLineWarning(int orderId, string sku, string reason);
}
