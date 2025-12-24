using Microsoft.EntityFrameworkCore;
using SoftcodeUnicontaMiddleware.Data;
using SoftcodeUnicontaMiddleware.Data.Entities;
using System.Security.Cryptography;
using System.Text;

namespace SoftcodeUnicontaMiddleware.Services
{
    public class ClientAuthService : IClientAuthService
    {
        private readonly AppDbContext _db;

        public ClientAuthService(AppDbContext db)
        {
            _db = db;
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

            var incomingHash = Hash(clientSecret);

            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(client.ClientSecretHash),
                    Convert.FromHexString(incomingHash)))
                return null;

            return client;
        }

        private static string Hash(string value)
        {
            using var sha = SHA256.Create();
            return Convert.ToHexString(
                sha.ComputeHash(Encoding.UTF8.GetBytes(value))
            );
        }
    }
}
