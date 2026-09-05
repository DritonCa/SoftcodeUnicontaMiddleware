namespace SoftcodeUnicontaMiddleware.Services;

public interface IOrderLogger
{
    void LogReceived(int orderId, string customerType, string email, string summary);
    void LogSubmitted(int orderId, string customerType, string email, string debtorAccount);
    void LogFailed(int orderId, string customerType, string email, string reason, string? detail = null);
    void LogLineWarning(int orderId, string sku, string reason);
}
