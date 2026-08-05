using ModernWMS.Core.Configuration;

namespace ModernWMS.Tests.Configuration;

public class DatabaseConfigurationTests
{
    [Fact]
    public void Database_provider_must_be_mysql()
    {
        Assert.Equal("MySql", DatabaseProvider.Name);
    }
}
