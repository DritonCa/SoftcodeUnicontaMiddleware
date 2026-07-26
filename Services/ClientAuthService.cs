using Microsoft.EntityFrameworkCore;
using SoftcodeUnicontaMiddleware.Data;
using SoftcodeUnicontaMiddleware.Data.Entities;

namespace SoftcodeUnicontaMiddleware.Services
{
    public class ClientAuthService : IClientAuthService
    {
        private readonly AppDbContext _db;
        private readonly SecretHasher _hasher;

        public ClientAuthService(AppDbContext db, SecretHasher hasher)
        {
            _db = db;
            _hasher = hasher;
        }

        public async Task<ApiClient?> ValidateAsync(
            string clientId,
            string clientSecret)
        {
            if (string.IsNullOrWhiteSpace(clientId) ||
                string.IsNullOrWhiteSpace(clientSecret))
                return null;

            var client = await _db.Clients
                .Include(c => c.Tenant)
                .FirstOrDefaultAsync(c =>
                    c.ClientId == clientId &&
                    c.IsActive &&
                    c.Tenant.IsActive);

            if (client == null)
                return null;

            // Constant-time HMAC verification (see SecretHasher).
            return _hasher.Verify(clientSecret, client.ClientSecretHash) ? client : null;
        }
    }
}
