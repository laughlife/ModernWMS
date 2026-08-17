using System.Data;

namespace ModernWMS.Core.Database;

/// <summary>
/// Executes a database operation inside one explicit transaction.
/// </summary>
public interface IDatabaseTransactionExecutor
{
    Task<T> ExecuteAsync<T>(
        Func<IDatabaseSession, CancellationToken, Task<T>> operation,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Owns transaction commit, rollback and session disposal for command services.
/// </summary>
public sealed class DatabaseTransactionExecutor(IDatabaseSessionFactory sessionFactory)
    : IDatabaseTransactionExecutor
{
    public async Task<T> ExecuteAsync<T>(
        Func<IDatabaseSession, CancellationToken, Task<T>> operation,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await using var session = await sessionFactory.BeginAsync(isolationLevel, cancellationToken);
        try
        {
            var result = await operation(session, cancellationToken);
            await session.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            try
            {
                await session.RollbackAsync(CancellationToken.None);
            }
            catch
            {
                // Keep the business/database exception that caused the rollback.
            }

            throw;
        }
    }
}
