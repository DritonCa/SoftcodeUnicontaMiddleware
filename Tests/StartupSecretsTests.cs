using Microsoft.Extensions.Configuration;
using SoftcodeUnicontaMiddleware.Services;
using Xunit;

namespace SoftcodeUnicontaMiddleware.Tests;

public class StartupSecretsTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] entries)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(entries.ToDictionary(e => e.Key, e => e.Value))
            .Build();
    }

    [Fact]
    public void Returns_the_value_when_a_real_secret_is_configured()
    {
        var config = Config(("Jwt:Key", "a-genuinely-long-random-signing-key-01234"));

        Assert.Equal(
            "a-genuinely-long-random-signing-key-01234",
            StartupSecrets.Require(config, "Jwt:Key", 32));
    }

    [Fact]
    public void Throws_when_the_secret_is_missing()
    {
        Assert.Throws<InvalidOperationException>(
            () => StartupSecrets.Require(Config(), "Jwt:Key", 32));
    }

    [Fact]
    public void Throws_when_the_secret_is_still_the_shipped_placeholder()
    {
        // The value from appsettings.json is long enough but must still be rejected.
        var config = Config(("Jwt:Key", "CHANGE_THIS_TO_A_LONG_RANDOM_SECRET_32_CHARS_MIN"));

        Assert.Throws<InvalidOperationException>(
            () => StartupSecrets.Require(config, "Jwt:Key", 32));
    }

    [Fact]
    public void Throws_when_the_secret_is_too_short()
    {
        var config = Config(("Auth:SecretPepper", "short"));

        Assert.Throws<InvalidOperationException>(
            () => StartupSecrets.Require(config, "Auth:SecretPepper", 16));
    }
}
