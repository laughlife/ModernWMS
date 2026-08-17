using ModernWMS.Core.Database;
using MySqlConnector;

namespace ModernWMS.Tests.Rolemenu;

internal sealed class UnavailableMySqlConnectionFactory : IMySqlConnectionFactory
{
    public MySqlConnection CreateConnection() => throw new NotSupportedException();

    public ValueTask<MySqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
