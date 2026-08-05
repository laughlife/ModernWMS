using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ModernWMS.Core.JWT;

/// <summary>
/// Creates the validation rules shared by every JWT validation path.
/// </summary>
public static class TokenValidationParametersFactory
{
    /// <summary>
    /// Builds strict validation parameters from application settings.
    /// </summary>
    public static TokenValidationParameters Create(TokenSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.Audience);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.Issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.SigningKey);

        var keyBytes = Encoding.UTF8.GetBytes(settings.SigningKey);
        if (keyBytes.Length < 32)
        {
            throw new InvalidOperationException("TokenSettings:SigningKey must contain at least 32 UTF-8 bytes.");
        }

        return new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidAudience = settings.Audience,
            ValidateIssuer = true,
            ValidIssuer = settings.Issuer,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(keyBytes),
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.Zero,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
        };
    }
}
