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

            // Store an encrypted copy. We never mutate the caller's object, and the
            // cached instance always holds ciphertext – see Get() for why that matters.
            var encrypted = new UnicontaCredentials
            {
                Username = credentials.Username,
                EncryptedPassword = _protector.Protect(credentials.EncryptedPassword),
                ApiKey = credentials.ApiKey,
                CompanyId = credentials.CompanyId
            };

            _cache.Set(key, encrypted, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ttl
            });
        }

        public UnicontaCredentials? Get(string username, int companyId)
        {
            if (!_cache.TryGetValue(
                    CacheKey(username, companyId),
                    out UnicontaCredentials? cached) || cached is null)
                return null;

            // Return a decrypted copy. Decrypting into the cached instance instead
            // would corrupt it: the next Get() would try to Unprotect plaintext and
            // throw. The factory calls Get() on every authenticated request, so this
            // must be safe to call repeatedly.
            return new UnicontaCredentials
            {
                Username = cached.Username,
                EncryptedPassword = _protector.Unprotect(cached.EncryptedPassword),
                ApiKey = cached.ApiKey,
                CompanyId = cached.CompanyId
            };
        }

        private static string CacheKey(string username, int companyId)
            => $"uniconta:{username}:{companyId}";
    }
}
