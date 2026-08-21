using System.Data;
using System.Data.Common;

namespace ModernWMS.Core.Database;

/// <summary>
/// One open database connection and its active transaction.
/// </summary>
public interface IDatabaseSession : IAsyncDisposable
{
    /// <summary>
    /// 获取或设置 Connection。
    /// </summary>
    DbConnection Connection { get; }
    /// <summary>
    /// 获取或设置 Transaction。
    /// </summary>
    DbTransaction Transaction { get; }
    /// <summary>
    /// 定义 CommitAsync 操作。
    /// </summary>
    Task CommitAsync(CancellationToken cancellationToken = default);
    /// <summary>
    /// 定义 RollbackAsync 操作。
    /// </summary>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Starts sessions against the shared WMS/ERP MySQL database.
/// </summary>
public interface IDatabaseSessionFactory
{
    /// <summary>
    /// 定义 BeginAsync 操作。
    /// </summary>
    ValueTask<IDatabaseSession> BeginAsync(
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken = default);
}
