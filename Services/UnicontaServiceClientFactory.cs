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

            // The JWT only identifies the caller (username + companyId). The actual
            // Uniconta secrets are fetched from the server-side credential store, so
            // they are never transported inside the token.
            var username = user.FindFirst("username")?.Value;
            var companyId = int.Parse(user.FindFirst("companyId")!.Value);

            var credentials = _store.Get(username!, companyId)
                ?? throw new UnauthorizedAccessException(
                "Uniconta credentials expired – please log in again");

            var client = new UnicontaServiceClient(
                credentials.Username,
                credentials.EncryptedPassword, // decrypted by the store on read
                credentials.ApiKey,
                _cache
            );

            await client.InitializeAsync();
            return client;
        }

    }
}
