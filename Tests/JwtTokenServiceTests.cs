using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Configuration;
using SoftcodeUnicontaMiddleware.Services;
using Xunit;

namespace SoftcodeUnicontaMiddleware.Tests;

public class JwtTokenServiceTests
{
    private static JwtTokenService NewService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "test-signing-key-that-is-at-least-32-bytes",
                ["Jwt:Issuer"] = "softcode-tests",
                ["Jwt:Audience"] = "softcode-tests",
                ["Jwt:ExpiresMinutes"] = "60"
            })
            .Build();

        return new JwtTokenService(config);
    }

    private static JwtSecurityToken Decode(string token)
        => new JwtSecurityTokenHandler().ReadJwtToken(token);

    [Fact]
    public void Access_token_carries_username_and_company()
    {
        var token = Decode(NewService().CreateToken("acme-api", 42));

        Assert.Equal("acme-api", token.Claims.Single(c => c.Type == "username").Value);
        Assert.Equal("42", token.Claims.Single(c => c.Type == "companyId").Value);
    }

    [Fact]
    public void Access_token_never_exposes_uniconta_secrets()
    {
        // Regression guard: the Uniconta API key and password must stay server-side.
        // A JWT payload is only base64-encoded, so any claim here is readable by
        // anyone who intercepts the token.
        var token = Decode(NewService().CreateToken("acme-api", 42));

        Assert.DoesNotContain(token.Claims, c => c.Type == "apiKey");
        Assert.DoesNotContain(token.Claims, c => c.Type == "password");
        Assert.DoesNotContain(token.Claims, c => c.Type == "EncryptedPassword");
    }

    [Fact]
    public void Refresh_tokens_are_unique_and_high_entropy()
    {
        var svc = NewService();

        var a = svc.GenerateRefreshToken();
        var b = svc.GenerateRefreshToken();

        Assert.NotEqual(a, b);
        Assert.True(Convert.FromBase64String(a).Length >= 64); // 64 random bytes
    }
}
