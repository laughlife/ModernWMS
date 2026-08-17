using System.Data;
using System.Data.Common;

namespace ModernWMS.Initialization;

/// <summary>
/// Reads MySQL metadata only. It never changes tables or migration history.
/// </summary>
public sealed class MySqlDatabaseSchemaInspector(DbConnection connection) : IDatabaseSchemaInspector
{
    private const string HistoryTableName = "wms_ef_migrations_history";

    public async Task<DatabaseSchemaSnapshot> InspectAsync(CancellationToken cancellationToken = default)
    {
        var shouldClose = connection.State == ConnectionState.Closed;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var tables = await ReadFirstColumnAsync(
                "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = DATABASE()",
                cancellationToken);
            var migrations = tables.Contains(HistoryTableName, StringComparer.OrdinalIgnoreCase)
                ? await ReadFirstColumnAsync(
                    "SELECT MigrationId FROM `wms_ef_migrations_history`",
                    cancellationToken)
                : [];

            return new DatabaseSchemaSnapshot(migrations, tables);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task<List<string>> ReadFirstColumnAsync(
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;

        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (!reader.IsDBNull(0))
            {
                values.Add(reader.GetString(0));
            }
        }

        return values;
    }
}
