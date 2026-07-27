using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace SoftcodeUnicontaMiddleware.Services
{
    /// <summary>
    /// In-memory refresh-token store. Tokens are persisted only as a SHA-256 hash, so
    /// the raw bearer secret is never kept after it has been handed to the client.
    /// </summary>
    /// <remarks>
    /// A refresh token is a 64-byte cryptographically-random value, so a fast hash
    /// (not a password KDF) is the correct primitive: lookups stay O(1) and a dump of
    /// the store yields no usable token. Being <see cref="IMemoryCache"/>-backed, the
    /// store is per-process; a multi-instance deployment should implement
    /// <see cref="IRefreshTokenStore"/> over a shared, persistent store using the same
    /// hash-at-rest approach.
    /// </remarks>
    public class MemoryRefreshTokenStore : IRefreshTokenStore
    {
        private readonly IMemoryCache _cache;

        public MemoryRefreshTokenStore(IMemoryCache cache)
        {
            _cache = cache;
        }

        public void Store(RefreshToken token)
        {
            var entry = new RefreshToken
            {
                Token = string.Empty, // never retain the raw bearer secret
                Username = token.Username,
                CompanyId = token.CompanyId,
                ExpiresAt = token.ExpiresAt
            };

            _cache.Set(Hash(token.Token), entry, token.ExpiresAt);
        }

        public RefreshToken? Get(string token)
        {
            _cache.TryGetValue(Hash(token), out RefreshToken? stored);
            return stored;
        }

        public void Revoke(string token)
        {
            _cache.Remove(Hash(token));
        }

        private static string Hash(string token)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return Convert.ToHexString(bytes);
        }
    }
}
