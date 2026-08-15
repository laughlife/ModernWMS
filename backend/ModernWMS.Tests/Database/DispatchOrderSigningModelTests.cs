using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using ModernWMS.Core.DBContext;
using ModernWMS.Migrations;
using ModernWMS.WMS.Entities.Models;

namespace ModernWMS.Tests.Database;

public class DispatchOrderSigningModelTests
{
    [Fact]
    public void Signing_facts_and_notification_recovery_index_are_part_of_dispatch_order()
    {
        using var database = CreateDatabase();
        var entity = database.Model.FindEntityType(typeof(DispatchOrderEntity))!;

        Assert.True(entity.FindProperty(nameof(DispatchOrderEntity.signed_qty))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(DispatchOrderEntity.damaged_qty))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(DispatchOrderEntity.signed_by))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(DispatchOrderEntity.signed_at))!.IsNullable);
        Assert.Equal(128, entity.FindProperty(nameof(DispatchOrderEntity.signed_by_name))!.GetMaxLength());
        Assert.Equal(500, entity.FindProperty(nameof(DispatchOrderEntity.notification_last_error))!.GetMaxLength());
        Assert.Equal(typeof(DispatchSignNotificationStatus),
            entity.FindProperty(nameof(DispatchOrderEntity.notification_status))!.ClrType);
        Assert.Contains(entity.GetIndexes(), index => index.Properties.Select(property => property.Name)
            .SequenceEqual([nameof(DispatchOrderEntity.notification_status), nameof(DispatchOrderEntity.notification_updated_at)]));
    }

    [Fact]
    public void Notification_status_values_are_controlled()
    {
        Assert.Equal((byte)0, (byte)DispatchSignNotificationStatus.None);
        Assert.Equal((byte)10, (byte)DispatchSignNotificationStatus.Pending);
        Assert.Equal((byte)20, (byte)DispatchSignNotificationStatus.Sending);
        Assert.Equal((byte)30, (byte)DispatchSignNotificationStatus.Sent);
        Assert.Equal((byte)40, (byte)DispatchSignNotificationStatus.Failed);
        Assert.DoesNotContain("Signed", Enum.GetNames<DispatchOrderStatus>());
    }

    [Fact]
    public void Additive_migration_only_adds_signing_columns_and_recovery_index()
    {
        var migration = new AddDispatchOrderSigningFacts();
        var addedColumns = migration.UpOperations.OfType<AddColumnOperation>().ToList();

        Assert.Equal(10, addedColumns.Count);
        Assert.All(addedColumns, column => Assert.Equal("wms_dispatch_order", column.Table));
        Assert.Contains(addedColumns, column => column.Name == "notification_status"
            && Equals(column.DefaultValue, (byte)DispatchSignNotificationStatus.None));
        var index = Assert.Single(migration.UpOperations.OfType<CreateIndexOperation>());
        Assert.Equal("wms_dispatch_order", index.Table);
        Assert.Equal(["notification_status", "notification_updated_at"], index.Columns);
        Assert.Empty(migration.UpOperations.OfType<DropColumnOperation>());
    }

    private static SqlDBContext CreateDatabase() => new(
        new DbContextOptionsBuilder<SqlDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
