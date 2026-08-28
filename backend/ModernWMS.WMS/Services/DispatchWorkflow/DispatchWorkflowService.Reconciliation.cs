using System.Data;
using Dapper;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;
using ModernWMS.WMS.Entities.ViewModels.PackingTask;

namespace ModernWMS.WMS.Services.DispatchWorkflow;

/// <summary>
/// 表示 DispatchWorkflowService 类型。
/// </summary>
public partial class DispatchWorkflowService
{
    /// <summary>
    /// 执行 ReconcileAsync 操作。
    /// </summary>
    public async Task<DispatchOrderDetailViewModel> ReconcileAsync(int orderId, CurrentUser currentUser,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        try
        {
            var order = await LoadReconcileOrderAsync(connection, tx, orderId, cancellationToken);
            await _warehouseAccessService.EnsureAllowedAsync(order.warehouse_id, currentUser);
            if (order.status != DispatchOrderStatus.PendingPick)
            {
                await tx.CommitAsync(cancellationToken);
                return await LoadDetailAsync(orderId, cancellationToken);
            }
            var tasks = order.packing_tasks.Where(x=>x.is_active).ToList();
            if (tasks.Count==0)
            {
                await connection.ExecuteAsync(new CommandDefinition("""
                    UPDATE `wms_dispatch_order` SET `status`=@status,`last_update_time`=@now,`row_version`=`row_version`+1 WHERE `id`=@orderId;
                    """,new {status=DispatchOrderStatus.SourceCancelled,now=DateTime.Now,orderId},tx,cancellationToken:cancellationToken));
                await tx.CommitAsync(cancellationToken); return await LoadDetailAsync(orderId,cancellationToken);
            }
            var snapshots=await _sourceReader.ReadAsync(tasks.Select(x=>x.source_task_id).ToArray(),cancellationToken);
            if(snapshots.Count!=tasks.Count) throw new InvalidOperationException("one or more packing tasks are missing during reconciliation");
            if(snapshots.Where(x=>!x.IsCancelled).Any(x=>x.WarehouseId!=order.warehouse_id))
                throw new InvalidOperationException("packing task warehouse changed; order reconciliation rejected");
            var runtime = await LoadInventoryRuntimeAsync(connection, tx,
                order.warehouse_id, cancellationToken);
            // 兼容本功能上线前已生成的待拣货单：选择记录仍在时补齐可用量快照。
            var legacyBindings=runtime.Mode==CanonicalInventoryMode
                ? new List<CreationBindingRow>()
                : await LoadCreationBindingRowsAsync(connection,tx,
                    tasks.Select(x=>x.source_task_id).ToArray(),false,cancellationToken);
            var legacyRequiredQty=legacyBindings.GroupBy(x=>(x.TaskId,x.ItemId))
                .ToDictionary(x=>x.Key,x=>x.Sum(row=>row.LockedQty));
            var legacyAvailableSnapshots=BuildAvailableSnapshots(snapshots,legacyBindings);
            var now=DateTime.Now;
            var changed=false;
            foreach(var task in tasks)
            {
                var snapshot=snapshots.Single(x=>x.SourceTaskId==task.source_task_id);
                if(snapshot.IsCancelled) { await CancelTaskAsync(connection,tx,task,order,currentUser,
                    runtime.Mode == CanonicalInventoryMode,now,cancellationToken); changed=true; }
                else if(!string.Equals(task.source_version,snapshot.SourceVersion,StringComparison.Ordinal))
                {
                    await RemoveTaskAllocationsAsync(connection,tx,task,order,currentUser,
                        runtime.Mode == CanonicalInventoryMode,"RECONCILE",cancellationToken);
                    await RebuildTaskItemsAsync(connection,tx,task,snapshot,now,cancellationToken);
                    changed=true;
                }
                else await connection.ExecuteAsync(new CommandDefinition("""
                    UPDATE `wms_dispatch_packing_task_item` SET `wms_sku_id`=NULL,`erp_stock_plan_row_version`=NULL,
                    `last_update_time`=@now,`row_version`=`row_version`+1
                    WHERE `packing_task_id`=@taskId AND `is_active`=1 AND `wms_sku_id` IS NOT NULL;
                    """,new {now,taskId=task.id},tx,cancellationToken:cancellationToken));
            }
            foreach(var task in tasks.Where(x=>x.is_active))
            {
                var snapshot=snapshots.Single(x=>x.SourceTaskId==task.source_task_id);
                if(snapshot.IsCancelled) continue;
                foreach(var item in snapshot.Items)
                {
                    if(!legacyRequiredQty.TryGetValue((snapshot.SourceTaskId,item.SourceItemId),out var requiredQty)) continue;
                    if(!legacyAvailableSnapshots.TryGetValue((snapshot.SourceTaskId,item.SourceItemId),out var available)) continue;
                    var backfilled=await connection.ExecuteAsync(new CommandDefinition("""
                        UPDATE `wms_dispatch_packing_task_item`
                        SET `required_qty`=@requiredQty,`source_quantity_shipped`=@Quantity,
                            `source_stock_available`=COALESCE(`source_stock_available`,@available),
                            `last_update_time`=@now,`row_version`=`row_version`+1
                        WHERE `packing_task_id`=@taskId AND `source_item_id`=@SourceItemId
                          AND `is_active`=1
                          AND (`required_qty`<>@requiredQty OR `source_quantity_shipped`<>@Quantity
                               OR `source_stock_available` IS NULL);
                        """,new{item.Quantity,requiredQty,available,now,taskId=task.id,item.SourceItemId},tx,
                        cancellationToken:cancellationToken));
                    changed=changed||backfilled>0;
                }
            }
            // 仅在确有变化时推进订单版本：待拣货页每次加载与角标刷新都会对全部待拣货订单执行
            // reconcile，若每次都无条件 row_version+1，前端缓存版本会迅速过期，导致回退/拣货
            // 复核等命令误报 CONCURRENCY_CONFLICT。
            if(changed)
            {
                var updated=await connection.ExecuteAsync(new CommandDefinition("""
                    UPDATE `wms_dispatch_order` SET `status`=@status,`source_version`=@version,`source_snapshot`=@snapshot,
                      `last_update_time`=@now,`row_version`=`row_version`+1 WHERE `id`=@orderId;
                    """,new {status=snapshots.Any(x=>!x.IsCancelled)?DispatchOrderStatus.PendingPick:DispatchOrderStatus.SourceCancelled,
                        version=CombinedVersion(snapshots),snapshot=SnapshotJson(snapshots),now,orderId},tx,cancellationToken:cancellationToken));
                if(updated!=1) throw new InvalidOperationException("dispatch order reconciliation failed to update the order");
            }
            await EnsureSourceUnchangedAsync(tasks,snapshots,cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return await LoadDetailAsync(orderId,cancellationToken);
        }
        catch { await tx.RollbackAsync(CancellationToken.None); throw; }
    }

    private async Task EnsureSourceUnchangedAsync(IReadOnlyCollection<DispatchPackingTaskEntity> tasks,
        IReadOnlyList<PackingTaskSourceSnapshot> snapshots,CancellationToken ct)
    {
        var commit=await _sourceReader.ReadAsync(tasks.Select(x=>x.source_task_id).ToArray(),ct);
        if(!string.Equals(SnapshotJson(commit),SnapshotJson(snapshots),StringComparison.Ordinal))
            throw new InvalidOperationException("packing task source changed during reconciliation");
    }

    private static async Task<DispatchOrderEntity> LoadReconcileOrderAsync(System.Data.IDbConnection c,IDbTransaction tx,int id,CancellationToken ct)
    {
        using var r=await c.QueryMultipleAsync(new CommandDefinition("""
            SELECT * FROM `wms_dispatch_order` WHERE `id`=@id FOR UPDATE;
            SELECT * FROM `wms_dispatch_packing_task` WHERE `dispatch_order_id`=@id AND `is_active`=1 FOR UPDATE;
            SELECT i.* FROM `wms_dispatch_packing_task_item` i JOIN `wms_dispatch_packing_task` t ON t.`id`=i.`packing_task_id`
            WHERE t.`dispatch_order_id`=@id AND t.`is_active`=1 FOR UPDATE;
            """,new{id},tx,cancellationToken:ct));
        var order=await r.ReadSingleOrDefaultAsync<DispatchOrderEntity>()??throw new KeyNotFoundException($"dispatch order not found: {id}");
        order.packing_tasks=(await r.ReadAsync<DispatchPackingTaskEntity>()).AsList(); var items=(await r.ReadAsync<DispatchPackingTaskItemEntity>()).AsList();
        foreach(var t in order.packing_tasks)t.items=items.Where(x=>x.packing_task_id==t.id).ToList(); return order;
    }

    private async Task RemoveTaskAllocationsAsync(System.Data.IDbConnection c,IDbTransaction tx,
        DispatchPackingTaskEntity task,DispatchOrderEntity order,CurrentUser user,bool canonical,
        string requestIdentity,CancellationToken ct)
    {
        var ids=task.items.Where(x=>x.id>0).Select(x=>x.id).ToArray(); if(ids.Length==0)return;
        if(await c.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT EXISTS(SELECT 1 FROM `wms_dispatchpicklist` WHERE `packing_task_item_id` IN @ids AND `is_update_stock`=1);
            """,new{ids},tx,cancellationToken:ct)))
            throw new InvalidOperationException("packing task has allocations that already updated stock; automatic reconciliation is forbidden");
        var now=DateTime.Now;
        // 统一模式的预占生命周期由 Ruoyi 按装箱任务来源事件处理；WMS 对账只清理本地工作流投影。
        if (!canonical)
            await c.ExecuteAsync(new CommandDefinition("""
            UPDATE `wms_packing_task_stock_selection`
               SET `status`='CANCELLED',`cancelled_by`=@cancelledBy,
                   `cancelled_by_name`=@cancelledByName,`cancelled_at`=@now,
                   `cancel_reason`=@cancelReason,
                   `operation_source`='DISPATCH_RECONCILIATION',
                   `last_update_time`=@now,`row_version`=`row_version`+1
             WHERE `sellfox_task_id`=@sourceTaskId
               AND `status`='ACTIVE';
            """,new
        {
            sourceTaskId=task.source_task_id,
            cancelledBy=user.user_id,
            cancelledByName=user.user_name??string.Empty,
            cancelReason=requestIdentity=="SOURCE_CANCEL"
                ? "装箱任务来源取消释放库存选择"
                : "拣货前重建释放库存选择",
            now
        },tx,cancellationToken:ct));
        await c.ExecuteAsync(new CommandDefinition("DELETE FROM `wms_dispatchpicklist` WHERE `packing_task_item_id` IN @ids AND `is_update_stock`=0;",
            new{ids},tx,cancellationToken:ct));
    }

    private async Task CancelTaskAsync(System.Data.IDbConnection c,IDbTransaction tx,DispatchPackingTaskEntity task,
        DispatchOrderEntity order,CurrentUser user,bool canonical,DateTime now,CancellationToken ct)
    {
        await RemoveTaskAllocationsAsync(c,tx,task,order,user,canonical,"SOURCE_CANCEL",ct);
        await c.ExecuteAsync(new CommandDefinition("""
            UPDATE `wms_dispatch_packing_task_item` SET `is_active`=0,`last_update_time`=@now,`row_version`=`row_version`+1 WHERE `packing_task_id`=@id;
            UPDATE `wms_dispatch_packing_task` SET `is_active`=0,`active_source_task_id`=NULL,`source_cancelled_at`=@now,
              `status`=@status,`last_update_time`=@now,`row_version`=`row_version`+1 WHERE `id`=@id;
            """,new{now,id=task.id,status=DispatchOrderStatus.SourceCancelled},tx,cancellationToken:ct));
    }

    private static async Task RebuildTaskItemsAsync(System.Data.IDbConnection c,IDbTransaction tx,DispatchPackingTaskEntity task,
        PackingTaskSourceSnapshot snapshot,DateTime now,CancellationToken ct)
    {
        var source=snapshot.Items.ToDictionary(x=>x.SourceItemId);
        foreach(var existing in task.items)
        {
            if(source.Remove(existing.source_item_id,out var item))
                await c.ExecuteAsync(new CommandDefinition("""
                    UPDATE `wms_dispatch_packing_task_item` SET `source_commodity_id`=@CommodityId,`wms_sku_id`=NULL,
                      `erp_stock_plan_row_version`=NULL,`commodity_sku`=@CommoditySku,
                      `commodity_name`=@CommodityName,`fn_sku`=@FnSku,`msku`=@Msku,`required_qty`=@Quantity,`source_quantity_shipped`=@Quantity,
                      `source_version`=@version,`source_snapshot`=@SourceSnapshot,`is_active`=1,`last_update_time`=@now,`row_version`=`row_version`+1 WHERE `id`=@id;
                    """,new{item.CommodityId,item.CommoditySku,item.CommodityName,item.FnSku,item.Msku,item.Quantity,version=snapshot.SourceVersion,item.SourceSnapshot,now,existing.id},tx,cancellationToken:ct));
            else await c.ExecuteAsync(new CommandDefinition("UPDATE `wms_dispatch_packing_task_item` SET `is_active`=0,`last_update_time`=@now,`row_version`=`row_version`+1 WHERE `id`=@id;",new{now,existing.id},tx,cancellationToken:ct));
        }
        foreach(var item in source.Values)
        {
            var entity=CreateItem(item,snapshot.SourceVersion,null,now); entity.packing_task_id=task.id;
            await c.ExecuteAsync(new CommandDefinition("""
                INSERT INTO `wms_dispatch_packing_task_item` (`packing_task_id`,`source_item_id`,`source_commodity_id`,`wms_sku_id`,`commodity_sku`,`commodity_name`,`fn_sku`,`msku`,`required_qty`,`source_quantity_shipped`,`source_stock_available`,`source_version`,`source_snapshot`,`is_active`,`create_time`,`last_update_time`,`row_version`)
                VALUES (@packing_task_id,@source_item_id,@source_commodity_id,NULL,@commodity_sku,@commodity_name,@fn_sku,@msku,@required_qty,@source_quantity_shipped,@source_stock_available,@source_version,@source_snapshot,1,@create_time,@last_update_time,0);
                """,entity,tx,cancellationToken:ct));
        }
        await c.ExecuteAsync(new CommandDefinition("""
            UPDATE `wms_dispatch_packing_task` SET `task_no`=@TaskNo,`source_task_no`=@TaskNo,`source_cartons_json`=@CartonsJson,
              `source_version`=@SourceVersion,`expected_box_count`=@boxCount,`stable_box_identity_verified`=@verified,
              `box_identity_validation_error`=@error,`last_update_time`=@now,`row_version`=`row_version`+1 WHERE `id`=@id;
            """,new{snapshot.TaskNo,snapshot.CartonsJson,snapshot.SourceVersion,boxCount=snapshot.Boxes.Count,
                verified=snapshot.Boxes.Count>0&&snapshot.Boxes.All(x=>!string.IsNullOrWhiteSpace(x.SourceBoxIdentity)),
                error=snapshot.Boxes.Count==0?"来源尚未提供物理箱，进入称重前必须同步并验证稳定箱ID":"",now,id=task.id},tx,cancellationToken:ct));
    }
}
