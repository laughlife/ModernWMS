using Microsoft.EntityFrameworkCore.Migrations.Operations;
using ModernWMS.Migrations;

namespace ModernWMS.Tests.Database;

public class DispatchSourceChangeEventIndexMigrationTests
{
    private const string TableName = "wms_dispatch_source_change_event";
    private const string DecisionIndexName =
        "IX_wms_dispatch_source_change_event_dispatch_order_id_source_ve~";
    private const string ForeignKeySupportIndexName =
        "IX_wms_dispatch_source_change_event_dispatch_order_id_tmp";

    [Fact]
    public void Migration_replaces_source_version_uniqueness_with_decision_scoped_uniqueness()
    {
        var migration = new ExpandDispatchSourceChangeEventDecisionIndex();

        Assert.Collection(
            migration.UpOperations,
            operation => Assert.IsType<CreateIndexOperation>(operation),
            operation => Assert.IsType<SqlOperation>(operation),
            operation => Assert.IsType<CreateIndexOperation>(operation),
            operation => Assert.IsType<DropIndexOperation>(operation));

        AssertForeignKeySupportIndex(Assert.IsType<CreateIndexOperation>(migration.UpOperations[0]));

        var dropped = Assert.IsType<SqlOperation>(migration.UpOperations[1]);
        Assert.Equal(
            $"DROP INDEX `{DecisionIndexName}` ON `{TableName}`;",
            dropped.Sql);

        var created = Assert.IsType<CreateIndexOperation>(migration.UpOperations[2]);
        Assert.Equal(DecisionIndexName, created.Name);
        Assert.Equal(TableName, created.Table);
        Assert.True(created.IsUnique);
        Assert.Equal(["dispatch_order_id", "source_version", "decision"], created.Columns);

        AssertForeignKeySupportIndexDrop(Assert.IsType<DropIndexOperation>(migration.UpOperations[3]));
        Assert.DoesNotContain(
            migration.UpOperations.OfType<SqlOperation>(),
            operation => operation.Sql.Contains("FOREIGN_KEY_CHECKS", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Down_migration_uses_safe_drop_sql_and_restores_source_version_uniqueness()
    {
        var migration = new ExpandDispatchSourceChangeEventDecisionIndex();

        Assert.Collection(
            migration.DownOperations,
            operation => Assert.IsType<CreateIndexOperation>(operation),
            operation => Assert.IsType<SqlOperation>(operation),
            operation => Assert.IsType<CreateIndexOperation>(operation),
            operation => Assert.IsType<DropIndexOperation>(operation));

        AssertForeignKeySupportIndex(Assert.IsType<CreateIndexOperation>(migration.DownOperations[0]));

        var dropped = Assert.IsType<SqlOperation>(migration.DownOperations[1]);
        Assert.Equal(
            $"DROP INDEX `{DecisionIndexName}` ON `{TableName}`;",
            dropped.Sql);

        var created = Assert.IsType<CreateIndexOperation>(migration.DownOperations[2]);
        Assert.Equal(DecisionIndexName, created.Name);
        Assert.Equal(TableName, created.Table);
        Assert.True(created.IsUnique);
        Assert.Equal(["dispatch_order_id", "source_version"], created.Columns);

        AssertForeignKeySupportIndexDrop(Assert.IsType<DropIndexOperation>(migration.DownOperations[3]));
        Assert.DoesNotContain(
            migration.DownOperations.OfType<SqlOperation>(),
            operation => operation.Sql.Contains("FOREIGN_KEY_CHECKS", StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertForeignKeySupportIndex(CreateIndexOperation operation)
    {
        Assert.Equal(ForeignKeySupportIndexName, operation.Name);
        Assert.Equal(TableName, operation.Table);
        Assert.False(operation.IsUnique);
        Assert.Equal(["dispatch_order_id"], operation.Columns);
    }

    private static void AssertForeignKeySupportIndexDrop(DropIndexOperation operation)
    {
        Assert.Equal(ForeignKeySupportIndexName, operation.Name);
        Assert.Equal(TableName, operation.Table);
    }
}
