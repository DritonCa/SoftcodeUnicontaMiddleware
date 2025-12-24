using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using System;

namespace SoftcodeUnicontaMiddleware.Services
{
    public class MemoryUnicontaCredentialStore : IUnicontaCredentialStore
    {
        private readonly IMemoryCache _cache;
        private readonly IDataProtector _protector;

        public MemoryUnicontaCredentialStore(
            IMemoryCache cache,
            IDataProtectionProvider provider)
        {
            _cache = cache;
            _protector = provider.CreateProtector(
                "Softcode.Uniconta.Credentials.v1");
        }

        public void Store(UnicontaCredentials credentials, TimeSpan ttl)
        {
            var key = CacheKey(credentials.Username, credentials.CompanyId);

            credentials.EncryptedPassword =
                _protector.Protect(credentials.EncryptedPassword);

            _cache.Set(key, credentials, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl
            });
        }

        public UnicontaCredentials? Get(string username, int companyId)
        {
            if (!_cache.TryGetValue(
                CacheKey(username, companyId),
                out UnicontaCredentials? credentials))
                return null;

            credentials.EncryptedPassword =
                _protector.Unprotect(credentials.EncryptedPassword);

            return credentials;
        }

        private static string CacheKey(string username, int companyId)
            => $"uniconta:{username}:{companyId}";
    }
}
