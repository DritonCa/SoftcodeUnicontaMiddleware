using Microsoft.EntityFrameworkCore;
using SoftcodeUnicontaMiddleware.Data;
using SoftcodeUnicontaMiddleware.Data.Entities;
using System.Security.Cryptography;

namespace SoftcodeUnicontaMiddleware.Services
{
    /// <summary>
    /// Manages the admin logins for the order-log interface. Passwords are stored
    /// only as a salted PBKDF2-SHA256 hash (never in clear text or reversibly encrypted).
    /// </summary>
    public class AdminUserService : IAdminUserService
    {
        private const int Iterations = 100_000;
        private const int SaltSize   = 16; // bytes
        private const int HashSize   = 32; // bytes

        private readonly AppDbContext _db;

        public AdminUserService(AppDbContext db) => _db = db;

        public Task<bool> AnyExistsAsync() => _db.AdminUsers.AnyAsync();

        public async Task<AdminUser?> CreateFirstAsync(string username, string password)
        {
            username = (username ?? string.Empty).Trim();

            if (username.Length < 3 || (password ?? string.Empty).Length < 8)
                return null;

            // Only ever create the very first admin through this path.
            if (await _db.AdminUsers.AnyAsync())
                return null;

            var user = new AdminUser
            {
                Username     = username,
                PasswordHash = Hash(password!),
                CreatedAt    = DateTime.UtcNow
            };

            _db.AdminUsers.Add(user);
            await _db.SaveChangesAsync();
            return user;
        }

        public async Task<AdminUser?> ValidateAsync(string username, string password)
        {
            username = (username ?? string.Empty).Trim();
            if (username.Length == 0 || string.IsNullOrEmpty(password))
                return null;

            var user = await _db.AdminUsers
                .FirstOrDefaultAsync(u => u.Username == username);
            if (user == null)
                return null;

            if (!Verify(password, user.PasswordHash))
                return null;

            user.LastLoginAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return user;
        }

        // ---- PBKDF2 hashing (format: iterations.saltB64.hashB64) ------------------

        private static string Hash(string password)
        {
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var hash = Rfc2898DeriveBytes.Pbkdf2(
                password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);

            return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
        }

        private static bool Verify(string password, string stored)
        {
            var parts = stored.Split('.');
            if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations))
                return false;

            byte[] salt, expected;
            try
            {
                salt     = Convert.FromBase64String(parts[1]);
                expected = Convert.FromBase64String(parts[2]);
            }
            catch (FormatException)
            {
                return false;
            }

            var actual = Rfc2898DeriveBytes.Pbkdf2(
                password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
    }
}
