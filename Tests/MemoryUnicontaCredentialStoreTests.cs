using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using SoftcodeUnicontaMiddleware.Services;
using Xunit;

namespace SoftcodeUnicontaMiddleware.Tests;

public class MemoryUnicontaCredentialStoreTests
{
    private static MemoryUnicontaCredentialStore NewStore()
        => new(
            new MemoryCache(new MemoryCacheOptions()),
            new EphemeralDataProtectionProvider());

    private static UnicontaCredentials Sample() => new()
    {
        Username = "acme-api",
        EncryptedPassword = "s3cr3t-password", // plaintext in transit; the store encrypts it
        ApiKey = "api-key-123",
        CompanyId = 42
    };

    [Fact]
    public void Get_round_trips_the_password_and_api_key()
    {
        var store = NewStore();
        store.Store(Sample(), TimeSpan.FromMinutes(5));

        var got = store.Get("acme-api", 42);

        Assert.NotNull(got);
        Assert.Equal("s3cr3t-password", got!.EncryptedPassword);
        Assert.Equal("api-key-123", got.ApiKey);
    }

    [Fact]
    public void Get_is_idempotent()
    {
        // Regression: Get() used to decrypt into the cached instance, corrupting it so
        // the SECOND read threw a CryptographicException. The factory calls Get() on
        // every authenticated request, so repeated reads must return the same value.
        var store = NewStore();
        store.Store(Sample(), TimeSpan.FromMinutes(5));

        var first = store.Get("acme-api", 42);
        var second = store.Get("acme-api", 42);

        Assert.Equal("s3cr3t-password", first!.EncryptedPassword);
        Assert.Equal("s3cr3t-password", second!.EncryptedPassword);
    }

    [Fact]
    public void Store_does_not_mutate_the_callers_object()
    {
        var store = NewStore();
        var creds = Sample();

        store.Store(creds, TimeSpan.FromMinutes(5));

        Assert.Equal("s3cr3t-password", creds.EncryptedPassword); // untouched, still plaintext
    }

    [Fact]
    public void Get_returns_null_for_an_unknown_user()
    {
        var store = NewStore();

        Assert.Null(store.Get("nobody", 1));
    }
}
