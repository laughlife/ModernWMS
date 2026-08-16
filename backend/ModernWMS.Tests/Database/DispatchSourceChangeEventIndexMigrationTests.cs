using Microsoft.EntityFrameworkCore.Migrations.Operations;
using ModernWMS.Migrations;

namespace ModernWMS.Tests.Database;

public class DispatchSourceChangeEventIndexMigrationTests
{
    [Fact]
    public void Migration_replaces_source_version_uniqueness_with_decision_scoped_uniqueness()
    {
        var migration = new ExpandDispatchSourceChangeEventDecisionIndex();

        Assert.Collection(
            migration.UpOperations,
            operation => Assert.IsType<SqlOperation>(operation),
            operation => Assert.IsType<CreateIndexOperation>(operation));

        var dropped = Assert.IsType<SqlOperation>(migration.UpOperations[0]);
        Assert.Equal(
            "DROP INDEX `IX_wms_dispatch_source_change_event_dispatch_order_id_source_ve~` " +
            "ON `wms_dispatch_source_change_event`;",
            dropped.Sql);

        var created = Assert.IsType<CreateIndexOperation>(migration.UpOperations[1]);
        Assert.Equal(
            "IX_wms_dispatch_source_change_event_dispatch_order_id_source_ve~",
            created.Name);
        Assert.Equal("wms_dispatch_source_change_event", created.Table);
        Assert.True(created.IsUnique);
        Assert.Equal(["dispatch_order_id", "source_version", "decision"], created.Columns);
    }

    [Fact]
    public void Down_migration_uses_safe_drop_sql_and_restores_source_version_uniqueness()
    {
        var migration = new ExpandDispatchSourceChangeEventDecisionIndex();

        Assert.Collection(
            migration.DownOperations,
            operation => Assert.IsType<SqlOperation>(operation),
            operation => Assert.IsType<CreateIndexOperation>(operation));

        var dropped = Assert.IsType<SqlOperation>(migration.DownOperations[0]);
        Assert.Equal(
            "DROP INDEX `IX_wms_dispatch_source_change_event_dispatch_order_id_source_ve~` " +
            "ON `wms_dispatch_source_change_event`;",
            dropped.Sql);

        var created = Assert.IsType<CreateIndexOperation>(migration.DownOperations[1]);
        Assert.Equal(
            "IX_wms_dispatch_source_change_event_dispatch_order_id_source_ve~",
            created.Name);
        Assert.Equal("wms_dispatch_source_change_event", created.Table);
        Assert.True(created.IsUnique);
        Assert.Equal(["dispatch_order_id", "source_version"], created.Columns);
    }
}
