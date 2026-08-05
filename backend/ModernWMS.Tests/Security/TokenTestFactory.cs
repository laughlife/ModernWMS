using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using ModernWMS.Core.JWT;

namespace ModernWMS.Tests.Security;

internal static class TokenTestFactory
{
    private const string SigningKey = "0123456789abcdef0123456789abcdef";

    public static void ValidateExpiredToken()
    {
        var settings = new TokenSettings
        {
            Audience = "ModernWMS.Tests",
            Issuer = "ModernWMS.Tests",
            SigningKey = SigningKey,
            ExpireMinute = 60
        };
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            notBefore: DateTime.UtcNow.AddHours(-2),
            expires: DateTime.UtcNow.AddHours(-1),
            signingCredentials: credentials);
        var encodedToken = new JwtSecurityTokenHandler().WriteToken(token);

        new JwtSecurityTokenHandler().ValidateToken(
            encodedToken,
            TokenValidationParametersFactory.Create(settings),
            out _);
    }
}
