using Microsoft.Extensions.DependencyInjection;
using ModernWMS.Core.Database;
using MySqlConnector;

namespace ModernWMS.Tests.Database;

public class MySqlConnectionFactoryTests
{
    private const string ConnectionString =
        "Server=127.0.0.1;Port=3306;Database=modernwms_test;User Id=test;Password=test;Pooling=true";

    [Fact]
    public void CreateConnection_returns_an_unopened_MySqlConnector_connection()
    {
        using var factory = new MySqlConnectionFactory(ConnectionString);

        using var connection = factory.CreateConnection();

        Assert.IsType<MySqlConnection>(connection);
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
        Assert.Equal("modernwms_test", connection.Database);
        Assert.Equal("127.0.0.1", connection.DataSource);
    }

    [Fact]
    public void AddModernWmsDatabase_registers_one_shared_connection_factory()
    {
        var services = new ServiceCollection();

        services.AddModernWmsDatabase(ConnectionString);

        using var provider = services.BuildServiceProvider();
        var first = provider.GetRequiredService<IMySqlConnectionFactory>();
        var second = provider.GetRequiredService<IMySqlConnectionFactory>();
        Assert.Same(first, second);
    }
}
