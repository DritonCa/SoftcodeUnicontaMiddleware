namespace SoftcodeUnicontaMiddleware.Data.Entities
{
    public class ApiTenant
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
