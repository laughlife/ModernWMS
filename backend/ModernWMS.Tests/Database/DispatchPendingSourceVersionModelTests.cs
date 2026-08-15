using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using ModernWMS.Core.DBContext;
using ModernWMS.Migrations;
using ModernWMS.WMS.Entities.Models;

namespace ModernWMS.Tests.Database;

public class DispatchPendingSourceVersionModelTests
{
    [Fact]
    public void Pending_source_version_is_a_bounded_order_field()
    {
        using var database = new SqlDBContext(
            new DbContextOptionsBuilder<SqlDBContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var property = database.Model.FindEntityType(typeof(DispatchOrderEntity))!
            .FindProperty(nameof(DispatchOrderEntity.pending_source_version))!;
        Assert.Equal(64, property.GetMaxLength());
        Assert.False(property.IsNullable);
    }

    [Fact]
    public void Migration_only_adds_pending_source_version_with_empty_default()
    {
        var migration = new AddDispatchPendingSourceVersion();
        var column = Assert.Single(migration.UpOperations.OfType<AddColumnOperation>());

        Assert.Equal("wms_dispatch_order", column.Table);
        Assert.Equal("pending_source_version", column.Name);
        Assert.Equal(64, column.MaxLength);
        Assert.False(column.IsNullable);
        Assert.Equal(string.Empty, column.DefaultValue);
        Assert.Single(migration.DownOperations.OfType<DropColumnOperation>());
    }
}
