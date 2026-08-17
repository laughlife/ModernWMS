using ModernWMS.Core.Database;
using MySqlConnector;

namespace ModernWMS.Tests.Dispatchlist;

/// <summary>Prevents legacy EF-only tests from accidentally opening a real database connection.</summary>
internal sealed class ForbiddenConnectionFactory : IMySqlConnectionFactory
{
    public static ForbiddenConnectionFactory Instance { get; } = new();

    private ForbiddenConnectionFactory()
    {
    }

    public MySqlConnection CreateConnection() =>
        throw new InvalidOperationException("Legacy EF test attempted to open a Dapper database connection.");

    public ValueTask<MySqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromException<MySqlConnection>(
            new InvalidOperationException("Legacy EF test attempted to open a Dapper database connection."));
}
