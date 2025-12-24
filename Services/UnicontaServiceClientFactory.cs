using Microsoft.Extensions.Caching.Memory;
using SoftcodeUnicontaMiddleware.Services;
using System.Security.Claims;

namespace SoftcodeUnicontaMiddleware.UnicontaService
{
    public class UnicontaServiceClientFactory
    {
        private readonly IHttpContextAccessor _http;
        private readonly IMemoryCache _cache;
        private readonly IUnicontaCredentialStore _store;

        public UnicontaServiceClientFactory(
            IHttpContextAccessor http,
            IMemoryCache cache,
            IUnicontaCredentialStore store)
        {
            _http = http;
            _cache = cache;
            _store = store;
        }

        public async Task<UnicontaServiceClient> CreateAsync()
        {
            var user = _http.HttpContext!.User;

            var username = user.FindFirst("username")?.Value;
            var apiKey = user.FindFirst("apiKey")?.Value;
            var companyId = int.Parse(user.FindFirst("companyId")!.Value);


            var credentials = _store.Get(username!, companyId)
                ?? throw new UnauthorizedAccessException(
                "Uniconta credentials expired – please log in again");

            var client = new UnicontaServiceClient(
                credentials.Username,
                credentials.EncryptedPassword, // already decrypted
                credentials.ApiKey,
                _cache
            );

            await client.InitializeAsync();
            return client;
        }

    }
}
