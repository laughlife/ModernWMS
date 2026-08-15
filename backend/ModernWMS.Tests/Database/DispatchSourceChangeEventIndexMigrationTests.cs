using Microsoft.EntityFrameworkCore.Migrations.Operations;
using ModernWMS.Migrations;

namespace ModernWMS.Tests.Database;

public class DispatchSourceChangeEventIndexMigrationTests
{
    [Fact]
    public void Migration_replaces_source_version_uniqueness_with_decision_scoped_uniqueness()
    {
        var migration = new ExpandDispatchSourceChangeEventDecisionIndex();

        var dropped = Assert.Single(migration.UpOperations.OfType<DropIndexOperation>());
        Assert.Equal("wms_dispatch_source_change_event", dropped.Table);

        var created = Assert.Single(migration.UpOperations.OfType<CreateIndexOperation>());
        Assert.Equal("wms_dispatch_source_change_event", created.Table);
        Assert.True(created.IsUnique);
        Assert.Equal(["dispatch_order_id", "source_version", "decision"], created.Columns);
    }
}
