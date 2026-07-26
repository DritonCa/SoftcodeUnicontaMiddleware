using System.Security.Cryptography;
using System.Text;

namespace SoftcodeUnicontaMiddleware.Services;

/// <summary>
/// Hashes and verifies client secrets with a keyed HMAC-SHA256.
/// The key ("pepper") is a server-side secret from configuration, so a leaked
/// database alone cannot be used to reconstruct or brute-force secrets.
/// Verification is constant-time to avoid timing side-channels.
/// </summary>
public sealed class SecretHasher
{
    private readonly byte[] _pepper;

    public SecretHasher(IConfiguration configuration)
        : this(configuration["Auth:SecretPepper"]
            ?? throw new InvalidOperationException("Auth:SecretPepper is not configured"))
    {
    }

    // Direct-key overload keeps the type trivially unit-testable.
    public SecretHasher(string pepper)
    {
        if (string.IsNullOrWhiteSpace(pepper))
            throw new ArgumentException("Pepper must not be empty", nameof(pepper));
        _pepper = Encoding.UTF8.GetBytes(pepper);
    }

    public string Hash(string value)
    {
        using var hmac = new HMACSHA256(_pepper);
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }

    public bool Verify(string value, string expectedHashHex)
    {
        if (string.IsNullOrEmpty(expectedHashHex))
            return false;

        var actual = Convert.FromHexString(Hash(value));
        byte[] expected;
        try
        {
            expected = Convert.FromHexString(expectedHashHex);
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
