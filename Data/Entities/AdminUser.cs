namespace SoftcodeUnicontaMiddleware.Data.Entities
{
    /// <summary>
    /// A login for the order-log admin interface. The password is never stored in
    /// clear text — only a salted PBKDF2 hash (format: iterations.saltBase64.hashBase64).
    /// </summary>
    public class AdminUser
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastLoginAt { get; set; }
    }
}
