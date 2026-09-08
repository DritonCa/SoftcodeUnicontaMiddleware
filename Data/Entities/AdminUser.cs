namespace SoftcodeUnicontaMiddleware.Data.Entities
{
    /// <summary>
    /// A login for the order-log admin interface. The password is never stored in
    /// clear text — only a salted PBKDF2 hash (format: iterations.saltBase64.hashBase64).
    /// </summary>
    public class AdminUser
    {
        public const string RoleAdmin  = "admin";
        public const string RoleViewer = "viewer";

        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastLoginAt { get; set; }

        public string? Email { get; set; }

        /// <summary>
        /// <see cref="RoleAdmin"/> manages users and sees every company;
        /// <see cref="RoleViewer"/> only sees the companies in <see cref="AllowedClientIds"/>.
        /// </summary>
        public string Role { get; set; } = RoleAdmin;

        /// <summary>
        /// Comma-separated client ids this user may see. Empty means every company —
        /// which is what an admin always gets, regardless of what is stored here.
        /// </summary>
        public string? AllowedClientIds { get; set; }

        public bool IsActive { get; set; } = true;

        public bool IsAdmin => string.Equals(Role, RoleAdmin, StringComparison.OrdinalIgnoreCase);

        /// <summary>The companies this user may see; empty list means "all".</summary>
        public IReadOnlyList<string> AllowedCompanies()
            => IsAdmin || string.IsNullOrWhiteSpace(AllowedClientIds)
                ? Array.Empty<string>()
                : AllowedClientIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}
