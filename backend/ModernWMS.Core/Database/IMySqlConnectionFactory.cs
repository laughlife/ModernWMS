using MySqlConnector;

namespace ModernWMS.Core.Database;

/// <summary>
/// Creates pooled MySQL connections for WMS and ERP data in the shared database.
/// </summary>
public interface IMySqlConnectionFactory
{
    /// <summary>
    /// Creates a closed connection. The caller owns and disposes it.
    /// </summary>
    MySqlConnection CreateConnection();

    /// <summary>
    /// Creates and opens a connection. The caller owns and disposes it.
    /// </summary>
    ValueTask<MySqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);
}
