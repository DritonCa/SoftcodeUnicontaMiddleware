namespace SoftcodeUnicontaMiddleware.Services
{
    public class RefreshToken
    {
        public string Token { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public int CompanyId { get; set; }
        public DateTime ExpiresAt { get; set; }
    }
}
