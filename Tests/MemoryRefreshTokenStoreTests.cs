using Microsoft.Extensions.Caching.Memory;
using SoftcodeUnicontaMiddleware.Services;
using Xunit;

namespace SoftcodeUnicontaMiddleware.Tests;

public class MemoryRefreshTokenStoreTests
{
    private static (MemoryRefreshTokenStore store, IMemoryCache cache) NewStore()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        return (new MemoryRefreshTokenStore(cache), cache);
    }

    private static RefreshToken NewToken(string raw) => new()
    {
        Token = raw,
        Username = "acme-api",
        CompanyId = 42,
        ExpiresAt = DateTime.UtcNow.AddDays(14)
    };

    [Fact]
    public void Get_returns_stored_metadata_for_the_right_token()
    {
        var (store, _) = NewStore();
        store.Store(NewToken("raw-token-value"));

        var found = store.Get("raw-token-value");

        Assert.NotNull(found);
        Assert.Equal("acme-api", found!.Username);
        Assert.Equal(42, found.CompanyId);
    }

    [Fact]
    public void Get_returns_null_for_an_unknown_token()
    {
        var (store, _) = NewStore();
        store.Store(NewToken("raw-token-value"));

        Assert.Null(store.Get("some-other-token"));
    }

    [Fact]
    public void Revoked_token_can_no_longer_be_used()
    {
        // Rotation: refresh() revokes the presented token before issuing a new one,
        // so replaying the old token must fail.
        var (store, _) = NewStore();
        store.Store(NewToken("raw-token-value"));

        store.Revoke("raw-token-value");

        Assert.Null(store.Get("raw-token-value"));
    }

    [Fact]
    public void Raw_token_is_never_used_as_the_cache_key()
    {
        // Hash-at-rest: the bearer secret must not appear as a key in the backing
        // cache, so dumping the cache yields nothing that can be replayed.
        var (store, cache) = NewStore();
        store.Store(NewToken("raw-token-value"));

        Assert.False(cache.TryGetValue("raw-token-value", out _));
    }

    [Fact]
    public void Stored_entry_does_not_retain_the_raw_token()
    {
        var (store, _) = NewStore();
        store.Store(NewToken("raw-token-value"));

        var found = store.Get("raw-token-value");

        Assert.Equal(string.Empty, found!.Token);
    }
}
