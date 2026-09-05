namespace SoftcodeUnicontaMiddleware.Data.Entities
{
    public class ApiClient
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }

        public string ClientId { get; set; } = string.Empty;
        public string ClientSecretHash { get; set; } = string.Empty;

        // Reversibly-encrypted (DataProtection) copy of the client secret, kept ONLY
        // so the admin Companies view can reveal it. Authentication still verifies
        // against ClientSecretHash (HMAC); this column is never used for auth.
        public string? ClientSecretEnc { get; set; }

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ApiTenant Tenant { get; set; } = null!;
    }
}
