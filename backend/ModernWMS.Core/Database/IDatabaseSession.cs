using System.Data;
using System.Data.Common;

namespace ModernWMS.Core.Database;

/// <summary>
/// One open database connection and its active transaction.
/// </summary>
public interface IDatabaseSession : IAsyncDisposable
{
    DbConnection Connection { get; }
    DbTransaction Transaction { get; }
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Starts sessions against the shared WMS/ERP MySQL database.
/// </summary>
public interface IDatabaseSessionFactory
{
    ValueTask<IDatabaseSession> BeginAsync(
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken = default);
}
