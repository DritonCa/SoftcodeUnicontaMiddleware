namespace SoftcodeUnicontaMiddleware.Services
{
    public interface IUnicontaCredentialStore
    {
        void Store(UnicontaCredentials credentials, TimeSpan ttl);
        UnicontaCredentials? Get(string username, int companyId);
    }
}
