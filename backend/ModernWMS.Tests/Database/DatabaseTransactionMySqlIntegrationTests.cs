using Dapper;
using ModernWMS.Core.Database;
using MySqlConnector;

namespace ModernWMS.Tests.Database;

public class DatabaseTransactionMySqlIntegrationTests
{
    [DevelopmentMySqlFact]
    public async Task Development_database_commits_and_rolls_back_real_mysql_transactions()
    {
        var connectionString = Environment.GetEnvironmentVariable("MODERNWMS_TEST_MYSQL")!;

        var settings = new MySqlConnectionStringBuilder(connectionString);
        Assert.Contains(
            settings.Server,
            new[] { "127.0.0.1", "localhost", "::1" },
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal("ruoyi-vue-pro", settings.Database);

        var tableName = $"wms_test_tx_{Guid.NewGuid():N}";
        await using var connectionFactory = new MySqlConnectionFactory(connectionString);
        var executor = new DatabaseTransactionExecutor(
            new MySqlDatabaseSessionFactory(connectionFactory));

        await using var setupConnection = await connectionFactory.OpenConnectionAsync();
        await setupConnection.ExecuteAsync(
            $"CREATE TABLE `{tableName}` (`id` INT NOT NULL PRIMARY KEY) ENGINE=InnoDB;");

        try
        {
            await executor.ExecuteAsync(async (session, cancellationToken) =>
            {
                await session.Connection.ExecuteAsync(new CommandDefinition(
                    $"INSERT INTO `{tableName}` (`id`) VALUES (1);",
                    transaction: session.Transaction,
                    cancellationToken: cancellationToken));
                return 0;
            });

            await Assert.ThrowsAsync<ExpectedRollbackException>(() =>
                executor.ExecuteAsync<int>(async (session, cancellationToken) =>
                {
                    await session.Connection.ExecuteAsync(new CommandDefinition(
                        $"INSERT INTO `{tableName}` (`id`) VALUES (2);",
                        transaction: session.Transaction,
                        cancellationToken: cancellationToken));
                    throw new ExpectedRollbackException();
                }));

            var ids = (await setupConnection.QueryAsync<int>(
                $"SELECT `id` FROM `{tableName}` ORDER BY `id`;"))
                .ToArray();
            Assert.Equal([1], ids);
        }
        finally
        {
            await setupConnection.ExecuteAsync($"DROP TABLE IF EXISTS `{tableName}`;");
        }
    }

    private sealed class ExpectedRollbackException : Exception;
}

public sealed class DevelopmentMySqlFactAttribute : FactAttribute
{
    public DevelopmentMySqlFactAttribute()
    {
        var connectionString = Environment.GetEnvironmentVariable("MODERNWMS_TEST_MYSQL");
        var purpose = Environment.GetEnvironmentVariable("MODERNWMS_TEST_MYSQL_PURPOSE");
        if (string.IsNullOrWhiteSpace(connectionString) || purpose != "DEVELOPMENT_ONLY")
        {
            Skip = "Requires the explicitly authorized development MySQL database.";
        }
    }
}
