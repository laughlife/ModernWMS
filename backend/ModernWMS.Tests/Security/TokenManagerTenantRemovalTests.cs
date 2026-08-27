using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using ModernWMS.Core.JWT;

namespace ModernWMS.Tests.Security;

public sealed class TokenManagerTenantRemovalTests
{
    [Fact]
    public void Access_token_round_trip_contains_only_real_user_identity()
    {
        var settings = Options.Create(new TokenSettings
        {
            Audience = "ModernWMS.Tests",
            Issuer = "ModernWMS.Tests",
            SigningKey = "0123456789abcdef0123456789abcdef",
            ExpireMinute = 60
        });
        var manager = new TokenManager(settings, new HttpContextAccessor());
        var expected = new CurrentUser
        {
            user_id = 42,
            user_num = "U0042",
            user_name = "测试用户",
            user_role = "picker"
        };

        var generated = manager.GenerateToken(expected);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(generated.token);
        var identityJson = Assert.Single(jwt.Claims, claim => claim.Type == ClaimValueTypes.Json).Value;
        var actual = manager.GetCurrentUser(generated.token);

        Assert.DoesNotContain("tenant", identityJson, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expected.user_id, actual.user_id);
        Assert.Equal(expected.user_num, actual.user_num);
        Assert.Equal(expected.user_name, actual.user_name);
        Assert.Equal(expected.user_role, actual.user_role);
    }
}
