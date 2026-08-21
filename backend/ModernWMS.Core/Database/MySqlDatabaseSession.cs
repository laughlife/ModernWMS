using System.Data;
using System.Data.Common;
using MySqlConnector;

namespace ModernWMS.Core.Database;

/// <summary>
/// Starts MySQL transactions on connections from the application-wide pool.
/// </summary>
public sealed class MySqlDatabaseSessionFactory(IMySqlConnectionFactory connectionFactory)
    : IDatabaseSessionFactory
{
    /// <summary>
    /// 执行 BeginAsync 操作。
    /// </summary>
    public async ValueTask<IDatabaseSession> BeginAsync(
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken = default)
    {
        var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
        try
        {
            var transaction = await connection.BeginTransactionAsync(isolationLevel, cancellationToken);
            return new MySqlDatabaseSession(connection, transaction);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}

internal sealed class MySqlDatabaseSession(
    MySqlConnection connection,
    MySqlTransaction transaction) : IDatabaseSession
{
    public DbConnection Connection => connection;
    public DbTransaction Transaction => transaction;

    public Task CommitAsync(CancellationToken cancellationToken = default) =>
        transaction.CommitAsync(cancellationToken);

    public Task RollbackAsync(CancellationToken cancellationToken = default) =>
        transaction.RollbackAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await transaction.DisposeAsync();
        await connection.DisposeAsync();
    }
}
