namespace SoftcodeUnicontaMiddleware.Services
{
    public interface IRefreshTokenStore
    {
        void Store(RefreshToken token);
        RefreshToken? Get(string token);
        void Revoke(string token);
    }
}
