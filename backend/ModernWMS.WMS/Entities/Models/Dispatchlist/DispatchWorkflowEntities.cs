using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ModernWMS.Core.Models;

namespace ModernWMS.WMS.Entities.Models;

/// <summary>Only legal states for packing-task driven WMS dispatch orders.</summary>
public enum DispatchOrderStatus : byte
{
    /// <summary>Order is awaiting picking.</summary>
    PendingPick = 20,
    /// <summary>Picking is complete.</summary>
    Picked = 30,
    /// <summary>Order is being weighed.</summary>
    Weighing = 40,
    /// <summary>Order is awaiting outbound processing.</summary>
    PendingOutbound = 50,
    /// <summary>Order has been dispatched outbound.</summary>
    Outbound = 60,
    /// <summary>Source order was cancelled.</summary>
    SourceCancelled = 90,
    /// <summary>Order was cancelled manually.</summary>
    ManualCancelled = 91
}

/// <summary>Immutable human adjudication outcome for a source change.</summary>
public enum DispatchSourceChangeDecision : byte
{
    /// <summary>Continue shipment using the adjudicated source.</summary>
    ContinueShipment = 1,
    /// <summary>Cancel the shipment.</summary>
    CancelShipment = 2,
    /// <summary>Record an outbound anomaly.</summary>
    OutboundAnomaly = 3,
    /// <summary>Source change was detected.</summary>
    Detected = 4
}

/// <summary>Local delivery state for the post-signing downstream notification.</summary>
public enum DispatchSignNotificationStatus : byte
{
    /// <summary>No notification is queued.</summary>
    None = 0,
    /// <summary>Notification is waiting to be sent.</summary>
    Pending = 10,
    /// <summary>Notification send is in progress.</summary>
    Sending = 20,
    /// <summary>Notification was sent successfully.</summary>
    Sent = 30,
    /// <summary>Notification send failed.</summary>
    Failed = 40
}

/// <summary>
/// WMS-owned dispatch/picking order. New packing-task flows use this header;
/// historical FBA dispatch rows remain unchanged and may have no header.
/// </summary>
[Table("dispatch_order")]
public class DispatchOrderEntity : BaseModel
{
    /// <summary>
    /// 获取或设置 dispatch_no。
    /// </summary>
    [MaxLength(64)] public string dispatch_no { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 create_idempotency_key。
    /// </summary>
    [MaxLength(64)] public string create_idempotency_key { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 warehouse_id。
    /// </summary>
    public long warehouse_id { get; set; }
    /// <summary>
    /// 获取或设置 status。
    /// </summary>
    public DispatchOrderStatus status { get; set; } = DispatchOrderStatus.PendingPick;
    /// <summary>
    /// 获取或设置 source_version。
    /// </summary>
    [MaxLength(64)] public string source_version { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 source_snapshot。
    /// </summary>
    public string source_snapshot { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 source_change_pending。
    /// </summary>
    public bool source_change_pending { get; set; }
    /// <summary>
    /// 获取或设置 pending_source_version。
    /// </summary>
    [MaxLength(64)] public string pending_source_version { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 source_change_snapshot。
    /// </summary>
    public string source_change_snapshot { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 accepted_source_version。
    /// </summary>
    [MaxLength(64)] public string accepted_source_version { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 adjudicated_source_version。
    /// </summary>
    [MaxLength(64)] public string adjudicated_source_version { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 adjudicated_by。
    /// </summary>
    public int? adjudicated_by { get; set; }
    /// <summary>
    /// 获取或设置 adjudicated_by_name。
    /// </summary>
    [MaxLength(128)] public string adjudicated_by_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 adjudicated_at。
    /// </summary>
    public DateTime? adjudicated_at { get; set; }
    /// <summary>
    /// 获取或设置 adjudication_reason。
    /// </summary>
    [MaxLength(500)] public string adjudication_reason { get; set; } = string.Empty;
    /// <summary>Signing is a fact on an Outbound order and does not introduce another workflow status.</summary>
    public int? signed_qty { get; set; }
    /// <summary>
    /// 获取或设置 damaged_qty。
    /// </summary>
    public int? damaged_qty { get; set; }
    /// <summary>
    /// 获取或设置 signed_by。
    /// </summary>
    public int? signed_by { get; set; }
    /// <summary>
    /// 获取或设置 signed_by_name。
    /// </summary>
    [MaxLength(128)] public string signed_by_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 signed_at。
    /// </summary>
    public DateTime? signed_at { get; set; }
    /// <summary>
    /// Local delivery uses an atomic Pending/Failed to Sending claim to prevent concurrent workers.
    /// A crash after the remote call remains an at-least-once window; the downstream receiver must
    /// deduplicate by the signing business key for effective exactly-once processing.
    /// </summary>
    public DispatchSignNotificationStatus notification_status { get; set; } = DispatchSignNotificationStatus.None;
    /// <summary>
    /// 获取或设置 notification_attempt_count。
    /// </summary>
    public int notification_attempt_count { get; set; }
    /// <summary>
    /// 获取或设置 notification_sent_at。
    /// </summary>
    public DateTime? notification_sent_at { get; set; }
    /// <summary>
    /// 获取或设置 notification_last_error。
    /// </summary>
    [MaxLength(500)] public string notification_last_error { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 notification_updated_at。
    /// </summary>
    public DateTime? notification_updated_at { get; set; }
    /// <summary>
    /// 获取或设置 created_by。
    /// </summary>
    public int created_by { get; set; }
    /// <summary>
    /// 获取或设置 creator。
    /// </summary>
    [MaxLength(128)] public string creator { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 create_time。
    /// </summary>
    public DateTime create_time { get; set; }
    /// <summary>
    /// 获取或设置 last_update_time。
    /// </summary>
    public DateTime last_update_time { get; set; }
    /// <summary>
    /// 获取或设置 row_version。
    /// </summary>
    [ConcurrencyCheck] public long row_version { get; set; }
    /// <summary>
    /// 获取或设置 packing_tasks。
    /// </summary>
    public List<DispatchPackingTaskEntity> packing_tasks { get; set; } = [];
    /// <summary>
    /// 获取或设置 details。
    /// </summary>
    public List<DispatchlistEntity> details { get; set; } = [];
    /// <summary>
    /// 获取或设置 source_change_events。
    /// </summary>
    public List<DispatchSourceChangeEventEntity> source_change_events { get; set; } = [];
}

/// <summary>One immutable SellFox task identity attached to a WMS order.</summary>
[Table("dispatch_packing_task")]
public class DispatchPackingTaskEntity : BaseModel
{
    /// <summary>
    /// 获取或设置 dispatch_order。
    /// </summary>
    [ForeignKey(nameof(dispatch_order_id))]
    public DispatchOrderEntity dispatch_order { get; set; } = null!;
    /// <summary>
    /// 获取或设置 dispatch_order_id。
    /// </summary>
    public int dispatch_order_id { get; set; }
    /// <summary>
    /// 获取或设置 task_no。
    /// </summary>
    [MaxLength(64)] public string task_no { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 source_task_id。
    /// </summary>
    public long source_task_id { get; set; }
    /// <summary>
    /// Must equal source_task_id whenever is_active is true and must be null otherwise.
    /// MySQL unique-null semantics enforce one active order while retaining cancelled history.
    /// The workflow service owns this two-field invariant transactionally.
    /// </summary>
    public long? active_source_task_id { get; set; }
    /// <summary>
    /// 获取或设置 source_task_no。
    /// </summary>
    [MaxLength(64)] public string source_task_no { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 source_cartons_json。
    /// </summary>
    public string source_cartons_json { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 status。
    /// </summary>
    public DispatchOrderStatus status { get; set; } = DispatchOrderStatus.PendingPick;
    /// <summary>
    /// 获取或设置 measured_box_count。
    /// </summary>
    public int measured_box_count { get; set; }
    /// <summary>
    /// 获取或设置 expected_box_count。
    /// </summary>
    public int expected_box_count { get; set; }
    /// <summary>
    /// 获取或设置 packing_plan_status。
    /// </summary>
    [MaxLength(24)] public string packing_plan_status { get; set; } = "DRAFT";
    /// <summary>
    /// 获取或设置 actual_confirmed_at。
    /// </summary>
    public DateTime? actual_confirmed_at { get; set; }
    /// <summary>
    /// 获取或设置 actual_confirmed_by。
    /// </summary>
    public int? actual_confirmed_by { get; set; }
    /// <summary>
    /// 获取或设置 actual_confirmed_by_name。
    /// </summary>
    [MaxLength(128)] public string actual_confirmed_by_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 source_version。
    /// </summary>
    [MaxLength(64)] public string source_version { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 stable_box_identity_verified。
    /// </summary>
    public bool stable_box_identity_verified { get; set; }
    /// <summary>
    /// 获取或设置 box_identity_validation_error。
    /// </summary>
    [MaxLength(500)] public string box_identity_validation_error { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 is_active。
    /// </summary>
    public bool is_active { get; set; } = true;
    /// <summary>
    /// 获取或设置 source_cancelled_at。
    /// </summary>
    public DateTime? source_cancelled_at { get; set; }
    // TODO: SellFox weight/dimension writeback is task-batch only and is not implemented.
    /// <summary>
    /// 获取或设置 writeback_status。
    /// </summary>
    [MaxLength(32)] public string writeback_status { get; set; } = "NOT_READY";
    /// <summary>
    /// 获取或设置 writeback_request_hash。
    /// </summary>
    [MaxLength(64)] public string writeback_request_hash { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 writeback_response。
    /// </summary>
    public string writeback_response { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 writeback_retry_count。
    /// </summary>
    public int writeback_retry_count { get; set; }
    /// <summary>
    /// 获取或设置 writeback_last_attempt_at。
    /// </summary>
    public DateTime? writeback_last_attempt_at { get; set; }
    /// <summary>
    /// 获取或设置 create_time。
    /// </summary>
    public DateTime create_time { get; set; }
    /// <summary>
    /// 获取或设置 last_update_time。
    /// </summary>
    public DateTime last_update_time { get; set; }
    /// <summary>
    /// 获取或设置 row_version。
    /// </summary>
    [ConcurrencyCheck] public long row_version { get; set; }
    /// <summary>
    /// 获取或设置 details。
    /// </summary>
    public List<DispatchlistEntity> details { get; set; } = [];
    /// <summary>
    /// 获取或设置 items。
    /// </summary>
    public List<DispatchPackingTaskItemEntity> items { get; set; } = [];
    /// <summary>
    /// 获取或设置 boxes。
    /// </summary>
    public List<WeighingBoxEntity> boxes { get; set; } = [];

    /// <summary>Maintains the active marker and nullable unique key as one invariant.</summary>
    public void SetActiveState(bool active)
    {
        is_active = active;
        active_source_task_id = active ? source_task_id : null;
    }
}

/// <summary>Task-scoped source item snapshot. Equal SKUs in different tasks remain separate rows.</summary>
[Table("dispatch_packing_task_item")]
public class DispatchPackingTaskItemEntity : BaseModel
{
    /// <summary>
    /// 获取或设置 packing_task。
    /// </summary>
    [ForeignKey(nameof(packing_task_id))]
    public DispatchPackingTaskEntity packing_task { get; set; } = null!;
    /// <summary>
    /// 获取或设置 packing_task_id。
    /// </summary>
    public int packing_task_id { get; set; }
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
    [MaxLength(255)] public string commodity_sku { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 commodity_name。
    /// </summary>
    [MaxLength(500)] public string commodity_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 fn_sku。
    /// </summary>
    [MaxLength(128)] public string fn_sku { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 msku。
    /// </summary>
    [MaxLength(255)] public string msku { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 required_qty。
    /// </summary>
    public int? required_qty { get; set; }
    /// <summary>
    /// 获取或设置 source_quantity_shipped。
    /// </summary>
    public int? source_quantity_shipped { get; set; }
    /// <summary>
    /// 获取或设置 source_stock_available。
    /// </summary>
    public int? source_stock_available { get; set; }
    /// <summary>
    /// 获取或设置 variant_qty。
    /// </summary>
    public int? variant_qty { get; set; }
    /// <summary>
    /// 获取或设置 actual_packed_task_qty。
    /// </summary>
    public int? actual_packed_task_qty { get; set; }
    /// <summary>
    /// 获取或设置 actual_packed_required_qty。
    /// </summary>
    public int? actual_packed_required_qty { get; set; }
    /// <summary>
    /// 获取或设置 source_version。
    /// </summary>
    [MaxLength(64)] public string source_version { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 source_snapshot。
    /// </summary>
    public string source_snapshot { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 is_active。
    /// </summary>
    public bool is_active { get; set; } = true;
    /// <summary>
    /// 获取或设置 create_time。
    /// </summary>
    public DateTime create_time { get; set; }
    /// <summary>
    /// 获取或设置 last_update_time。
    /// </summary>
    public DateTime last_update_time { get; set; }
    /// <summary>
    /// 获取或设置 row_version。
    /// </summary>
    [ConcurrencyCheck] public long row_version { get; set; }
    /// <summary>
    /// 获取或设置 dispatch_details。
    /// </summary>
    public List<DispatchlistEntity> dispatch_details { get; set; } = [];
    /// <summary>
    /// 获取或设置 pick_allocations。
    /// </summary>
    public List<DispatchpicklistEntity> pick_allocations { get; set; } = [];
}

/// <summary>
/// Append-only source-change adjudication/audit event. Services must insert a new row and never update it.
/// </summary>
[Table("dispatch_source_change_event")]
public class DispatchSourceChangeEventEntity : BaseModel
{
    /// <summary>
    /// 获取或设置 dispatch_order。
    /// </summary>
    [ForeignKey(nameof(dispatch_order_id))]
    public DispatchOrderEntity dispatch_order { get; set; } = null!;
    /// <summary>
    /// 获取或设置 dispatch_order_id。
    /// </summary>
    public int dispatch_order_id { get; set; }
    /// <summary>
    /// 获取或设置 source_version。
    /// </summary>
    [MaxLength(64)] public string source_version { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 event_idempotency_key。
    /// </summary>
    [MaxLength(64)] public string event_idempotency_key { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 decision。
    /// </summary>
    public DispatchSourceChangeDecision decision { get; set; }
    /// <summary>
    /// 获取或设置 operator_id。
    /// </summary>
    public int operator_id { get; set; }
    /// <summary>
    /// 获取或设置 operator_name。
    /// </summary>
    [MaxLength(128)] public string operator_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 decision_time。
    /// </summary>
    public DateTime decision_time { get; set; }
    /// <summary>
    /// 获取或设置 reason。
    /// </summary>
    [MaxLength(500)] public string reason { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 diff_snapshot。
    /// </summary>
    public string diff_snapshot { get; set; } = string.Empty;
}

/// <summary>
/// WMS-owned physical box and its measurements for one packing task.
/// This new-flow table is intentionally separate from historical wms_dispatch_weighing_box,
/// whose ERP FBA identities and measurements remain unchanged and are never migrated here.
/// </summary>
[Table("weighing_box")]
public class WeighingBoxEntity : BaseModel
{
    /// <summary>
    /// 获取或设置 packing_task。
    /// </summary>
    [ForeignKey(nameof(packing_task_id))]
    public DispatchPackingTaskEntity packing_task { get; set; } = null!;
    /// <summary>
    /// 获取或设置 packing_task_id。
    /// </summary>
    public int packing_task_id { get; set; }
    /// <summary>
    /// 获取或设置 box_identity。
    /// </summary>
    [MaxLength(64)] public string box_identity { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 source_box_identity。
    /// </summary>
    [MaxLength(256)] public string source_box_identity { get; set; } = string.Empty;
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
    [MaxLength(16)] public string measurement_status { get; set; } = "UNMEASURED";
    /// <summary>
    /// 获取或设置 measured_by。
    /// </summary>
    public int? measured_by { get; set; }
    /// <summary>
    /// 获取或设置 measured_by_name。
    /// </summary>
    [MaxLength(128)] public string measured_by_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 measured_at。
    /// </summary>
    public DateTime? measured_at { get; set; }
    /// <summary>
    /// 获取或设置 copied_from_box_id。
    /// </summary>
    public int? copied_from_box_id { get; set; }
    /// <summary>
    /// 获取或设置 copied_from_box。
    /// </summary>
    [ForeignKey(nameof(copied_from_box_id))]
    public WeighingBoxEntity? copied_from_box { get; set; }
    /// <summary>
    /// 获取或设置 source_snapshot。
    /// </summary>
    public string source_snapshot { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 is_invalidated。
    /// </summary>
    public bool is_invalidated { get; set; }
    /// <summary>
    /// 获取或设置 invalidated_at。
    /// </summary>
    public DateTime? invalidated_at { get; set; }
    /// <summary>
    /// 获取或设置 create_time。
    /// </summary>
    public DateTime create_time { get; set; }
    /// <summary>
    /// 获取或设置 last_update_time。
    /// </summary>
    public DateTime last_update_time { get; set; }
    /// <summary>
    /// 获取或设置 row_version。
    /// </summary>
    [ConcurrencyCheck] public long row_version { get; set; }
    /// <summary>
    /// 获取或设置 items。
    /// </summary>
    public List<WeighingBoxItemEntity> items { get; set; } = [];
}

/// <summary>Actual inventory content placed into one weighing box.</summary>
[Table("weighing_box_item")]
public class WeighingBoxItemEntity : BaseModel
{
    /// <summary>
    /// 获取或设置 weighing_box。
    /// </summary>
    [ForeignKey(nameof(weighing_box_id))]
    public WeighingBoxEntity weighing_box { get; set; } = null!;
    /// <summary>
    /// 获取或设置 weighing_box_id。
    /// </summary>
    public int weighing_box_id { get; set; }
    /// <summary>Stable browser-side line identity within the box.</summary>
    [MaxLength(64)]
    public string client_line_key { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 packing_task_item。
    /// </summary>
    [ForeignKey(nameof(packing_task_item_id))]
    public DispatchPackingTaskItemEntity? packing_task_item { get; set; }
    /// <summary>
    /// 获取或设置 packing_task_item_id。
    /// </summary>
    public int? packing_task_item_id { get; set; }
    /// <summary>Historical WMS SKU snapshot; new stock-only rows leave it null.</summary>
    public int? wms_sku_id { get; set; }
    /// <summary>ERP stock balance row used by this actual line.</summary>
    public long erp_stock_id { get; set; }
    /// <summary>Historical position allocation snapshot; new rows leave it null.</summary>
    public long? stock_allocation_id { get; set; }
    /// <summary>Historical WMS owner snapshot; new rows leave it null.</summary>
    public int? goods_owner_id { get; set; }
    /// <summary>Historical WMS location snapshot; new rows leave it null.</summary>
    public int? goods_location_id { get; set; }
    /// <summary>SKU snapshot.</summary>
    [MaxLength(255)]
    public string sku_code { get; set; } = string.Empty;
    /// <summary>Commodity-name snapshot.</summary>
    [MaxLength(500)]
    public string commodity_name { get; set; } = string.Empty;
    /// <summary>Actual stock-unit quantity placed in the box.</summary>
    public int actual_qty { get; set; }
    /// <summary>Materialized dispatch pick after actual confirmation.</summary>
    public int? dispatchpicklist_id { get; set; }
    /// <summary>
    /// 获取或设置 create_time。
    /// </summary>
    public DateTime create_time { get; set; }
    /// <summary>
    /// 获取或设置 last_update_time。
    /// </summary>
    public DateTime last_update_time { get; set; }
    /// <summary>
    /// 获取或设置 row_version。
    /// </summary>
    [ConcurrencyCheck] public long row_version { get; set; }
}
