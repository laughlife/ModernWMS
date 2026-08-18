using System.Data;
using System.Text.Json;
using Dapper;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;
using ModernWMS.WMS.Entities.ViewModels.PackingTask;
using ModernWMS.WMS.Services.PackingTask;

namespace ModernWMS.WMS.Services.DispatchWorkflow;

public partial class DispatchWorkflowService
{
    public async Task<List<WeighingBoxViewModel>> GetTaskBoxesAsync(int orderId,int taskId,CurrentUser user,CancellationToken ct=default)
    {
        if(orderId<=0||taskId<=0)throw new ArgumentException("order id and packing task id are required");
        await using var c=await _connectionFactory.OpenConnectionAsync(ct);
        var order=await c.QuerySingleOrDefaultAsync<DispatchOrderEntity>(new CommandDefinition("SELECT * FROM `wms_dispatch_order` WHERE `id`=@orderId;",new{orderId},cancellationToken:ct))??throw new KeyNotFoundException($"dispatch order not found: {orderId}");
        await _warehouseAccessService.EnsureAllowedAsync(order.warehouse_id,user);
        if(!await c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM `wms_dispatch_packing_task` WHERE `id`=@taskId AND `dispatch_order_id`=@orderId AND `is_active`=1);",new{taskId,orderId},cancellationToken:ct)))throw new KeyNotFoundException($"packing task not found in dispatch order: {taskId}");
        var boxes=(await c.QueryAsync<WeighingBoxViewModel>(new CommandDefinition("""
            SELECT `id`,`packing_task_id`,`source_box_identity`,`box_sequence`,`weight`,`length`,`width`,`height`,`measurement_status`,`copied_from_box_id`,`row_version`
            FROM `wms_weighing_box` WHERE `packing_task_id`=@taskId AND `is_invalidated`=0 ORDER BY `box_sequence`,`id`;
            """,new{taskId},cancellationToken:ct))).AsList();
        var boxIds=boxes.Select(x=>x.id).ToArray();
        if(boxIds.Length==0)return boxes;
        var boxItems=(await c.QueryAsync<WeighingBoxItemEntity>(new CommandDefinition("""
            SELECT `weighing_box_id`,`packing_task_item_id`,`task_qty`
            FROM `wms_weighing_box_item` WHERE `weighing_box_id` IN @boxIds ORDER BY `weighing_box_id`,`id`;
            """,new{boxIds},cancellationToken:ct))).AsList();
        foreach(var box in boxes)box.items=boxItems.Where(x=>x.weighing_box_id==box.id)
            .Select(x=>new PackingPlanBoxItemViewModel{packing_task_item_id=x.packing_task_item_id,task_qty=x.task_qty}).ToList();
        return boxes;
    }

    public Task<WeighingCommandResult> SaveWeighingBoxAsync(int orderId,int boxId,SaveWeighingBoxRequest r,CurrentUser u,CancellationToken ct=default)
    {
        if(boxId<=0||r.box_row_version<0||r.weight<=0||r.length<=0||r.width<=0||r.height<=0)throw new ArgumentException("box, row version and four positive measurements are required",nameof(r));
        return ExecuteWeighingMutationAsync(orderId,r.request_id,ScopedRequestId("SAVE_BOX",boxId.ToString(),r.request_id),r.row_version,DispatchWorkflowOperation.SaveWeighing,[DispatchOrderStatus.Weighing],u,(o,now,_)=>
        {var b=FindAvailableBox(o,boxId);if(b.row_version!=r.box_row_version)throw DispatchWorkflowCommandException.ConcurrencyConflict();ApplyMeasurement(b,r.weight,r.length,r.width,r.height,null,u,now);UpdateTaskMeasuredCount(o,b.packing_task_id,now);return Task.CompletedTask;},ct);
    }

    public Task<WeighingCommandResult> CopyWeighingBoxAsync(int orderId,int targetId,CopyWeighingBoxRequest r,CurrentUser u,CancellationToken ct=default)
    {
        if(r.source_box_id<=0||targetId<=0||r.source_box_id==targetId||r.target_box_row_version<0)throw new ArgumentException("different existing source and target boxes are required",nameof(r));
        return ExecuteWeighingMutationAsync(orderId,r.request_id,ScopedRequestId("COPY_BOX",$"{r.source_box_id}:{targetId}",r.request_id),r.row_version,DispatchWorkflowOperation.CopyWeighing,[DispatchOrderStatus.Weighing],u,(o,now,_)=>
        {var s=FindAvailableBox(o,r.source_box_id);var t=FindAvailableBox(o,targetId);if(s.packing_task_id!=t.packing_task_id)throw DispatchWorkflowCommandException.BoxNotAvailable("measurements may only be copied inside one packing task");if(t.row_version!=r.target_box_row_version)throw DispatchWorkflowCommandException.ConcurrencyConflict();if(!HasCompleteMeasurement(s))throw DispatchWorkflowCommandException.WeighingIncomplete("source box has no complete WMS measurement");ApplyMeasurement(t,s.weight!.Value,s.length!.Value,s.width!.Value,s.height!.Value,s.id,u,now);UpdateTaskMeasuredCount(o,t.packing_task_id,now);return Task.CompletedTask;},ct);
    }

    public Task<WeighingCommandResult> CompleteTaskWeighingAsync(int orderId,int taskId,WeighingOrderCommandRequest r,CurrentUser u,CancellationToken ct=default)
    {
        if(taskId<=0)throw new ArgumentException("packing task id is required",nameof(taskId));
        return ExecuteWeighingMutationAsync(orderId,r.request_id,ScopedRequestId("COMPLETE_TASK_WEIGHING",taskId.ToString(),r.request_id),r.row_version,DispatchWorkflowOperation.CompleteTaskWeighing,[DispatchOrderStatus.Weighing],u,(o,now,_)=>
        {var t=o.packing_tasks.SingleOrDefault(x=>x.id==taskId&&x.is_active)??throw new KeyNotFoundException($"packing task not found in dispatch order: {taskId}");ValidateCompletedPackingTask(t);var boxes=t.boxes.Where(x=>!x.is_invalidated).ToList();t.measured_box_count=boxes.Count;t.expected_box_count=boxes.Count;t.packing_plan_status="COMPLETED";t.status=DispatchOrderStatus.PendingOutbound;t.last_update_time=now;t.row_version++;return Task.CompletedTask;},ct);
    }

    public Task<WeighingCommandResult> CompleteOrderWeighingAsync(int orderId,WeighingOrderCommandRequest r,CurrentUser u,CancellationToken ct=default)=>
        ExecuteWeighingMutationAsync(orderId,r.request_id,r.request_id,r.row_version,DispatchWorkflowOperation.CompleteWeighing,[DispatchOrderStatus.Weighing],u,(o,_,_)=>
        {var tasks=o.packing_tasks.Where(x=>x.is_active).ToList();if(tasks.Count==0||tasks.Any(t=>t.status!=DispatchOrderStatus.PendingOutbound||t.boxes.Count(x=>!x.is_invalidated)==0||t.boxes.Count(x=>!x.is_invalidated)!=t.expected_box_count||t.boxes.Where(x=>!x.is_invalidated).Any(x=>!HasCompleteMeasurement(x))))throw DispatchWorkflowCommandException.WeighingIncomplete("every active packing task must finish all box measurements");o.status=DispatchOrderStatus.PendingOutbound;return Task.CompletedTask;},ct);

    public async Task<WeighingCommandResult> StartWeighingAsync(int orderId,WeighingOrderCommandRequest r,CurrentUser u,CancellationToken ct=default)
    {
        ValidateOrderCommand(orderId,r.request_id,r.row_version);
        await using var c=await _connectionFactory.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        PostPickSourceGuardResult guard;
        try{guard=await EnsurePostPickSourceCurrentAsync(c,tx,orderId,u,ct);}
        catch{await tx.RollbackAsync(CancellationToken.None);throw;}
        if(guard.source_change_pending){await tx.CommitAsync(ct);throw DispatchWorkflowCommandException.SourceChangePending();}
        try
        {
            var previous=await FindOperationAsync(c,tx,orderId,DispatchWorkflowOperation.StartWeighing,r.request_id,ct);if(previous!=null){await tx.CommitAsync(ct);return WeighingResultFromLedger(previous,r.request_id);}
            var order=await LoadWeighOrderAsync(c,tx,orderId,ct);await _warehouseAccessService.EnsureAllowedAsync(order.warehouse_id,u);
            if(order.status!=DispatchOrderStatus.Picked)throw DispatchWorkflowCommandException.StatusNotAllowedForWeighing();if(order.row_version!=r.row_version)throw DispatchWorkflowCommandException.ConcurrencyConflict();
            var now=DateTime.Now;
            foreach(var task in order.packing_tasks.Where(x=>x.is_active).OrderBy(x=>x.id))
            {
                var changed=await c.ExecuteAsync(new CommandDefinition("""
                    UPDATE `wms_dispatch_packing_task` SET `status`=@status,`expected_box_count`=@count,`measured_box_count`=0,
                      `packing_plan_status`='DRAFT',`stable_box_identity_verified`=1,`box_identity_validation_error`='',`last_update_time`=@now,`row_version`=`row_version`+1
                    WHERE `id`=@id AND `row_version`=@expected;
                    """,new{status=DispatchOrderStatus.Weighing,count=0,now,id=task.id,expected=task.row_version},tx,cancellationToken:ct));
                if(changed!=1)throw DispatchWorkflowCommandException.ConcurrencyConflict();
            }
            var version=order.row_version+1;await UpdateWeighOrderAsync(c,tx,orderId,r.row_version,DispatchOrderStatus.Weighing,now,ct);await InsertOperationAsync(c,tx,orderId,DispatchWorkflowOperation.StartWeighing,r.request_id,DispatchOrderStatus.Weighing,version,u,now,ct);await tx.CommitAsync(ct);
            return new(){order_id=orderId,request_id=r.request_id,status=ToApiStatus(DispatchOrderStatus.Weighing),row_version=version};
        }
        catch(Exception ex)when(IsDatabaseConcurrency(ex)){await tx.RollbackAsync(CancellationToken.None);var winner=await FindOperationAsync(c,null,orderId,DispatchWorkflowOperation.StartWeighing,r.request_id,CancellationToken.None);if(winner!=null)return WeighingResultFromLedger(winner,r.request_id);throw DispatchWorkflowCommandException.ConcurrencyConflict();}
        catch{await tx.RollbackAsync(CancellationToken.None);throw;}
    }

    private async Task<WeighingCommandResult> ExecuteWeighingMutationAsync(int orderId,string clientId,string ledgerId,long version,DispatchWorkflowOperation op,IReadOnlyCollection<DispatchOrderStatus> statuses,CurrentUser u,Func<DispatchOrderEntity,DateTime,CancellationToken,Task> mutation,CancellationToken ct)
    {
        ValidateOrderCommand(orderId,clientId,version);
        await using var c=await _connectionFactory.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        PostPickSourceGuardResult guard;
        try{guard=await EnsurePostPickSourceCurrentAsync(c,tx,orderId,u,ct);}
        catch{await tx.RollbackAsync(CancellationToken.None);throw;}
        if(guard.source_change_pending){await tx.CommitAsync(ct);throw DispatchWorkflowCommandException.SourceChangePending();}
        try
        {
            var previous=await FindOperationAsync(c,tx,orderId,op,ledgerId,ct);if(previous!=null){await tx.CommitAsync(ct);return WeighingResultFromLedger(previous,clientId);}
            var order=await LoadWeighOrderAsync(c,tx,orderId,ct);await _warehouseAccessService.EnsureAllowedAsync(order.warehouse_id,u);if(!statuses.Contains(order.status))throw DispatchWorkflowCommandException.StatusNotAllowedForWeighing();if(order.row_version!=version)throw DispatchWorkflowCommandException.ConcurrencyConflict();
            var taskVersions=order.packing_tasks.ToDictionary(x=>x.id,x=>x.row_version);var boxVersions=order.packing_tasks.SelectMany(x=>x.boxes).ToDictionary(x=>x.id,x=>x.row_version);
            var now=DateTime.Now;await mutation(order,now,ct);await PersistWeighAggregateAsync(c,tx,order,version,taskVersions,boxVersions,now,ct);var next=version+1;await InsertOperationAsync(c,tx,orderId,op,ledgerId,order.status,next,u,now,ct);await tx.CommitAsync(ct);return new(){order_id=orderId,request_id=clientId,status=ToApiStatus(order.status),row_version=next};
        }
        catch(Exception ex)when(IsDatabaseConcurrency(ex)){await tx.RollbackAsync(CancellationToken.None);var winner=await FindOperationAsync(c,null,orderId,op,ledgerId,CancellationToken.None);if(winner!=null)return WeighingResultFromLedger(winner,clientId);throw DispatchWorkflowCommandException.ConcurrencyConflict();}
        catch{await tx.RollbackAsync(CancellationToken.None);throw;}
    }

    private static async Task<DispatchOrderEntity> LoadWeighOrderAsync(System.Data.IDbConnection c,IDbTransaction tx,int id,CancellationToken ct)
    {using var r=await c.QueryMultipleAsync(new CommandDefinition("SELECT * FROM `wms_dispatch_order` WHERE `id`=@id FOR UPDATE;SELECT * FROM `wms_dispatch_packing_task` WHERE `dispatch_order_id`=@id AND `is_active`=1 FOR UPDATE;SELECT i.* FROM `wms_dispatch_packing_task_item` i JOIN `wms_dispatch_packing_task` t ON t.`id`=i.`packing_task_id` WHERE t.`dispatch_order_id`=@id AND i.`is_active`=1 FOR UPDATE;SELECT b.* FROM `wms_weighing_box` b JOIN `wms_dispatch_packing_task` t ON t.`id`=b.`packing_task_id` WHERE t.`dispatch_order_id`=@id FOR UPDATE;SELECT bi.* FROM `wms_weighing_box_item` bi JOIN `wms_weighing_box` b ON b.`id`=bi.`weighing_box_id` JOIN `wms_dispatch_packing_task` t ON t.`id`=b.`packing_task_id` WHERE t.`dispatch_order_id`=@id FOR UPDATE;",new{id},tx,cancellationToken:ct));var o=await r.ReadSingleOrDefaultAsync<DispatchOrderEntity>()??throw new KeyNotFoundException($"dispatch order not found: {id}");o.packing_tasks=(await r.ReadAsync<DispatchPackingTaskEntity>()).AsList();var items=(await r.ReadAsync<DispatchPackingTaskItemEntity>()).AsList();var boxes=(await r.ReadAsync<WeighingBoxEntity>()).AsList();var boxItems=(await r.ReadAsync<WeighingBoxItemEntity>()).AsList();foreach(var t in o.packing_tasks){t.items=items.Where(x=>x.packing_task_id==t.id).ToList();t.boxes=boxes.Where(x=>x.packing_task_id==t.id).ToList();foreach(var b in t.boxes)b.items=boxItems.Where(x=>x.weighing_box_id==b.id).ToList();}return o;}

    private static async Task PersistWeighAggregateAsync(System.Data.IDbConnection c,IDbTransaction tx,DispatchOrderEntity o,long expected,
        IReadOnlyDictionary<int,long> taskVersions,IReadOnlyDictionary<int,long> boxVersions,DateTime now,CancellationToken ct)
    {
        foreach(var t in o.packing_tasks){var n=await c.ExecuteAsync(new CommandDefinition("UPDATE `wms_dispatch_packing_task` SET `status`=@status,`packing_plan_status`=@packing_plan_status,`expected_box_count`=@expected_box_count,`measured_box_count`=@measured_box_count,`last_update_time`=@last_update_time,`row_version`=@row_version WHERE `id`=@id AND `row_version`=@expected;",new{t.status,t.packing_plan_status,t.expected_box_count,t.measured_box_count,t.last_update_time,t.row_version,t.id,expected=taskVersions[t.id]},tx,cancellationToken:ct));if(n!=1)throw DispatchWorkflowCommandException.ConcurrencyConflict();}
        foreach(var b in o.packing_tasks.SelectMany(x=>x.boxes)){var n=await c.ExecuteAsync(new CommandDefinition("""
            UPDATE `wms_weighing_box` SET `weight`=@weight,`length`=@length,`width`=@width,`height`=@height,`measurement_status`=@measurement_status,
              `measured_by`=@measured_by,`measured_by_name`=@measured_by_name,`measured_at`=@measured_at,`copied_from_box_id`=@copied_from_box_id,
              `last_update_time`=@last_update_time,`row_version`=@row_version WHERE `id`=@id AND `row_version`=@expected;
            """,new{b.weight,b.length,b.width,b.height,b.measurement_status,b.measured_by,b.measured_by_name,b.measured_at,b.copied_from_box_id,b.last_update_time,b.row_version,b.id,expected=boxVersions[b.id]},tx,cancellationToken:ct));if(n!=1)throw DispatchWorkflowCommandException.ConcurrencyConflict();}
        if(o.status==DispatchOrderStatus.PendingOutbound)
        {
            var valid=await c.ExecuteScalarAsync<bool>(new CommandDefinition("""
                SELECT EXISTS(SELECT 1 FROM `wms_dispatchlist` WHERE `dispatch_order_id`=@orderId)
                  AND NOT EXISTS(SELECT 1 FROM `wms_dispatchlist` WHERE `dispatch_order_id`=@orderId AND `dispatch_status` NOT IN (3,4,5));
                """,new{orderId=o.id},tx,cancellationToken:ct));
            if(!valid)throw DispatchWorkflowCommandException.StockConflict("dispatch detail status is not ready for pending outbound");
            await c.ExecuteAsync(new CommandDefinition("""
                UPDATE `wms_dispatchlist` SET `dispatch_status`=5,`last_update_time`=@now
                WHERE `dispatch_order_id`=@orderId AND `dispatch_status` IN (3,4,5);
                """,new{orderId=o.id,now},tx,cancellationToken:ct));
        }
        await UpdateWeighOrderAsync(c,tx,o.id,expected,o.status,now,ct);
    }
    private static async Task UpdateWeighOrderAsync(System.Data.IDbConnection c,IDbTransaction tx,int id,long expected,DispatchOrderStatus status,DateTime now,CancellationToken ct){var n=await c.ExecuteAsync(new CommandDefinition("UPDATE `wms_dispatch_order` SET `status`=@status,`last_update_time`=@now,`row_version`=`row_version`+1 WHERE `id`=@id AND `row_version`=@expected;",new{id,expected,status,now},tx,cancellationToken:ct));if(n!=1)throw DispatchWorkflowCommandException.ConcurrencyConflict();}

    private static IReadOnlyList<SellFoxSourceBox> ResolveWeighingBoxes(DispatchPackingTaskEntity task)
    {
        var parsed=SellFoxCartonParser.Parse(task.source_cartons_json,allowEmpty:true);
        if(!parsed.IsSupported)throw DispatchWorkflowCommandException.SourceBoxIdentityUnsupported($"packing task {task.source_task_no}: {parsed.Error}");
        if(parsed.Boxes.Count>0)return parsed.Boxes;

        // The packing-task list can legitimately have no SellFox/FBA carton payload.
        // In that case the WMS workflow owns one initial weighing box whose identity
        // is derived from the immutable packing-task id, so retrying is idempotent.
        var identity=$"PACKING_TASK:{task.source_task_id}";
        var snapshot=JsonSerializer.Serialize(new
        {
            identityOrigin="WMS_PACKING_TASK",
            sourceTaskId=task.source_task_id,
            sourceTaskNo=task.source_task_no,
            sequence=1
        });
        return [new SellFoxSourceBox(identity,1,snapshot)];
    }

    private static void ValidateOrderCommand(int id,string requestId,long version){if(id<=0||string.IsNullOrWhiteSpace(requestId)||requestId.Length>64||requestId!=requestId.Trim()||version<0)throw new ArgumentException("order id, request_id and row_version are required");}
    private static WeighingBoxEntity FindAvailableBox(DispatchOrderEntity o,int id)=>o.packing_tasks.Where(x=>x.is_active).SelectMany(x=>x.boxes).SingleOrDefault(x=>x.id==id&&!x.is_invalidated)??throw DispatchWorkflowCommandException.BoxNotAvailable("box does not belong to the active packing tasks of this order");
    private static bool HasCompleteMeasurement(WeighingBoxEntity b)=>b.measurement_status=="MEASURED"&&b.weight>0&&b.length>0&&b.width>0&&b.height>0;
    private static void ValidateCompletedPackingTask(DispatchPackingTaskEntity t){var boxes=t.boxes.Where(x=>!x.is_invalidated).ToList();if(boxes.Count==0||boxes.Any(x=>!HasCompleteMeasurement(x)||x.items.Count==0))throw DispatchWorkflowCommandException.WeighingIncomplete("每个箱必须有商品且重量和箱规完整");foreach(var item in t.items){var packed=boxes.SelectMany(x=>x.items).Where(x=>x.packing_task_item_id==item.id).Sum(x=>x.task_qty);var expected=item.actual_packed_task_qty??item.source_quantity_shipped;if(expected is null or <=0||packed!=expected)throw DispatchWorkflowCommandException.WeighingIncomplete($"商品 {item.commodity_sku} 尚未分配完成");}}
    private static void ApplyMeasurement(WeighingBoxEntity b,decimal weight,decimal length,decimal width,decimal height,int? copied,CurrentUser u,DateTime now){b.weight=weight;b.length=length;b.width=width;b.height=height;b.measurement_status="MEASURED";b.measured_by=u.user_id;b.measured_by_name=u.user_name;b.measured_at=now;b.copied_from_box_id=copied;b.last_update_time=now;b.row_version++;}
    private static void UpdateTaskMeasuredCount(DispatchOrderEntity o,int id,DateTime now){var t=o.packing_tasks.Single(x=>x.id==id);t.measured_box_count=t.boxes.Count(x=>!x.is_invalidated&&HasCompleteMeasurement(x));t.last_update_time=now;t.row_version++;}
    private static WeighingCommandResult WeighingResultFromLedger(DispatchWorkflowOperationEntity x,string clientId){if(x.result_order_status==null||x.result_row_version==null)throw DispatchWorkflowCommandException.ConcurrencyConflict();return new(){order_id=x.dispatch_order_id,request_id=clientId,status=ToApiStatus(x.result_order_status.Value),row_version=x.result_row_version.Value};}
    private static string ScopedRequestId(string kind,string resource,string client){if(string.IsNullOrWhiteSpace(client)||client.Length>64||client!=client.Trim())throw new ArgumentException("request_id must be non-empty, canonical and at most 64 characters");return HashText($"{kind}|{resource}|{client}");}
}

public sealed partial class DispatchWorkflowCommandException
{
    public static DispatchWorkflowCommandException StatusNotAllowedForWeighing()=>new("STATUS_NOT_ALLOWED","weighing command is not allowed for the current order status");
    public static DispatchWorkflowCommandException SourceBoxIdentityUnsupported(string detail)=>new("SOURCE_BOX_ID_UNSUPPORTED",detail);
    public static DispatchWorkflowCommandException BoxNotAvailable(string detail)=>new("BOX_NOT_AVAILABLE",detail);
    public static DispatchWorkflowCommandException WeighingIncomplete(string detail)=>new("WEIGHING_INCOMPLETE",detail);
}
