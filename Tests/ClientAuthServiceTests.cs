using Microsoft.EntityFrameworkCore;
using SoftcodeUnicontaMiddleware.Data;
using SoftcodeUnicontaMiddleware.Data.Entities;
using SoftcodeUnicontaMiddleware.Services;
using Xunit;

namespace SoftcodeUnicontaMiddleware.Tests;

public class ClientAuthServiceTests
{
    private static readonly SecretHasher Hasher = new("test-pepper");

    private static AppDbContext NewDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())   // isolated per test
            .Options;
        return new AppDbContext(options);
    }

    private static AppDbContext SeedClient(
        string clientId = "c1",
        string secret = "correct-secret",
        bool clientActive = true,
        bool tenantActive = true)
    {
        var db = NewDb();
        var tenant = new ApiTenant { Id = Guid.NewGuid(), Name = "T", IsActive = tenantActive };
        db.Tenants.Add(tenant);
        db.Clients.Add(new ApiClient
        {
            Id = Guid.NewGuid(),
            TenantId = tenant.Id,
            Tenant = tenant,
            ClientId = clientId,
            ClientSecretHash = Hasher.Hash(secret),
            IsActive = clientActive
        });
        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task Returns_client_for_correct_secret()
    {
        var sut = new ClientAuthService(SeedClient(), Hasher);
        var result = await sut.ValidateAsync("c1", "correct-secret");
        Assert.NotNull(result);
        Assert.Equal("c1", result!.ClientId);
    }

    [Fact]
    public async Task Returns_null_for_wrong_secret()
    {
        var sut = new ClientAuthService(SeedClient(), Hasher);
        Assert.Null(await sut.ValidateAsync("c1", "wrong-secret"));
    }

    [Fact]
    public async Task Returns_null_for_unknown_client()
    {
        var sut = new ClientAuthService(SeedClient(), Hasher);
        Assert.Null(await sut.ValidateAsync("does-not-exist", "correct-secret"));
    }

    [Fact]
    public async Task Returns_null_for_inactive_client()
    {
        var sut = new ClientAuthService(SeedClient(clientActive: false), Hasher);
        Assert.Null(await sut.ValidateAsync("c1", "correct-secret"));
    }

    [Fact]
    public async Task Returns_null_for_inactive_tenant()
    {
        var sut = new ClientAuthService(SeedClient(tenantActive: false), Hasher);
        Assert.Null(await sut.ValidateAsync("c1", "correct-secret"));
    }

    [Theory]
    [InlineData("", "secret")]
    [InlineData("c1", "")]
    [InlineData(" ", " ")]
    public async Task Returns_null_for_empty_input(string clientId, string secret)
    {
        var sut = new ClientAuthService(SeedClient(), Hasher);
        Assert.Null(await sut.ValidateAsync(clientId, secret));
    }
}
