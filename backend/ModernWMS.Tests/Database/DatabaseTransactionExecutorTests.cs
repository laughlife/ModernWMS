using System.Data.Common;
using ModernWMS.Core.Database;

namespace ModernWMS.Tests.Database;

public class DatabaseTransactionExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_commits_once_and_returns_the_callback_result()
    {
        var session = new RecordingSession();
        var executor = new DatabaseTransactionExecutor(new RecordingSessionFactory(session));

        var result = await executor.ExecuteAsync(
            (_, _) => Task.FromResult(42),
            cancellationToken: CancellationToken.None);

        Assert.Equal(42, result);
        Assert.Equal(1, session.CommitCount);
        Assert.Equal(0, session.RollbackCount);
        Assert.Equal(1, session.DisposeCount);
    }

    [Fact]
    public async Task ExecuteAsync_rolls_back_and_preserves_the_original_exception()
    {
        var session = new RecordingSession();
        var executor = new DatabaseTransactionExecutor(new RecordingSessionFactory(session));
        var expected = new InvalidOperationException("write failed");

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(
            () => executor.ExecuteAsync<int>(
                (_, _) => Task.FromException<int>(expected),
                cancellationToken: CancellationToken.None));

        Assert.Same(expected, actual);
        Assert.Equal(0, session.CommitCount);
        Assert.Equal(1, session.RollbackCount);
        Assert.Equal(1, session.DisposeCount);
    }

    private sealed class RecordingSessionFactory(IDatabaseSession session) : IDatabaseSessionFactory
    {
        public ValueTask<IDatabaseSession> BeginAsync(
            System.Data.IsolationLevel isolationLevel,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(session);
    }

    private sealed class RecordingSession : IDatabaseSession
    {
        public DbConnection Connection => null!;
        public DbTransaction Transaction => null!;
        public int CommitCount { get; private set; }
        public int RollbackCount { get; private set; }
        public int DisposeCount { get; private set; }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            CommitCount++;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            RollbackCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
