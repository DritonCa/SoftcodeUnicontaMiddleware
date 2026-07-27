using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;

namespace SoftcodeUnicontaMiddleware.Services
{
    public class JwtTokenService
    {
        private readonly IConfiguration _config;

        public JwtTokenService(IConfiguration config)
        {
            _config = config;
        }

        public string GenerateRefreshToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(64);
            return Convert.ToBase64String(bytes);
        }

        // The access token carries only a non-secret identity (username + companyId).
        // The Uniconta password and API key are NEVER placed in the JWT: a JWT payload
        // is merely base64-encoded, so anyone who intercepts the token could read them.
        // Secrets live server-side in IUnicontaCredentialStore, keyed by this identity.
        public string CreateToken(
            string username,
            int companyId)
        {
            var jwt = _config.GetSection("Jwt");
            var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt["Key"]));

            var claims = new[]
            {
                new Claim("username", username),
                new Claim("companyId", companyId.ToString())
            };

            var token = new JwtSecurityToken(
                issuer: jwt["Issuer"],
                audience: jwt["Audience"],
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(
                    int.Parse(jwt["ExpiresMinutes"] ?? "60")
                ),
                signingCredentials: new SigningCredentials(
                    key,
                    SecurityAlgorithms.HmacSha256
                )
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
