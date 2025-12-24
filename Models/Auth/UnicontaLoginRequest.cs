namespace SoftcodeUnicontaMiddleware.Models.Auth
{
    /// <summary>
    /// Request model used to authenticate against Uniconta.
    /// Sent from Magento / Postman.
    /// </summary>
    public class UnicontaLoginRequest
    {
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
    }
}
