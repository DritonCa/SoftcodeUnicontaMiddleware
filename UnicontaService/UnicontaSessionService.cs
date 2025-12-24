using Microsoft.Extensions.Caching.Memory;
using System;
using System.Threading.Tasks;

namespace SoftcodeUnicontaMiddleware.UnicontaService
{
    public class UnicontaSessionService
    {
        private readonly IMemoryCache _cache;

        public UnicontaServiceClient Client { get; private set; }

        public bool IsInitialized => Client != null;

        public UnicontaSessionService(IMemoryCache cache)
        {
            _cache = cache;
        }

        public async Task InitializeAsync(
            string username,
            string password,
            string apiKey)
        {
            Client = new UnicontaServiceClient(
                username,
                password,
                apiKey,
                _cache
            );

            await Client.InitializeAsync();
        }

        // ✅ CENTRAL GUARD
        public void EnsureInitialized()
        {
            if (!IsInitialized)
                throw new InvalidOperationException("Uniconta session not initialized");
        }
    }
}
