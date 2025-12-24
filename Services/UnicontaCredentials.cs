namespace SoftcodeUnicontaMiddleware.Services
{
    public class UnicontaCredentials
    {
        public string Username { get; set; } = string.Empty;
        public string EncryptedPassword { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public int CompanyId { get; set; }
    }
}
