using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using ModernWMS.Core.DBContext;
using ModernWMS.Migrations;
using ModernWMS.WMS.Entities.Models;

namespace ModernWMS.Tests.Database;

public class DispatchWorkflowOperationModelTests
{
    [Fact]
    public void Model_has_prefixed_idempotency_table_safe_unique_key_and_restricted_order_fk()
    {
        using var database = CreateDatabase();
        var entity = database.Model.FindEntityType(typeof(DispatchWorkflowOperationEntity))!;

        Assert.Equal("wms_dispatch_workflow_operation", entity.GetTableName());
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual([
                nameof(DispatchWorkflowOperationEntity.dispatch_order_id),
                nameof(DispatchWorkflowOperationEntity.operation),
                nameof(DispatchWorkflowOperationEntity.request_id)
            ]));
        Assert.Equal(64, entity.FindProperty(nameof(DispatchWorkflowOperationEntity.request_id))!.GetMaxLength());
        Assert.True(entity.FindProperty(nameof(DispatchWorkflowOperationEntity.result_order_status))!.IsNullable);
        Assert.Equal(typeof(DispatchOrderStatus?),
            entity.FindProperty(nameof(DispatchWorkflowOperationEntity.result_order_status))!.ClrType);
        var foreignKey = Assert.Single(entity.GetForeignKeys(), key =>
            key.PrincipalEntityType.ClrType == typeof(DispatchOrderEntity));
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }

    [Fact]
    public void Operation_enum_reserves_complete_picking_and_followup_workflow_commands()
    {
        Assert.Equal((byte)10, (byte)DispatchWorkflowOperation.CompletePicking);
        Assert.Equal((byte)20, (byte)DispatchWorkflowOperation.StartWeighing);
        Assert.Equal((byte)30, (byte)DispatchWorkflowOperation.SaveWeighing);
        Assert.Equal((byte)35, (byte)DispatchWorkflowOperation.CopyWeighing);
        Assert.Equal((byte)40, (byte)DispatchWorkflowOperation.CompleteTaskWeighing);
        Assert.Equal((byte)50, (byte)DispatchWorkflowOperation.CompleteWeighing);
        Assert.Equal((byte)60, (byte)DispatchWorkflowOperation.ConfirmOutbound);
    }

    [Fact]
    public void Additive_migration_only_creates_operation_ledger_with_restricted_fk()
    {
        var migration = new AddDispatchWorkflowOperation();
        var table = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal("wms_dispatch_workflow_operation", table.Name);

        var foreignKey = Assert.Single(table.ForeignKeys, key =>
            key.PrincipalTable == "wms_dispatch_order");
        Assert.Equal(ReferentialAction.Restrict, foreignKey.OnDelete);
        var resultOrderStatus = Assert.Single(table.Columns, column => column.Name == "result_order_status");
        Assert.True(resultOrderStatus.IsNullable);
        Assert.Contains(migration.UpOperations.OfType<CreateIndexOperation>(), index => index.IsUnique
            && index.Table == table.Name
            && index.Columns.SequenceEqual(["dispatch_order_id", "operation", "request_id"]));
    }

    private static SqlDBContext CreateDatabase() => new(
        new DbContextOptionsBuilder<SqlDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
