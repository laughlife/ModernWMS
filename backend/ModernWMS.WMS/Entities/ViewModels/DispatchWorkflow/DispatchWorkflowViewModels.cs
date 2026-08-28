namespace ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;

/// <summary>
/// 表示 CreateDispatchOrderRequest 类型。
/// </summary>
public sealed class CreateDispatchOrderRequest
{
    /// <summary>
    /// 获取或设置 warehouse_id。
    /// </summary>
    public long warehouse_id { get; set; }
    /// <summary>
    /// 获取或设置 source_task_ids。
    /// </summary>
    public List<long> source_task_ids { get; set; } = [];
    /// <summary>
    /// 获取或设置 idempotency_key。
    /// </summary>
    public string idempotency_key { get; set; } = string.Empty;
}

/// <summary>
/// 表示 CompletePickingRequest 类型。
/// </summary>
public sealed class CompletePickingRequest
{
    /// <summary>
    /// 获取或设置 request_id。
    /// </summary>
    public string request_id { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 row_version。
    /// </summary>
    public long row_version { get; set; }
}

/// <summary>
/// 表示 RollbackPendingPickRequest 类型。
/// </summary>
public sealed class RollbackPendingPickRequest
{
    /// <summary>
    /// 获取或设置 request_id。
    /// </summary>
    public string request_id { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 row_version。
    /// </summary>
    public long row_version { get; set; }
}

/// <summary>
/// 表示 RollbackPendingPickResult 类型。
/// </summary>
public sealed class RollbackPendingPickResult
{
    /// <summary>
    /// 获取或设置 order_id。
    /// </summary>
    public int order_id { get; set; }
    /// <summary>
    /// 获取或设置 request_id。
    /// </summary>
    public string request_id { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 status。
    /// </summary>
    public string status { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 row_version。
    /// </summary>
    public long row_version { get; set; }
}

/// <summary>
/// 表示 CompletePickingResult 类型。
/// </summary>
public sealed class CompletePickingResult
{
    /// <summary>
    /// 获取或设置 order_id。
    /// </summary>
    public int order_id { get; set; }
    /// <summary>
    /// 获取或设置 request_id。
    /// </summary>
    public string request_id { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 status。
    /// </summary>
    public string status { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 row_version。
    /// </summary>
    public long row_version { get; set; }
}

/// <summary>
/// 表示 WeighingOrderCommandRequest 类型。
/// </summary>
public sealed class WeighingOrderCommandRequest
{
    /// <summary>
    /// 获取或设置 request_id。
    /// </summary>
    public string request_id { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 row_version。
    /// </summary>
    public long row_version { get; set; }
}

/// <summary>
/// 表示 WeighingCommandResult 类型。
/// </summary>
public sealed class WeighingCommandResult
{
    /// <summary>
    /// 获取或设置 order_id。
    /// </summary>
    public int order_id { get; set; }
    /// <summary>
    /// 获取或设置 request_id。
    /// </summary>
    public string request_id { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 status。
    /// </summary>
    public string status { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 row_version。
    /// </summary>
    public long row_version { get; set; }
}

/// <summary>
/// 表示 OutboundCommandRequest 类型。
/// </summary>
public sealed class OutboundCommandRequest
{
    /// <summary>
    /// 获取或设置 request_id。
    /// </summary>
    public string request_id { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 row_version。
    /// </summary>
    public long row_version { get; set; }
}

/// <summary>
/// 表示 OutboundCommandResult 类型。
/// </summary>
public class OutboundCommandResult
{
    /// <summary>
    /// 获取或设置 order_id。
    /// </summary>
    public int order_id { get; set; }
    /// <summary>
    /// 获取或设置 request_id。
    /// </summary>
    public string request_id { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 status。
    /// </summary>
    public string status { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 row_version。
    /// </summary>
    public long row_version { get; set; }
}

/// <summary>
/// 表示 DispatchCarrierOptionViewModel 类型。
/// </summary>
public sealed class DispatchCarrierOptionViewModel
{
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    public long id { get; set; }
    /// <summary>
    /// 获取或设置 name。
    /// </summary>
    public string name { get; set; } = string.Empty;
}

/// <summary>
/// 表示 SetDispatchCarrierRequest 类型。
/// </summary>
public sealed class SetDispatchCarrierRequest
{
    /// <summary>
    /// 获取或设置 order_ids。
    /// </summary>
    public List<int> order_ids { get; set; } = [];
    /// <summary>
    /// 获取或设置 carrier_warehouse_id。
    /// </summary>
    public long carrier_warehouse_id { get; set; }
}

/// <summary>
/// 表示 SetDispatchCarrierResult 类型。
/// </summary>
public sealed class SetDispatchCarrierResult
{
    /// <summary>
    /// 获取或设置 updated_order_count。
    /// </summary>
    public int updated_order_count { get; set; }
    /// <summary>
    /// 获取或设置 carrier_warehouse_id。
    /// </summary>
    public long carrier_warehouse_id { get; set; }
    /// <summary>
    /// 获取或设置 carrier_unit。
    /// </summary>
    public string carrier_unit { get; set; } = string.Empty;
}

/// <summary>
/// 表示 SignDispatchOrderRequest 类型。
/// </summary>
public sealed class SignDispatchOrderRequest
{
    /// <summary>
    /// 获取或设置 request_id。
    /// </summary>
    public string request_id { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 row_version。
    /// </summary>
    public long row_version { get; set; }
    /// <summary>
    /// 获取或设置 damaged_qty。
    /// </summary>
    public int damaged_qty { get; set; }
}

/// <summary>
/// 表示 SignDispatchOrderResult 类型。
/// </summary>
public sealed class SignDispatchOrderResult : OutboundCommandResult
{
    /// <summary>
    /// 获取或设置 signed_qty。
    /// </summary>
    public int signed_qty { get; set; }
    /// <summary>
    /// 获取或设置 damaged_qty。
    /// </summary>
    public int damaged_qty { get; set; }
    /// <summary>
    /// 获取或设置 notification_status。
    /// </summary>
    public string notification_status { get; set; } = string.Empty;
}

/// <summary>
/// 表示 SaveWeighingBoxRequest 类型。
/// </summary>
public sealed record SaveWeighingBoxRequest
{
    /// <summary>
    /// 获取或设置 request_id。
    /// </summary>
    public string request_id { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 row_version。
    /// </summary>
    public long row_version { get; set; }
    /// <summary>
    /// 获取或设置 box_row_version。
    /// </summary>
    public long box_row_version { get; set; }
    /// <summary>
    /// 获取或设置 weight。
    /// </summary>
    public decimal weight { get; set; }
    /// <summary>
    /// 获取或设置 length。
    /// </summary>
    public decimal length { get; set; }
    /// <summary>
    /// 获取或设置 width。
    /// </summary>
    public decimal width { get; set; }
    /// <summary>
    /// 获取或设置 height。
    /// </summary>
    public decimal height { get; set; }
}

/// <summary>
/// 表示 CopyWeighingBoxRequest 类型。
/// </summary>
public sealed class CopyWeighingBoxRequest
{
    /// <summary>
    /// 获取或设置 request_id。
    /// </summary>
    public string request_id { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 row_version。
    /// </summary>
    public long row_version { get; set; }
    /// <summary>
    /// 获取或设置 source_box_id。
    /// </summary>
    public int source_box_id { get; set; }
    /// <summary>
    /// 获取或设置 target_box_row_version。
    /// </summary>
    public long target_box_row_version { get; set; }
}

/// <summary>
/// 表示 WeighingBoxViewModel 类型。
/// </summary>
public sealed class WeighingBoxViewModel
{
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    public int id { get; set; }
    /// <summary>
    /// 获取或设置 packing_task_id。
    /// </summary>
    public int packing_task_id { get; set; }
    /// <summary>
    /// 获取或设置 source_box_identity。
    /// </summary>
    public string source_box_identity { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 box_sequence。
    /// </summary>
    public int box_sequence { get; set; }
    /// <summary>
    /// 获取或设置 weight。
    /// </summary>
    public decimal? weight { get; set; }
    /// <summary>
    /// 获取或设置 length。
    /// </summary>
    public decimal? length { get; set; }
    /// <summary>
    /// 获取或设置 width。
    /// </summary>
    public decimal? width { get; set; }
    /// <summary>
    /// 获取或设置 height。
    /// </summary>
    public decimal? height { get; set; }
    /// <summary>
    /// 获取或设置 measurement_status。
    /// </summary>
    public string measurement_status { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 copied_from_box_id。
    /// </summary>
    public int? copied_from_box_id { get; set; }
    /// <summary>
    /// 获取或设置 row_version。
    /// </summary>
    public long row_version { get; set; }
    /// <summary>
    /// 获取或设置 items。
    /// </summary>
    public List<PackingPlanBoxItemViewModel> items { get; set; } = [];
}

/// <summary>
/// 表示 PackingPlanItemViewModel 类型。
/// </summary>
public sealed class PackingPlanItemViewModel
{
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    public int id { get; set; }
    /// <summary>
    /// 获取或设置 commodity_sku。
    /// </summary>
    public string commodity_sku { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 commodity_name。
    /// </summary>
    public string commodity_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 fn_sku。
    /// </summary>
    public string fn_sku { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 msku。
    /// </summary>
    public string msku { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 main_image。
    /// </summary>
    public string main_image { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 task_qty。
    /// </summary>
    public int task_qty { get; set; }
    /// <summary>
    /// 获取或设置 variant_qty。
    /// </summary>
    public int variant_qty { get; set; }
    /// <summary>
    /// 获取或设置 required_qty。
    /// </summary>
    public int required_qty { get; set; }
    /// <summary>
    /// 获取或设置 actual_packed_task_qty。
    /// </summary>
    public int? actual_packed_task_qty { get; set; }
    /// <summary>
    /// 获取或设置 actual_packed_required_qty。
    /// </summary>
    public int? actual_packed_required_qty { get; set; }
}

/// <summary>
/// 表示 PackingPlanBoxItemViewModel 类型。
/// </summary>
public sealed class PackingPlanBoxItemViewModel
{
    /// <summary>Stable line identity within one box.</summary>
    public string client_line_key { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 packing_task_item_id。
    /// </summary>
    public int? packing_task_item_id { get; set; }
    /// <summary>Selected stock-allocation identity.</summary>
    public long stock_allocation_id { get; set; }
    /// <summary>Server-resolved ERP stock identity.</summary>
    public long erp_stock_id { get; set; }
    /// <summary>Server-resolved WMS SKU.</summary>
    public int wms_sku_id { get; set; }
    /// <summary>Server-resolved owner snapshot.</summary>
    public int goods_owner_id { get; set; }
    /// <summary>Server-resolved location snapshot.</summary>
    public int goods_location_id { get; set; }
    /// <summary>Server-resolved SKU-code snapshot.</summary>
    public string sku_code { get; set; } = string.Empty;
    /// <summary>Server-resolved commodity-name snapshot.</summary>
    public string commodity_name { get; set; } = string.Empty;
    /// <summary>Current available stock shown as warning information only.</summary>
    public long available_qty { get; set; }
    /// <summary>Actual stock-unit quantity placed in the box.</summary>
    public int actual_qty { get; set; }
    /// <summary>Materialized dispatch-pick identity after confirmation.</summary>
    public int? dispatchpicklist_id { get; set; }
}

/// <summary>Selectable actual stock allocation for packing.</summary>
public sealed class ActualPackingStockViewModel
{
    public long stock_allocation_id { get; set; }
    public long erp_stock_id { get; set; }
    public int wms_sku_id { get; set; }
    public int goods_owner_id { get; set; }
    public int goods_location_id { get; set; }
    public string goods_owner_name { get; set; } = string.Empty;
    public string location_name { get; set; } = string.Empty;
    public string sku_code { get; set; } = string.Empty;
    public string commodity_name { get; set; } = string.Empty;
    public long available_qty { get; set; }
}

/// <summary>
/// 表示 PackingPlanBoxViewModel 类型。
/// </summary>
public sealed class PackingPlanBoxViewModel
{
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    public int? id { get; set; }
    /// <summary>
    /// 获取或设置 client_key。
    /// </summary>
    public string client_key { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 box_sequence。
    /// </summary>
    public int box_sequence { get; set; }
    /// <summary>
    /// 获取或设置 weight。
    /// </summary>
    public decimal? weight { get; set; }
    /// <summary>
    /// 获取或设置 length。
    /// </summary>
    public decimal? length { get; set; }
    /// <summary>
    /// 获取或设置 width。
    /// </summary>
    public decimal? width { get; set; }
    /// <summary>
    /// 获取或设置 height。
    /// </summary>
    public decimal? height { get; set; }
    /// <summary>
    /// 获取或设置 row_version。
    /// </summary>
    public long row_version { get; set; }
    /// <summary>
    /// 获取或设置 items。
    /// </summary>
    public List<PackingPlanBoxItemViewModel> items { get; set; } = [];
}

/// <summary>
/// 表示 PackingPlanViewModel 类型。
/// </summary>
public sealed class PackingPlanViewModel
{
    /// <summary>
    /// 获取或设置 order_id。
    /// </summary>
    public int order_id { get; set; }
    /// <summary>
    /// 获取或设置 packing_task_id。
    /// </summary>
    public int packing_task_id { get; set; }
    /// <summary>
    /// 获取或设置 packing_task_no。
    /// </summary>
    public string packing_task_no { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 packing_plan_status。
    /// </summary>
    public string packing_plan_status { get; set; } = "DRAFT";
    /// <summary>
    /// 获取或设置 row_version。
    /// </summary>
    public long row_version { get; set; }
    /// <summary>
    /// 获取或设置 task_row_version。
    /// </summary>
    public long task_row_version { get; set; }
    /// <summary>
    /// 获取或设置 items。
    /// </summary>
    public List<PackingPlanItemViewModel> items { get; set; } = [];
    /// <summary>
    /// 获取或设置 boxes。
    /// </summary>
    public List<PackingPlanBoxViewModel> boxes { get; set; } = [];
}

/// <summary>
/// 表示 SavePackingPlanRequest 类型。
/// </summary>
public sealed class SavePackingPlanRequest
{
    /// <summary>
    /// 获取或设置 request_id。
    /// </summary>
    public string request_id { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 row_version。
    /// </summary>
    public long row_version { get; set; }
    /// <summary>
    /// 获取或设置 task_row_version。
    /// </summary>
    public long task_row_version { get; set; }
    /// <summary>
    /// 获取或设置 boxes。
    /// </summary>
    public List<PackingPlanBoxViewModel> boxes { get; set; } = [];
}

/// <summary>
/// 表示 ConfirmActualPackingRequest 类型。
/// </summary>
public sealed class ConfirmActualPackingRequest
{
    /// <summary>
    /// 获取或设置 request_id。
    /// </summary>
    public string request_id { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 row_version。
    /// </summary>
    public long row_version { get; set; }
    /// <summary>
    /// 获取或设置 task_row_version。
    /// </summary>
    public long task_row_version { get; set; }
}

/// <summary>
/// 表示 SourceDecisionRequest 类型。
/// </summary>
public sealed class SourceDecisionRequest
{
    /// <summary>
    /// 获取或设置 decision。
    /// </summary>
    public string decision { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 source_version。
    /// </summary>
    public string source_version { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 reason。
    /// </summary>
    public string reason { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 request_id。
    /// </summary>
    public string request_id { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 row_version。
    /// </summary>
    public long row_version { get; set; }
}

/// <summary>
/// 表示 PostPickSourceGuardResult 类型。
/// </summary>
public sealed class PostPickSourceGuardResult
{
    /// <summary>
    /// 获取或设置 source_change_pending。
    /// </summary>
    public bool source_change_pending { get; set; }
    /// <summary>
    /// 获取或设置 error_code。
    /// </summary>
    public string error_code { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 source_version。
    /// </summary>
    public string source_version { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 row_version。
    /// </summary>
    public long row_version { get; set; }
}

/// <summary>
/// 表示 SourceDecisionResult 类型。
/// </summary>
public sealed class SourceDecisionResult
{
    /// <summary>
    /// 获取或设置 order_id。
    /// </summary>
    public int order_id { get; set; }
    /// <summary>
    /// 获取或设置 request_id。
    /// </summary>
    public string request_id { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 decision。
    /// </summary>
    public string decision { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 source_version。
    /// </summary>
    public string source_version { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 status。
    /// </summary>
    public string status { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 source_change_pending。
    /// </summary>
    public bool source_change_pending { get; set; }
    /// <summary>
    /// 获取或设置 row_version。
    /// </summary>
    public long row_version { get; set; }
}

/// <summary>
/// 表示 DispatchOrderPageRequest 类型。
/// </summary>
public sealed class DispatchOrderPageRequest
{
    /// <summary>
    /// 获取或设置 status。
    /// </summary>
    public string status { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 warehouse_id。
    /// </summary>
    public long warehouse_id { get; set; }
    /// <summary>
    /// 获取或设置 keyword。
    /// </summary>
    public string keyword { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 group_id。
    /// </summary>
    public long? group_id { get; set; }
    /// <summary>
    /// 获取或设置 member_id。
    /// </summary>
    public long? member_id { get; set; }
    /// <summary>
    /// 获取或设置 pageIndex。
    /// </summary>
    public int pageIndex { get; set; } = 1;
    /// <summary>
    /// 获取或设置 pageSize。
    /// </summary>
    public int pageSize { get; set; } = 20;
}

/// <summary>
/// 表示 DispatchOrderSummaryViewModel 类型。
/// </summary>
public class DispatchOrderSummaryViewModel
{
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    public int id { get; set; }
    /// <summary>
    /// 获取或设置 dispatch_no。
    /// </summary>
    public string dispatch_no { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 warehouse_id。
    /// </summary>
    public long warehouse_id { get; set; }
    /// <summary>
    /// 获取或设置 status。
    /// </summary>
    public string status { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 packing_task_nos。
    /// </summary>
    public List<string> packing_task_nos { get; set; } = [];
    /// <summary>
    /// 获取或设置 creator。
    /// </summary>
    public string creator { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 create_time。
    /// </summary>
    public DateTime create_time { get; set; }
    /// <summary>
    /// 获取或设置 last_update_time。
    /// </summary>
    public DateTime last_update_time { get; set; }
    /// <summary>
    /// 获取或设置 source_change_pending。
    /// </summary>
    public bool source_change_pending { get; set; }
    /// <summary>
    /// 获取或设置 pending_source_version。
    /// </summary>
    public string pending_source_version { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 source_change_snapshot。
    /// </summary>
    public string source_change_snapshot { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 accepted_source_version。
    /// </summary>
    public string accepted_source_version { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 signed_qty。
    /// </summary>
    public int? signed_qty { get; set; }
    /// <summary>
    /// 获取或设置 damaged_qty。
    /// </summary>
    public int? damaged_qty { get; set; }
    /// <summary>
    /// 获取或设置 signed_at。
    /// </summary>
    public DateTime? signed_at { get; set; }
    /// <summary>
    /// 获取或设置 signed_by_name。
    /// </summary>
    public string signed_by_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 notification_status。
    /// </summary>
    public string notification_status { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 notification_last_error。
    /// </summary>
    public string notification_last_error { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 outbound_source_anomaly。
    /// </summary>
    public bool outbound_source_anomaly { get; set; }
    /// <summary>
    /// 获取或设置 outbound_source_anomaly_snapshot。
    /// </summary>
    public string outbound_source_anomaly_snapshot { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 carrier_warehouse_id。
    /// </summary>
    public long? carrier_warehouse_id { get; set; }
    /// <summary>
    /// 获取或设置 carrier_unit。
    /// </summary>
    public string carrier_unit { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 row_version。
    /// </summary>
    public long row_version { get; set; }
}

/// <summary>
/// 表示 DispatchOrderDetailViewModel 类型。
/// </summary>
public sealed class DispatchOrderDetailViewModel : DispatchOrderSummaryViewModel
{
    /// <summary>
    /// 获取或设置 source_version。
    /// </summary>
    public string source_version { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 packing_tasks。
    /// </summary>
    public List<DispatchPackingTaskViewModel> packing_tasks { get; set; } = [];
}

/// <summary>
/// 表示 DispatchPackingTaskViewModel 类型。
/// </summary>
public sealed class DispatchPackingTaskViewModel
{
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    public int id { get; set; }
    /// <summary>
    /// 获取或设置 source_task_id。
    /// </summary>
    public long source_task_id { get; set; }
    /// <summary>
    /// 获取或设置 source_task_no。
    /// </summary>
    public string source_task_no { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 status。
    /// </summary>
    public string status { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 source_version。
    /// </summary>
    public string source_version { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 expected_box_count。
    /// </summary>
    public int expected_box_count { get; set; }
    /// <summary>
    /// 获取或设置 measured_box_count。
    /// </summary>
    public int measured_box_count { get; set; }
    /// <summary>
    /// 获取或设置 items。
    /// </summary>
    public List<DispatchPackingTaskItemViewModel> items { get; set; } = [];
}

/// <summary>
/// 表示 DispatchPackingTaskItemViewModel 类型。
/// </summary>
public sealed class DispatchPackingTaskItemViewModel
{
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    public int id { get; set; }
    /// <summary>
    /// 获取或设置 source_item_id。
    /// </summary>
    public long source_item_id { get; set; }
    /// <summary>
    /// 获取或设置 source_commodity_id。
    /// </summary>
    public long? source_commodity_id { get; set; }
    /// <summary>
    /// 获取或设置 wms_sku_id。
    /// </summary>
    public int? wms_sku_id { get; set; }
    /// <summary>
    /// 获取或设置 commodity_sku。
    /// </summary>
    public string commodity_sku { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 commodity_name。
    /// </summary>
    public string commodity_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 main_image。
    /// </summary>
    public string main_image { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 fn_sku。
    /// </summary>
    public string fn_sku { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 msku。
    /// </summary>
    public string msku { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 task_qty。
    /// </summary>
    public int? task_qty { get; set; }
    /// <summary>
    /// 获取或设置 required_qty。
    /// </summary>
    public int? required_qty { get; set; }
    /// <summary>
    /// 获取或设置 source_stock_available。
    /// </summary>
    public int? source_stock_available { get; set; }
}

/// <summary>
/// 表示 DispatchOrderPageResult 类型。
/// </summary>
public sealed record DispatchOrderPageResult(
    List<DispatchOrderSummaryViewModel> Data,
    int Totals);

/// <summary>
/// 表示 DispatchOrderStatusCounts 类型。
/// </summary>
public sealed record DispatchOrderStatusCounts(
    IReadOnlyDictionary<string, int> Counts);
