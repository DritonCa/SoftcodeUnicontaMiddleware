namespace SoftcodeUnicontaMiddleware.Services;

public interface IOrderLogger
{
    void LogSubmitted(int orderId, string customerType, string email, string debtorAccount);
    void LogFailed(int orderId, string customerType, string email, string reason);
    void LogLineWarning(int orderId, string sku, string reason);
}
