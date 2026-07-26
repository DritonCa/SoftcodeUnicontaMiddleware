using SoftcodeUnicontaMiddleware.Services;
using Xunit;

namespace SoftcodeUnicontaMiddleware.Tests;

public class SecretHasherTests
{
    private static SecretHasher Hasher(string pepper = "test-pepper") => new(pepper);

    [Fact]
    public void Hash_is_deterministic_for_same_input_and_pepper()
    {
        var h = Hasher();
        Assert.Equal(h.Hash("secret"), h.Hash("secret"));
    }

    [Fact]
    public void Verify_returns_true_for_correct_secret()
    {
        var h = Hasher();
        var stored = h.Hash("correct-secret");
        Assert.True(h.Verify("correct-secret", stored));
    }

    [Fact]
    public void Verify_returns_false_for_wrong_secret()
    {
        var h = Hasher();
        var stored = h.Hash("correct-secret");
        Assert.False(h.Verify("wrong-secret", stored));
    }

    [Fact]
    public void Verify_returns_false_for_malformed_hash()
    {
        var h = Hasher();
        Assert.False(h.Verify("secret", "not-hex-!!"));
        Assert.False(h.Verify("secret", ""));
    }

    [Fact]
    public void Different_peppers_produce_different_hashes()
    {
        // A leaked DB hash is useless without the server-side pepper.
        Assert.NotEqual(Hasher("pepper-a").Hash("secret"), Hasher("pepper-b").Hash("secret"));
    }

    [Fact]
    public void Constructor_rejects_empty_pepper()
    {
        Assert.Throws<ArgumentException>(() => new SecretHasher(""));
    }
}
