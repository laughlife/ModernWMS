using Microsoft.IdentityModel.Tokens;

namespace ModernWMS.Tests.Security;

public class TokenValidationTests
{
    [Fact]
    public void Expired_token_is_rejected()
    {
        Assert.Throws<SecurityTokenExpiredException>(TokenTestFactory.ValidateExpiredToken);
    }
}
