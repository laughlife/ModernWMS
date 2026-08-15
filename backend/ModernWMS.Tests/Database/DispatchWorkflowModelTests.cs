using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using ModernWMS.Core.DBContext;
using ModernWMS.Core.DBContext.Entities;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.Migrations;

namespace ModernWMS.Tests.Database;

public class DispatchWorkflowModelTests
{
    [Fact]
    public void Wms_model_has_prefixed_workflow_tables_and_required_identity_indexes()
    {
        using var database = CreateWmsDatabase();
        var model = database.Model;

        Assert.Equal("wms_dispatch_order", model.FindEntityType(typeof(DispatchOrderEntity))!.GetTableName());
        Assert.Equal("wms_dispatch_packing_task", model.FindEntityType(typeof(DispatchPackingTaskEntity))!.GetTableName());
        Assert.Equal("wms_dispatch_packing_task_item", model.FindEntityType(typeof(DispatchPackingTaskItemEntity))!.GetTableName());
        Assert.Equal("wms_dispatch_source_change_event", model.FindEntityType(typeof(DispatchSourceChangeEventEntity))!.GetTableName());
        Assert.Equal("wms_weighing_box", model.FindEntityType(typeof(WeighingBoxEntity))!.GetTableName());
        Assert.Equal("wms_role_warehouse", model.FindEntityType(typeof(RoleWarehouseEntity))!.GetTableName());

        AssertUniqueIndex<DispatchOrderEntity>(model, nameof(DispatchOrderEntity.dispatch_no));
        AssertUniqueIndex<DispatchOrderEntity>(model, nameof(DispatchOrderEntity.create_idempotency_key));
        AssertUniqueIndex<DispatchPackingTaskEntity>(model, nameof(DispatchPackingTaskEntity.active_source_task_id));
        AssertUniqueIndex<DispatchPackingTaskItemEntity>(model,
            nameof(DispatchPackingTaskItemEntity.packing_task_id), nameof(DispatchPackingTaskItemEntity.source_item_id));
        AssertUniqueIndex<DispatchSourceChangeEventEntity>(model,
            nameof(DispatchSourceChangeEventEntity.dispatch_order_id), nameof(DispatchSourceChangeEventEntity.source_version));
        AssertUniqueIndex<WeighingBoxEntity>(model,
            nameof(WeighingBoxEntity.packing_task_id), nameof(WeighingBoxEntity.source_box_identity));
        AssertUniqueIndex<RoleWarehouseEntity>(model,
            nameof(RoleWarehouseEntity.role_id), nameof(RoleWarehouseEntity.warehouse_id));
    }

    [Fact]
    public void Historical_dispatch_rows_keep_nullable_new_workflow_ownership()
    {
        using var database = CreateWmsDatabase();
        var entity = database.Model.FindEntityType(typeof(DispatchlistEntity))!;

        Assert.True(entity.FindProperty(nameof(DispatchlistEntity.dispatch_order_id))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(DispatchlistEntity.packing_task_id))!.IsNullable);
        Assert.True(entity.FindProperty(nameof(DispatchlistEntity.packing_task_item_id))!.IsNullable);
        var allocation = database.Model.FindEntityType(typeof(DispatchpicklistEntity))!;
        Assert.True(allocation.FindProperty(nameof(DispatchpicklistEntity.packing_task_item_id))!.IsNullable);
    }

    [Fact]
    public void Workflow_statuses_are_controlled_enums_and_active_task_key_tracks_active_state()
    {
        using var database = CreateWmsDatabase();
        Assert.Equal(typeof(DispatchOrderStatus), database.Model.FindEntityType(typeof(DispatchOrderEntity))!
            .FindProperty(nameof(DispatchOrderEntity.status))!.ClrType);
        Assert.Equal(typeof(DispatchOrderStatus), database.Model.FindEntityType(typeof(DispatchPackingTaskEntity))!
            .FindProperty(nameof(DispatchPackingTaskEntity.status))!.ClrType);
        Assert.Equal((byte)20, (byte)DispatchOrderStatus.PendingPick);
        Assert.Equal((byte)30, (byte)DispatchOrderStatus.Picked);
        Assert.Equal((byte)40, (byte)DispatchOrderStatus.Weighing);
        Assert.Equal((byte)50, (byte)DispatchOrderStatus.PendingOutbound);
        Assert.Equal((byte)60, (byte)DispatchOrderStatus.Outbound);
        Assert.Equal((byte)90, (byte)DispatchOrderStatus.SourceCancelled);
        Assert.Equal((byte)91, (byte)DispatchOrderStatus.ManualCancelled);

        var task = new DispatchPackingTaskEntity { source_task_id = 9988 };
        task.SetActiveState(true);
        Assert.True(task.is_active);
        Assert.Equal(task.source_task_id, task.active_source_task_id);
        task.SetActiveState(false);
        Assert.False(task.is_active);
        Assert.Null(task.active_source_task_id);
    }

    [Fact]
    public void Source_change_audit_cannot_be_cascade_deleted_with_order()
    {
        using var database = CreateWmsDatabase();
        var foreignKey = Assert.Single(database.Model.FindEntityType(typeof(DispatchSourceChangeEventEntity))!
            .GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(DispatchOrderEntity));
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);

        var migration = new AddPackingTaskDispatchWorkflow();
        var operation = Assert.Single(migration.UpOperations.OfType<AddForeignKeyOperation>(), key =>
            key.Table == "wms_dispatch_source_change_event" && key.PrincipalTable == "wms_dispatch_order");
        Assert.Equal(ReferentialAction.Restrict, operation.OnDelete);
    }

    [Fact]
    public void New_weighing_boxes_are_separate_from_historical_fba_measurements()
    {
        using var database = CreateWmsDatabase();
        Assert.Equal("wms_weighing_box", database.Model.FindEntityType(typeof(WeighingBoxEntity))!.GetTableName());
        Assert.Equal("wms_dispatch_weighing_box", database.Model.FindEntityType(typeof(DispatchWeighingBoxEntity))!.GetTableName());
    }

    [Fact]
    public void Sellfox_read_model_maps_source_version_and_carton_snapshot_fields()
    {
        using var database = CreateRuoyiDatabase();
        var task = database.Model.FindEntityType(typeof(ErpPackingTaskEntity))!;

        Assert.Equal("ruiyi_sellfox_packing_task", task.GetTableName());
        Assert.NotNull(task.FindProperty(nameof(ErpPackingTaskEntity.cartons_json)));
        Assert.NotNull(task.FindProperty(nameof(ErpPackingTaskEntity.source_hash)));
        Assert.NotNull(task.FindProperty(nameof(ErpPackingTaskEntity.last_sync_time)));
        Assert.NotNull(task.FindProperty(nameof(ErpPackingTaskEntity.source_status)));

        var item = database.Model.FindEntityType(typeof(ErpPackingTaskItemEntity))!;
        Assert.NotNull(item.FindProperty(nameof(ErpPackingTaskItemEntity.source_hash)));
        Assert.NotNull(item.FindProperty(nameof(ErpPackingTaskItemEntity.raw_json)));
    }

    [Fact]
    public void Stable_source_box_identity_is_required_and_sequence_is_not_an_identity_key()
    {
        using var database = CreateWmsDatabase();
        var entity = database.Model.FindEntityType(typeof(WeighingBoxEntity))!;

        Assert.False(entity.FindProperty(nameof(WeighingBoxEntity.source_box_identity))!.IsNullable);
        Assert.DoesNotContain(entity.GetIndexes(), index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(WeighingBoxEntity.packing_task_id), nameof(WeighingBoxEntity.box_sequence)])
            && index.IsUnique);
    }

    [Fact]
    public void Migration_is_single_expand_only_workflow_change_and_matches_active_task_identity()
    {
        var migration = new AddPackingTaskDispatchWorkflow();
        var createdTables = migration.UpOperations.OfType<CreateTableOperation>()
            .Select(operation => operation.Name)
            .Order()
            .ToArray();

        Assert.Equal([
            "wms_dispatch_order",
            "wms_dispatch_packing_task",
            "wms_dispatch_packing_task_item",
            "wms_dispatch_source_change_event",
            "wms_role_warehouse",
            "wms_weighing_box"
        ], createdTables);
        Assert.Equal(3, migration.UpOperations.OfType<AddColumnOperation>()
            .Count(operation => operation.Table == "wms_dispatchlist" && operation.IsNullable));
        Assert.Single(migration.UpOperations.OfType<AddColumnOperation>(),
            operation => operation.Table == "wms_dispatchpicklist" && operation.IsNullable);

        var targetTask = migration.TargetModel.FindEntityType(typeof(DispatchPackingTaskEntity))!;
        Assert.NotNull(targetTask.FindProperty(nameof(DispatchPackingTaskEntity.active_source_task_id)));
        Assert.Contains(targetTask.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name)
                .SequenceEqual([nameof(DispatchPackingTaskEntity.active_source_task_id)]));
        Assert.Equal(typeof(byte), migration.TargetModel.FindEntityType(typeof(DispatchOrderEntity))!
            .FindProperty(nameof(DispatchOrderEntity.status))!.ClrType);
    }

    private static void AssertUniqueIndex<TEntity>(IModel model, params string[] properties)
    {
        var entity = model.FindEntityType(typeof(TEntity))!;
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique
            && index.Properties.Select(property => property.Name).SequenceEqual(properties));
    }

    private static SqlDBContext CreateWmsDatabase() => new(
        new DbContextOptionsBuilder<SqlDBContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static RuoyiDbContext CreateRuoyiDatabase() => new(
        new DbContextOptionsBuilder<RuoyiDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
