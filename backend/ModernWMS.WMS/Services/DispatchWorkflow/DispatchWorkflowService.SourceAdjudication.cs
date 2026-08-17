using System.Data;
using Dapper;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Entities.Models;
using ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;
using MySqlConnector;

namespace ModernWMS.WMS.Services.DispatchWorkflow;

public partial class DispatchWorkflowService
{
    public async Task<PostPickSourceGuardResult> EnsurePostPickSourceCurrentAsync(int orderId,CurrentUser user,CancellationToken ct=default)
    {
        if(orderId<=0)throw new ArgumentException("dispatch order id is required",nameof(orderId));
        await using var c=await _connectionFactory.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        try
        {
            var result=await EnsurePostPickSourceCurrentAsync(c,tx,orderId,user,ct);
            await tx.CommitAsync(ct);return result;
        }
        catch(Exception ex) when(IsDatabaseConcurrency(ex))
        {await tx.RollbackAsync(CancellationToken.None);var winner=await c.QuerySingleOrDefaultAsync<DispatchOrderEntity>("SELECT * FROM `wms_dispatch_order` WHERE `id`=@orderId;",new{orderId});if(winner?.source_change_pending==true)return GuardPending(winner,winner.pending_source_version);throw DispatchWorkflowCommandException.ConcurrencyConflict();}
        catch{await tx.RollbackAsync(CancellationToken.None);throw;}
    }

    internal async Task<PostPickSourceGuardResult> EnsurePostPickSourceCurrentAsync(MySqlConnection c,MySqlTransaction tx,int orderId,CurrentUser user,CancellationToken ct)
    {
        if(orderId<=0)throw new ArgumentException("dispatch order id is required",nameof(orderId));
        var order=await c.QuerySingleOrDefaultAsync<DispatchOrderEntity>(new CommandDefinition("SELECT * FROM `wms_dispatch_order` WHERE `id`=@orderId FOR UPDATE;",new{orderId},tx,cancellationToken:ct))??throw new KeyNotFoundException($"dispatch order not found: {orderId}");
        await _warehouseAccessService.EnsureAllowedAsync(order.warehouse_id,user);EnsureGuardableStatus(order.status);
        var taskIds=(await c.QueryAsync<long>(new CommandDefinition("SELECT `source_task_id` FROM `wms_dispatch_packing_task` WHERE `dispatch_order_id`=@orderId AND `is_active`=1 ORDER BY `source_task_id`;",new{orderId},tx,cancellationToken:ct))).AsList();
        var snapshots=await _sourceReader.ReadAsync(taskIds,ct);var version=CombinedVersion(snapshots);var snapshot=SnapshotJson(snapshots);var diff=SourceDiffJson(order.source_snapshot,snapshot);
        var accepted=await c.ExecuteScalarAsync<bool>(new CommandDefinition("""
            SELECT EXISTS(SELECT 1 FROM `wms_dispatch_source_change_event` WHERE `dispatch_order_id`=@orderId AND `source_version`=@version AND `decision`=@decision);
            """,new{orderId,version,decision=DispatchSourceChangeDecision.ContinueShipment},tx,cancellationToken:ct));
        var current=order.source_version==version||order.accepted_source_version==version||accepted;
        if(current)
        {
            if(order.source_change_pending||order.pending_source_version.Length>0||order.source_change_snapshot.Length>0)
            {var n=await c.ExecuteAsync(new CommandDefinition("""
                UPDATE `wms_dispatch_order` SET `source_change_pending`=0,`pending_source_version`='',`source_change_snapshot`='',
                  `last_update_time`=@now,`row_version`=`row_version`+1 WHERE `id`=@orderId AND `row_version`=@rowVersion;
                """,new{now=DateTime.Now,orderId,rowVersion=order.row_version},tx,cancellationToken:ct));if(n!=1)throw DispatchWorkflowCommandException.ConcurrencyConflict();order.row_version++;}
            return GuardPassed(order,version);
        }
        if(order.status==DispatchOrderStatus.Outbound)
        {
            await InsertSourceEventIfMissingAsync(c,tx,CreateSourceEvent(order,version,diff,DispatchSourceChangeDecision.OutboundAnomaly,user,"source changed after outbound",DateTime.Now),ct);
            return GuardPassed(order,version);
        }
        var now=DateTime.Now;
        await InsertSourceEventIfMissingAsync(c,tx,CreateSourceEvent(order,version,diff,DispatchSourceChangeDecision.Detected,user,"source change detected; awaiting a human decision",now),ct);
        var changed=!order.source_change_pending||order.pending_source_version!=version||order.source_change_snapshot!=diff;
        if(changed)
        {var n=await c.ExecuteAsync(new CommandDefinition("""
            UPDATE `wms_dispatch_order` SET `source_change_pending`=1,`pending_source_version`=@version,`source_change_snapshot`=@diff,
              `last_update_time`=@now,`row_version`=`row_version`+1 WHERE `id`=@orderId AND `row_version`=@rowVersion;
            """,new{version,diff,now,orderId,rowVersion=order.row_version},tx,cancellationToken:ct));if(n!=1)throw DispatchWorkflowCommandException.ConcurrencyConflict();order.row_version++;}
        return GuardPending(order,version);
    }

    public async Task<SourceDecisionResult> DecideSourceChangeAsync(int orderId,SourceDecisionRequest request,CurrentUser user,CancellationToken ct=default)
    {
        var decision=ParseDecision(request);ValidateDecisionRequest(orderId,request);var requestId=request.request_id.Trim();var sourceVersion=request.source_version.Trim();var reason=request.reason.Trim();
        var operation=decision==DispatchSourceChangeDecision.ContinueShipment?DispatchWorkflowOperation.ContinueAfterSourceChange:DispatchWorkflowOperation.CancelAfterSourceChange;
        await using var c=await _connectionFactory.OpenConnectionAsync(ct);await using var tx=await c.BeginTransactionAsync(IsolationLevel.Serializable,ct);
        try
        {
            var previous=await c.QuerySingleOrDefaultAsync<DispatchWorkflowOperationEntity>(new CommandDefinition("""
                SELECT * FROM `wms_dispatch_workflow_operation` WHERE `dispatch_order_id`=@orderId AND `request_id`=@requestId
                  AND `operation` IN (@continueOp,@cancelOp) LIMIT 1;
                """,new{orderId,requestId,continueOp=DispatchWorkflowOperation.ContinueAfterSourceChange,cancelOp=DispatchWorkflowOperation.CancelAfterSourceChange},tx,cancellationToken:ct));
            if(previous?.result_status==DispatchWorkflowOperationResultStatus.Succeeded)
            {if(previous.operation!=operation)throw DispatchWorkflowCommandException.IdempotencyConflict();var eventVersion=await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition("SELECT `source_version` FROM `wms_dispatch_source_change_event` WHERE `event_idempotency_key`=@key LIMIT 1;",new{key=DecisionEventKey(orderId,operation,requestId)},tx,cancellationToken:ct));await tx.CommitAsync(ct);return DecisionFromLedger(previous,request.decision,eventVersion??sourceVersion);}
            var order=await c.QuerySingleOrDefaultAsync<DispatchOrderEntity>(new CommandDefinition("SELECT * FROM `wms_dispatch_order` WHERE `id`=@orderId FOR UPDATE;",new{orderId},tx,cancellationToken:ct))??throw new KeyNotFoundException($"dispatch order not found: {orderId}");
            await _warehouseAccessService.EnsureAllowedAsync(order.warehouse_id,user);
            if(order.status is DispatchOrderStatus.Outbound or DispatchOrderStatus.PendingPick or DispatchOrderStatus.SourceCancelled or DispatchOrderStatus.ManualCancelled)throw DispatchWorkflowCommandException.StatusNotAllowedForSourceDecision();
            if(!order.source_change_pending)throw DispatchWorkflowCommandException.SourceDecisionNotPending();
            if(order.pending_source_version!=sourceVersion)throw DispatchWorkflowCommandException.SourceVersionConflict();
            if(order.row_version!=request.row_version)throw DispatchWorkflowCommandException.ConcurrencyConflict();
            var tasks=(await c.QueryAsync<DispatchPackingTaskEntity>(new CommandDefinition("SELECT * FROM `wms_dispatch_packing_task` WHERE `dispatch_order_id`=@orderId;",new{orderId},tx,cancellationToken:ct))).AsList();order.packing_tasks=tasks;
            var snapshots=await _sourceReader.ReadAsync(tasks.Where(x=>x.is_active).Select(x=>x.source_task_id).ToArray(),ct);
            if(sourceVersion!=CombinedVersion(snapshots))throw DispatchWorkflowCommandException.SourceVersionConflict();
            var detected=await c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM `wms_dispatch_source_change_event` WHERE `dispatch_order_id`=@orderId AND `source_version`=@sourceVersion AND `decision`=@d);",new{orderId,sourceVersion,d=DispatchSourceChangeDecision.Detected},tx,cancellationToken:ct));
            if(!detected)throw DispatchWorkflowCommandException.SourceVersionConflict();var now=DateTime.Now;
            if(decision==DispatchSourceChangeDecision.CancelShipment){await CancelAfterSourceChangeAsync(c,tx,order,now,ct);order.status=DispatchOrderStatus.ManualCancelled;}
            else order.accepted_source_version=sourceVersion;
            var next=order.row_version+1;
            var n=await c.ExecuteAsync(new CommandDefinition("""
                UPDATE `wms_dispatch_order` SET `status`=@status,`accepted_source_version`=@accepted,`source_change_pending`=0,
                  `adjudicated_source_version`=@sourceVersion,`adjudicated_by`=@userId,`adjudicated_by_name`=@name,`adjudicated_at`=@now,
                  `adjudication_reason`=@reason,`last_update_time`=@now,`row_version`=`row_version`+1,`pending_source_version`=''
                WHERE `id`=@orderId AND `row_version`=@expected;
                """,new{status=order.status,accepted=order.accepted_source_version,sourceVersion,userId=user.user_id,name=user.user_name,now,reason,orderId,expected=order.row_version},tx,cancellationToken:ct));if(n!=1)throw DispatchWorkflowCommandException.ConcurrencyConflict();
            await InsertSourceEventAsync(c,tx,CreateSourceEvent(order,sourceVersion,order.source_change_snapshot,decision,user,reason,now,DecisionEventKey(orderId,operation,requestId)),ct);
            await InsertOperationAsync(c,tx,orderId,operation,requestId,order.status,next,user,now,ct);await tx.CommitAsync(ct);
            order.row_version=next;order.source_change_pending=false;return ToDecisionResult(order,requestId,request.decision,sourceVersion);
        }
        catch(Exception ex) when(IsDatabaseConcurrency(ex))
        {await tx.RollbackAsync(CancellationToken.None);var winner=await FindOperationAsync(c,null,orderId,operation,requestId,CancellationToken.None);if(winner?.result_status==DispatchWorkflowOperationResultStatus.Succeeded){var v=await c.QuerySingleOrDefaultAsync<string>("SELECT `source_version` FROM `wms_dispatch_source_change_event` WHERE `event_idempotency_key`=@key LIMIT 1;",new{key=DecisionEventKey(orderId,operation,requestId)});return DecisionFromLedger(winner,request.decision,v??sourceVersion);}throw DispatchWorkflowCommandException.ConcurrencyConflict();}
        catch{await tx.RollbackAsync(CancellationToken.None);throw;}
    }

    private static async Task CancelAfterSourceChangeAsync(System.Data.IDbConnection c,IDbTransaction tx,DispatchOrderEntity order,DateTime now,CancellationToken ct)
    {
        if(await c.ExecuteScalarAsync<bool>(new CommandDefinition("SELECT EXISTS(SELECT 1 FROM `wms_dispatchpicklist` p JOIN `wms_dispatchlist` d ON d.`id`=p.`dispatchlist_id` WHERE d.`dispatch_order_id`=@id AND p.`is_update_stock`=1);",new{id=order.id},tx,cancellationToken:ct)))throw DispatchWorkflowCommandException.StockAlreadyDeducted();
        await c.ExecuteAsync(new CommandDefinition("""
            DELETE p FROM `wms_dispatchpicklist` p JOIN `wms_dispatchlist` d ON d.`id`=p.`dispatchlist_id` WHERE d.`dispatch_order_id`=@id;
            UPDATE `wms_dispatchlist` SET `dispatch_status`=0,`last_update_time`=@now WHERE `dispatch_order_id`=@id;
            UPDATE `wms_weighing_box` b JOIN `wms_dispatch_packing_task` t ON t.`id`=b.`packing_task_id`
              SET b.`is_invalidated`=1,b.`invalidated_at`=@now,b.`last_update_time`=@now,b.`row_version`=b.`row_version`+1
              WHERE t.`dispatch_order_id`=@id AND b.`is_invalidated`=0;
            UPDATE `wms_dispatch_packing_task` SET `status`=@status,`last_update_time`=@now,`row_version`=`row_version`+1 WHERE `dispatch_order_id`=@id;
            """,new{id=order.id,now,status=DispatchOrderStatus.ManualCancelled},tx,cancellationToken:ct));
    }

    private static async Task InsertSourceEventIfMissingAsync(System.Data.IDbConnection c,IDbTransaction tx,DispatchSourceChangeEventEntity e,CancellationToken ct)=>
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT IGNORE INTO `wms_dispatch_source_change_event` (`dispatch_order_id`,`source_version`,`event_idempotency_key`,`decision`,`operator_id`,`operator_name`,`decision_time`,`reason`,`diff_snapshot`)
            VALUES (@dispatch_order_id,@source_version,@event_idempotency_key,@decision,@operator_id,@operator_name,@decision_time,@reason,@diff_snapshot);
            """,e,tx,cancellationToken:ct));

    private static async Task InsertSourceEventAsync(System.Data.IDbConnection c,IDbTransaction tx,DispatchSourceChangeEventEntity e,CancellationToken ct)=>
        await c.ExecuteAsync(new CommandDefinition("""
            INSERT INTO `wms_dispatch_source_change_event` (`dispatch_order_id`,`source_version`,`event_idempotency_key`,`decision`,`operator_id`,`operator_name`,`decision_time`,`reason`,`diff_snapshot`)
            VALUES (@dispatch_order_id,@source_version,@event_idempotency_key,@decision,@operator_id,@operator_name,@decision_time,@reason,@diff_snapshot);
            """,e,tx,cancellationToken:ct));

    private static DispatchSourceChangeDecision ParseDecision(SourceDecisionRequest r)=>r.decision.Trim().ToUpperInvariant() switch{"CONTINUE"=>DispatchSourceChangeDecision.ContinueShipment,"CANCEL"=>DispatchSourceChangeDecision.CancelShipment,_=>throw new ArgumentException("decision must be CONTINUE or CANCEL",nameof(r))};
    private static void ValidateDecisionRequest(int id,SourceDecisionRequest r){if(id<=0||string.IsNullOrWhiteSpace(r.decision)||string.IsNullOrWhiteSpace(r.source_version)||r.source_version.Trim().Length>64||string.IsNullOrWhiteSpace(r.reason)||r.reason.Trim().Length>500||string.IsNullOrWhiteSpace(r.request_id)||r.request_id.Trim().Length>64||r.row_version<0)throw new ArgumentException("decision, source_version, reason, request_id and row_version are required",nameof(r));}
    private static void EnsureGuardableStatus(DispatchOrderStatus s){if(s is not(DispatchOrderStatus.Picked or DispatchOrderStatus.Weighing or DispatchOrderStatus.PendingOutbound or DispatchOrderStatus.Outbound))throw DispatchWorkflowCommandException.StatusNotAllowedForSourceGuard();}
    private static DispatchSourceChangeEventEntity CreateSourceEvent(DispatchOrderEntity o,string version,string diff,DispatchSourceChangeDecision d,CurrentUser u,string reason,DateTime now,string? key=null)=>new(){dispatch_order_id=o.id,source_version=version,event_idempotency_key=key??HashText($"{o.id}|{version}|{(byte)d}"),decision=d,operator_id=u.user_id,operator_name=u.user_name,decision_time=now,reason=reason,diff_snapshot=diff};
    private static string DecisionEventKey(int id,DispatchWorkflowOperation op,string requestId)=>HashText($"{id}|{(byte)op}|{requestId}");
    private static string SourceDiffJson(string accepted,string current)=>System.Text.Json.JsonSerializer.Serialize(new{wms_snapshot=accepted,current_source_snapshot=current});
    private static PostPickSourceGuardResult GuardPassed(DispatchOrderEntity o,string v)=>new(){source_change_pending=false,source_version=v,row_version=o.row_version};
    private static PostPickSourceGuardResult GuardPending(DispatchOrderEntity o,string v)=>new(){source_change_pending=true,error_code="SOURCE_CHANGE_PENDING",source_version=v,row_version=o.row_version};
    private static SourceDecisionResult ToDecisionResult(DispatchOrderEntity o,string requestId,string decision,string version)=>new(){order_id=o.id,request_id=requestId,decision=decision.Trim().ToUpperInvariant(),source_version=version,status=ToApiStatus(o.status),source_change_pending=o.source_change_pending,row_version=o.row_version};
    private static SourceDecisionResult DecisionFromLedger(DispatchWorkflowOperationEntity x,string decision,string version){if(x.result_order_status==null||x.result_row_version==null)throw DispatchWorkflowCommandException.ConcurrencyConflict();return new(){order_id=x.dispatch_order_id,request_id=x.request_id,decision=decision.Trim().ToUpperInvariant(),source_version=version,status=ToApiStatus(x.result_order_status.Value),source_change_pending=false,row_version=x.result_row_version.Value};}
}

public sealed partial class DispatchWorkflowCommandException
{
    public static DispatchWorkflowCommandException SourceChangePending()=>new("SOURCE_CHANGE_PENDING","source changed after picking and requires a human decision");
    public static DispatchWorkflowCommandException SourceVersionConflict()=>new("SOURCE_VERSION_CONFLICT","source version is not the current pending version");
    public static DispatchWorkflowCommandException SourceDecisionNotPending()=>new("SOURCE_DECISION_NOT_PENDING","the order has no pending source change");
    public static DispatchWorkflowCommandException StockAlreadyDeducted()=>new("STOCK_ALREADY_DEDUCTED","inventory was already deducted and the order cannot be cancelled");
    public static DispatchWorkflowCommandException IdempotencyConflict()=>new("IDEMPOTENCY_CONFLICT","request_id was already used for the opposite source decision");
    public static DispatchWorkflowCommandException StatusNotAllowedForSourceGuard()=>new("STATUS_NOT_ALLOWED","source guard is only valid after picking");
    public static DispatchWorkflowCommandException StatusNotAllowedForSourceDecision()=>new("STATUS_NOT_ALLOWED","source decision is only valid before outbound");
}
