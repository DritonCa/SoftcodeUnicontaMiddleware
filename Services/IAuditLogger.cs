namespace SoftcodeUnicontaMiddleware.Services
{
    public interface IAuditLogger
    {
        void Info(string action, object? data = null);
        void Warn(string action, object? data = null);
    }
}
