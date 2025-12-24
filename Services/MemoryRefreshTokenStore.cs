using Microsoft.Extensions.Caching.Memory;

namespace SoftcodeUnicontaMiddleware.Services
{
    public class MemoryRefreshTokenStore : IRefreshTokenStore
    {
        private readonly IMemoryCache _cache;

        public MemoryRefreshTokenStore(IMemoryCache cache)
        {
            _cache = cache;
        }

        public void Store(RefreshToken token)
        {
            _cache.Set(token.Token, token, token.ExpiresAt);
        }

        public RefreshToken? Get(string token)
        {
            _cache.TryGetValue(token, out RefreshToken? stored);
            return stored;
        }

        public void Revoke(string token)
        {
            _cache.Remove(token);
        }
    }
}
