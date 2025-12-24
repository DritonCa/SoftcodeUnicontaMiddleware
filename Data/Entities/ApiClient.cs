namespace SoftcodeUnicontaMiddleware.Data.Entities
{
    public class ApiClient
    {
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }

        public string ClientId { get; set; } = string.Empty;
        public string ClientSecretHash { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ApiTenant Tenant { get; set; } = null!;
    }
}
