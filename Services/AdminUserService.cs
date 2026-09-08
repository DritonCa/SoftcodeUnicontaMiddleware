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
                CreatedAt    = DateTime.UtcNow,
                Role         = AdminUser.RoleAdmin,
                IsActive     = true
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
            if (user == null || !user.IsActive)
                return null;

            if (!Verify(password, user.PasswordHash))
                return null;

            user.LastLoginAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return user;
        }

        public Task<AdminUser?> FindAsync(string username)
            => _db.AdminUsers.FirstOrDefaultAsync(u => u.Username == (username ?? "").Trim())!;

        public async Task<IReadOnlyList<AdminUser>> ListAsync()
            => await _db.AdminUsers.AsNoTracking().OrderBy(u => u.Username).ToListAsync();

        public async Task<(AdminUser? User, string? Error)> CreateAsync(
            string username, string password, string? email, string role, IEnumerable<string>? companies)
        {
            username = (username ?? "").Trim();

            if (username.Length < 3)
                return (null, "Brugernavn skal være mindst 3 tegn.");
            if ((password ?? "").Length < 8)
                return (null, "Adgangskoden skal være mindst 8 tegn.");
            if (await _db.AdminUsers.AnyAsync(u => u.Username == username))
                return (null, "Brugernavnet findes allerede.");

            var normalisedRole = NormaliseRole(role);
            var allowed        = Join(companies);

            if (normalisedRole == AdminUser.RoleViewer && allowed.Length == 0)
                return (null, "Vælg mindst én virksomhed brugeren må se.");

            var user = new AdminUser
            {
                Username         = username,
                PasswordHash     = Hash(password!),
                Email            = Clean(email),
                Role             = normalisedRole,
                AllowedClientIds = normalisedRole == AdminUser.RoleAdmin ? null : allowed,
                IsActive         = true,
                CreatedAt        = DateTime.UtcNow
            };

            _db.AdminUsers.Add(user);
            await _db.SaveChangesAsync();
            return (user, null);
        }

        public async Task<string?> UpdateAsync(
            int id, string? email, string role, IEnumerable<string>? companies, bool isActive, string actingUsername)
        {
            var user = await _db.AdminUsers.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null)
                return "Brugeren findes ikke.";

            var normalisedRole = NormaliseRole(role);
            var allowed        = Join(companies);

            if (normalisedRole == AdminUser.RoleViewer && allowed.Length == 0)
                return "Vælg mindst én virksomhed brugeren må se.";

            // Never let the last admin — or yourself — lose the ability to administer,
            // which would leave the installation with no way back in.
            var selfDemotion = string.Equals(user.Username, actingUsername, StringComparison.OrdinalIgnoreCase)
                               && (normalisedRole != AdminUser.RoleAdmin || !isActive);
            if (selfDemotion)
                return "Du kan ikke fjerne din egen administratoradgang.";

            if (user.IsAdmin && (normalisedRole != AdminUser.RoleAdmin || !isActive) && await LastAdmin(user.Id))
                return "Der skal være mindst én aktiv administrator.";

            user.Email            = Clean(email);
            user.Role             = normalisedRole;
            user.AllowedClientIds = normalisedRole == AdminUser.RoleAdmin ? null : allowed;
            user.IsActive         = isActive;

            await _db.SaveChangesAsync();
            return null;
        }

        public async Task<string?> SetPasswordAsync(int id, string password)
        {
            if ((password ?? "").Length < 8)
                return "Adgangskoden skal være mindst 8 tegn.";

            var user = await _db.AdminUsers.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null)
                return "Brugeren findes ikke.";

            user.PasswordHash = Hash(password!);
            await _db.SaveChangesAsync();
            return null;
        }

        public async Task<string?> DeleteAsync(int id, string actingUsername)
        {
            var user = await _db.AdminUsers.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null)
                return "Brugeren findes ikke.";

            if (string.Equals(user.Username, actingUsername, StringComparison.OrdinalIgnoreCase))
                return "Du kan ikke slette din egen bruger.";

            if (user.IsAdmin && await LastAdmin(user.Id))
                return "Der skal være mindst én aktiv administrator.";

            _db.AdminUsers.Remove(user);
            await _db.SaveChangesAsync();
            return null;
        }

        /// <summary>True when no other active admin would be left behind.</summary>
        private async Task<bool> LastAdmin(int excludingId)
            => !await _db.AdminUsers
                .AnyAsync(u => u.Id != excludingId && u.IsActive && u.Role == AdminUser.RoleAdmin);

        private static string NormaliseRole(string? role)
            => string.Equals(role, AdminUser.RoleViewer, StringComparison.OrdinalIgnoreCase)
                ? AdminUser.RoleViewer
                : AdminUser.RoleAdmin;

        private static string Join(IEnumerable<string>? companies)
            => companies == null
                ? ""
                : string.Join(',', companies.Select(c => (c ?? "").Trim()).Where(c => c.Length > 0).Distinct());

        private static string? Clean(string? s)
            => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

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
