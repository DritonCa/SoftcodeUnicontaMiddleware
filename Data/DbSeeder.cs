using SoftcodeUnicontaMiddleware.Data.Entities;
using System.Security.Cryptography;
using System.Text;

namespace SoftcodeUnicontaMiddleware.Data
{
    public static class DbSeeder
    {
        public static void Seed(AppDbContext db)
        {
            // Prevent double-seeding
            if (db.Tenants.Any())
                return;

            // 1️⃣ Create tenant
            var tenant = new ApiTenant
            {
                Id = Guid.NewGuid(),
                Name = "Demo Customer",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            // 2️⃣ Create client credentials
            var clientSecret = "demo-secret-9f3c2a1d7b4e6c8f0a2d4e6f8a1c3b5d";

            var client = new ApiClient
            {
                Id = Guid.NewGuid(),
                TenantId = tenant.Id,
                ClientId = "demo-client",
                ClientSecretHash = Hash(clientSecret),
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };

            db.Tenants.Add(tenant);
            db.Clients.Add(client);
            db.SaveChanges();

            // Helpful for first run
            Console.WriteLine("==== API CLIENT SEEDED ====");
            Console.WriteLine($"ClientId: demo-client");
            Console.WriteLine($"ClientSecret: {clientSecret}");
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
