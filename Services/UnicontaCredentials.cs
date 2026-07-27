namespace SoftcodeUnicontaMiddleware.Services
{
    public class UnicontaCredentials
    {
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// The Uniconta password. It holds plaintext while in transit to/from the
        /// store; inside <see cref="MemoryUnicontaCredentialStore"/> it is only ever
        /// held as ciphertext (protected with ASP.NET Data Protection).
        /// </summary>
        public string EncryptedPassword { get; set; } = string.Empty;

        public string ApiKey { get; set; } = string.Empty;
        public int CompanyId { get; set; }
    }
}
