using MySqlConnector;

namespace ModernWMS.Core.Database;

/// <summary>
/// Application-wide MySQL connection factory backed by a shared connection pool.
/// </summary>
public sealed class MySqlConnectionFactory : IMySqlConnectionFactory, IDisposable, IAsyncDisposable
{
    private readonly MySqlDataSource _dataSource;

    /// <summary>
    /// Creates a factory for the configured MySQL database.
    /// </summary>
    public MySqlConnectionFactory(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _dataSource = new MySqlDataSourceBuilder(connectionString).Build();
    }

    /// <inheritdoc />
    public MySqlConnection CreateConnection() => _dataSource.CreateConnection();

    /// <inheritdoc />
    public async ValueTask<MySqlConnection> OpenConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        return await _dataSource.OpenConnectionAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose() => _dataSource.Dispose();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}
