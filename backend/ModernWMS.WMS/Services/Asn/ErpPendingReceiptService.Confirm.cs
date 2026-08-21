using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModernWMS.Core.JWT;

namespace ModernWMS.WMS.Services;

/// <summary>
/// Purchase-task lifecycle refresh helpers used after the receipt is confirmed.
/// </summary>
public partial class ErpPendingReceiptService
{
    private const string PurchaseTaskSourceType = "PURCHASE_TASK";
    private const string StatusAllSigned = "ALL_SIGNED";
    private const string TaskTypeFdTransfer = "FD_TRANSFER";
    private const string CloseTypeManualClose = "MANUAL_CLOSE";
    private const string ManualCompleteEvent = "MANUAL_COMPLETE";
    private const string UnifiedReceiptConfirmedEvent = "UNIFIED_RECEIPT_CONFIRMED";
    private const string ShenzhenSelfWarehouseSigner = "深圳自建仓";

    private async Task<(bool changed, string fromAction, string toAction)> RefreshPurchaseTaskAsync(
        long taskId,
        CurrentUser currentUser,
        DateTime now)
    {
        var task = await ScalarTaskRowAsync(taskId);
        if (task == null)
        {
            return (false, string.Empty, string.Empty);
        }

        var facts = await BuildPurchaseTaskFactsAsync(task);
        var manualCompleted = (task.closed_time != null
            && string.Equals(task.close_type?.Trim(), CloseTypeManualClose, StringComparison.OrdinalIgnoreCase))
            || await ExistsManualCompleteEventAsync(taskId);
        var explicitlyCompleted = task.closed_time != null
            && (!string.IsNullOrWhiteSpace(task.close_type) || !string.IsNullOrWhiteSpace(task.closed_by));
        var toAction = ResolveNextAction(task.action, manualCompleted, explicitlyCompleted, facts);

        var changed = !string.Equals(task.action ?? string.Empty, toAction, StringComparison.Ordinal)
            || !string.Equals(task.production_action ?? string.Empty, facts.ProductionAction, StringComparison.Ordinal)
            || task.actual_produced_qty != facts.ActualProducedQty
            || task.fbm_qty != facts.FbmQty
            || task.fba_qty != facts.FbaQty
            || task.actual_shipped_qty != facts.ActualShippedQty
            || task.actual_signed_qty != facts.ActualSignedQty;
        if (changed)
        {
            await ExecuteAsync(
                """
                UPDATE erp_purchase_task
                   SET action=@action, production_action=@productionAction,
                       actual_produced_qty=@producedQty, fbm_qty=@fbmQty, fba_qty=@fbaQty,
                       actual_shipped_qty=@shippedQty, actual_signed_qty=@signedQty,
                       updater=@updater, update_time=@now
                 WHERE id=@taskId AND deleted=b'0'
                """,
                ("@action", toAction), ("@productionAction", facts.ProductionAction),
                ("@producedQty", facts.ActualProducedQty), ("@fbmQty", facts.FbmQty),
                ("@fbaQty", facts.FbaQty), ("@shippedQty", facts.ActualShippedQty),
                ("@signedQty", facts.ActualSignedQty), ("@updater", Truncate(currentUser.user_name, 64)),
                ("@now", now), ("@taskId", taskId));
        }
        return (changed, task.action ?? string.Empty, toAction);
    }

    private async Task WritePurchaseActionLogAsync(
        long taskId,
        string fromAction,
        string toAction,
        string remark,
        object payload,
        CurrentUser currentUser,
        DateTime now)
    {
        var rawJson = JsonSerializer.Serialize(new { taskId, action = toAction });
        await ExecuteAsync(
            """
            INSERT INTO erp_purchase_task_action_log
                (task_id,source_system,biz_type,biz_id,event_type,from_action,to_action,
                 operator_id,operator_name,operator_role,remark,payload_json,raw_json,raw_md5,
                 creator,create_time,updater,update_time,deleted)
            VALUES
                (@taskId,'ERP','TASK',@taskId,'UNIFIED_RECEIPT_CONFIRMED',@fromAction,@toAction,
                @operatorId,@signerName,@signerRole,@remark,@payloadJson,@rawJson,@rawMd5,
                 @creator,@now,@creator,@now,b'0')
            """,
            ("@taskId", taskId), ("@fromAction", fromAction), ("@toAction", toAction),
            ("@operatorId", currentUser.user_id), ("@signerName", ShenzhenSelfWarehouseSigner),
            ("@signerRole", "SELF_WAREHOUSE"), ("@creator", Truncate(currentUser.user_name, 64)),
            ("@remark", remark), ("@payloadJson", JsonSerializer.Serialize(payload)),
            ("@rawJson", rawJson), ("@rawMd5", Md5Hex(rawJson)),
            ("@now", now));
    }

    private async Task<PurchaseTaskRow?> ScalarTaskRowAsync(long taskId)
    {
        await using var connectionLease = await OpenConnectionLeaseAsync();
        await using var command = CreateCommand(
            """
            SELECT id, task_type, action, production_action, total_num,
                   close_type, closed_by, closed_time,
                   actual_produced_qty, fbm_qty, fba_qty,
                   actual_shipped_qty, actual_signed_qty
              FROM erp_purchase_task
             WHERE id=@taskId AND deleted=b'0'
             LIMIT 1
            """,
            ("@taskId", taskId));
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }
        return new PurchaseTaskRow
        {
            id = reader.IsDBNull(0) ? 0 : reader.GetInt64(0),
            task_type = reader.IsDBNull(1) ? null : reader.GetString(1),
            action = reader.IsDBNull(2) ? null : reader.GetString(2),
            production_action = reader.IsDBNull(3) ? null : reader.GetString(3),
            total_num = reader.IsDBNull(4) ? null : reader.GetInt64(4),
            close_type = reader.IsDBNull(5) ? null : reader.GetString(5),
            closed_by = reader.IsDBNull(6) ? null : reader.GetString(6),
            closed_time = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
            actual_produced_qty = reader.IsDBNull(8) ? 0 : reader.GetInt64(8),
            fbm_qty = reader.IsDBNull(9) ? 0 : reader.GetInt64(9),
            fba_qty = reader.IsDBNull(10) ? 0 : reader.GetInt64(10),
            actual_shipped_qty = reader.IsDBNull(11) ? 0 : reader.GetInt64(11),
            actual_signed_qty = reader.IsDBNull(12) ? 0 : reader.GetInt64(12)
        };
    }

    private async Task<PurchaseTaskFacts> BuildPurchaseTaskFactsAsync(PurchaseTaskRow task)
    {
        var totalQty = task.total_num ?? 0;
        var actualProducedQty = await ScalarAsync<long>(
            """
            SELECT COALESCE(SUM(produced_qty),0) FROM erp_purchase_task_production_batch
             WHERE task_id=@taskId AND deleted=b'0' AND status <> 'CANCELED'
            """,
            ("@taskId", task.id));
        if (string.Equals(task.task_type?.Trim(), TaskTypeFdTransfer, StringComparison.Ordinal))
        {
            actualProducedQty = Math.Max(actualProducedQty, totalQty);
        }

        var hasAllocations = await ScalarAsync<long>(
            "SELECT COUNT(*) FROM erp_purchase_task_order_allocation WHERE task_id=@taskId AND deleted=b'0'",
            ("@taskId", task.id)) > 0;
        long fbmQty;
        long fbaQty;
        if (hasAllocations)
        {
            fbmQty = await ScalarAsync<long>(
                """
                SELECT COALESCE(SUM(allocation_qty),0) FROM erp_purchase_task_order_allocation
                 WHERE task_id=@taskId AND deleted=b'0' AND usage_type='FBM'
                """,
                ("@taskId", task.id));
            fbaQty = await ScalarAsync<long>(
                """
                SELECT COALESCE(SUM(allocation_qty),0) FROM erp_purchase_task_order_allocation
                 WHERE task_id=@taskId AND deleted=b'0' AND usage_type='FBA'
                """,
                ("@taskId", task.id));
        }
        else
        {
            fbmQty = await ScalarAsync<long>(
                """
                SELECT COALESCE(SUM(shipment_qty),0) FROM erp_purchase_task_shipment_batch
                 WHERE task_id=@taskId AND deleted=b'0' AND status <> 'CANCELED'
                   AND (shipment_type IS NULL OR shipment_type <> 'FBA')
                """,
                ("@taskId", task.id));
            fbaQty = await ScalarAsync<long>(
                """
                SELECT COALESCE(SUM(shipment_qty),0) FROM erp_purchase_task_shipment_batch
                 WHERE task_id=@taskId AND deleted=b'0' AND status <> 'CANCELED'
                   AND shipment_type='FBA'
                """,
                ("@taskId", task.id));
        }

        var plannedShipmentQty = await ScalarAsync<long>(
            """
            SELECT COALESCE(SUM(shipment_qty),0) FROM erp_purchase_task_shipment_batch
             WHERE task_id=@taskId AND deleted=b'0' AND status <> 'CANCELED'
            """,
            ("@taskId", task.id));
        var fbaPlannedQty = await ScalarAsync<long>(
            """
            SELECT COALESCE(SUM(shipment_qty),0) FROM erp_purchase_task_shipment_batch
             WHERE task_id=@taskId AND deleted=b'0' AND status <> 'CANCELED' AND shipment_type='FBA'
            """,
            ("@taskId", task.id));
        var actualShippedQty = await ScalarAsync<long>(
            """
            SELECT COALESCE(SUM(shipment_qty),0) FROM erp_purchase_task_shipment_batch
             WHERE task_id=@taskId AND deleted=b'0' AND status <> 'CANCELED'
               AND (shipment_time IS NOT NULL OR status IN ('SHIPPED','PART_SIGNED','ALL_SIGNED'))
            """,
            ("@taskId", task.id));
        var actualSignedQty = await ScalarAsync<long>(
            """
            SELECT COALESCE(SUM(receipt_qty),0) FROM erp_purchase_task_receipt_record
             WHERE task_id=@taskId AND deleted=b'0'
            """,
            ("@taskId", task.id));

        var productionAction = actualProducedQty <= 0
            ? "5.1"
            : (totalQty > 0 && actualProducedQty >= totalQty ? "5.3" : "5.2");
        return new PurchaseTaskFacts(
            totalQty,
            actualProducedQty,
            fbmQty,
            fbaQty,
            plannedShipmentQty,
            fbaPlannedQty,
            actualShippedQty,
            actualSignedQty,
            productionAction);
    }

    private async Task<bool> ExistsManualCompleteEventAsync(long taskId)
    {
        return await ScalarAsync<long>(
            """
            SELECT COUNT(*) FROM erp_purchase_task_action_log
             WHERE task_id=@taskId AND deleted=b'0' AND event_type=@eventType
            """,
            ("@taskId", taskId), ("@eventType", ManualCompleteEvent)) > 0;
    }

    private static string ResolveNextAction(
        string? currentAction,
        bool manualCompleted,
        bool explicitlyCompleted,
        PurchaseTaskFacts facts)
    {
        if (currentAction is "3" or "9")
        {
            return currentAction;
        }
        if (manualCompleted)
        {
            return "8";
        }
        if (facts.ActualShippedQty > 0 && facts.ActualSignedQty < facts.ActualShippedQty)
        {
            return facts.ActualSignedQty > 0 ? "7.2" : "7.1";
        }
        if (facts.ActualSignedQty > 0 || facts.ActualShippedQty > 0)
        {
            if (explicitlyCompleted && CanCompletePurchaseTask(facts))
            {
                return "8";
            }
            return "7.3";
        }
        if (currentAction is "1" or "4" or "4.1" or "4.2" or "4.3" or "4.4" or "5")
        {
            return currentAction;
        }
        if (facts.TotalQty > 0 && facts.ActualProducedQty < facts.TotalQty)
        {
            return facts.ProductionAction;
        }
        var allocatedQty = facts.FbmQty + facts.FbaQty;
        if (facts.ActualProducedQty > allocatedQty)
        {
            return facts.ProductionAction;
        }
        if (facts.FbaQty > facts.FbaPlannedQty)
        {
            return "6.3";
        }
        if (allocatedQty > facts.PlannedShipmentQty)
        {
            return "6.4";
        }
        if (facts.PlannedShipmentQty > 0 && facts.ActualShippedQty <= 0)
        {
            return "6.4";
        }
        return facts.ProductionAction;
    }

    private static bool CanCompletePurchaseTask(PurchaseTaskFacts facts)
    {
        return facts.ActualShippedQty > 0 && facts.ActualSignedQty >= facts.ActualShippedQty;
    }

    private static string Md5Hex(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }
        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    }

    private record PurchaseTaskRow
    {
        /// <summary>记录标识。</summary>
        public long id { get; init; }
        /// <summary>任务类型。</summary>
        public string? task_type { get; init; }
        /// <summary>操作类型。</summary>
        public string? action { get; init; }
        /// <summary>生产操作类型。</summary>
        public string? production_action { get; init; }
        /// <summary>总数量。</summary>
        public long? total_num { get; init; }
        /// <summary>关闭类型。</summary>
        public string? close_type { get; init; }
        /// <summary>关闭人。</summary>
        public string? closed_by { get; init; }
        /// <summary>关闭时间。</summary>
        public DateTime? closed_time { get; init; }
        /// <summary>实际生产数量。</summary>
        public long actual_produced_qty { get; init; }
        /// <summary>FBM 数量。</summary>
        public long fbm_qty { get; init; }
        /// <summary>FBA 数量。</summary>
        public long fba_qty { get; init; }
        /// <summary>实际发货数量。</summary>
        public long actual_shipped_qty { get; init; }
        /// <summary>实际签收数量。</summary>
        public long actual_signed_qty { get; init; }
    }

    private record PurchaseTaskFacts(
        long TotalQty,
        long ActualProducedQty,
        long FbmQty,
        long FbaQty,
        long PlannedShipmentQty,
        long FbaPlannedQty,
        long ActualShippedQty,
        long ActualSignedQty,
        string ProductionAction);
}
