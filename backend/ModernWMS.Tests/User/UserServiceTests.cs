using Microsoft.Extensions.Localization;
using ModernWMS.Core.Database;
using ModernWMS.WMS.Services;

namespace ModernWMS.Tests.User;

public class UserServiceTests
{
    [Fact]
    public void Constructor_rejects_missing_connection_factory()
    {
        Assert.Throws<ArgumentNullException>(() => new UserService(null!, new TestStringLocalizer()));
    }

    [Fact]
    public void GetRandomPassword_returns_six_allowed_characters()
    {
        var service = new UserService(new MySqlConnectionFactory(
            "Server=127.0.0.1;Database=modernwms_tests;User ID=test;Password=test;SslMode=None"),
            new TestStringLocalizer());

        var password = service.GetRandomPassword();

        Assert.Equal(6, password.Length);
        Assert.All(password, character => Assert.Contains(character, "ABCDEFGHIJKLMNOPQRSTVWXYZ123456789"));
    }

    private sealed class TestStringLocalizer : IStringLocalizer<ModernWMS.Core.MultiLanguage>
    {
        public LocalizedString this[string name] => new(name, name);

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(name, arguments));

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
