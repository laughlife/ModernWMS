using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using ModernWMS.Core.Models;

namespace ModernWMS.WMS.Entities.Models;

/// <summary>Only legal states for packing-task driven WMS dispatch orders.</summary>
public enum DispatchOrderStatus : byte
{
    PendingPick = 20,
    Picked = 30,
    Weighing = 40,
    PendingOutbound = 50,
    Outbound = 60,
    SourceCancelled = 90,
    ManualCancelled = 91
}

/// <summary>Immutable human adjudication outcome for a source change.</summary>
public enum DispatchSourceChangeDecision : byte
{
    ContinueShipment = 1,
    CancelShipment = 2,
    OutboundAnomaly = 3,
    Detected = 4
}

/// <summary>Local delivery state for the post-signing downstream notification.</summary>
public enum DispatchSignNotificationStatus : byte
{
    None = 0,
    Pending = 10,
    Sending = 20,
    Sent = 30,
    Failed = 40
}

/// <summary>
/// WMS-owned dispatch/picking order. New packing-task flows use this header;
/// historical FBA dispatch rows remain unchanged and may have no header.
/// </summary>
[Table("dispatch_order")]
[Index(nameof(dispatch_no), IsUnique = true)]
[Index(nameof(create_idempotency_key), IsUnique = true)]
[Index(nameof(warehouse_id), nameof(status))]
[Index(nameof(notification_status), nameof(notification_updated_at))]
public class DispatchOrderEntity : BaseModel
{
    [MaxLength(64)] public string dispatch_no { get; set; } = string.Empty;
    [MaxLength(64)] public string create_idempotency_key { get; set; } = string.Empty;
    public long warehouse_id { get; set; }
    public DispatchOrderStatus status { get; set; } = DispatchOrderStatus.PendingPick;
    [MaxLength(64)] public string source_version { get; set; } = string.Empty;
    public string source_snapshot { get; set; } = string.Empty;
    public bool source_change_pending { get; set; }
    public string source_change_snapshot { get; set; } = string.Empty;
    [MaxLength(64)] public string accepted_source_version { get; set; } = string.Empty;
    [MaxLength(64)] public string adjudicated_source_version { get; set; } = string.Empty;
    public int? adjudicated_by { get; set; }
    [MaxLength(128)] public string adjudicated_by_name { get; set; } = string.Empty;
    public DateTime? adjudicated_at { get; set; }
    [MaxLength(500)] public string adjudication_reason { get; set; } = string.Empty;
    /// <summary>Signing is a fact on an Outbound order and does not introduce another workflow status.</summary>
    public int? signed_qty { get; set; }
    public int? damaged_qty { get; set; }
    public int? signed_by { get; set; }
    [MaxLength(128)] public string signed_by_name { get; set; } = string.Empty;
    public DateTime? signed_at { get; set; }
    /// <summary>
    /// Local delivery uses an atomic Pending/Failed to Sending claim to prevent concurrent workers.
    /// A crash after the remote call remains an at-least-once window; the downstream receiver must
    /// deduplicate by the signing business key for effective exactly-once processing.
    /// </summary>
    public DispatchSignNotificationStatus notification_status { get; set; } = DispatchSignNotificationStatus.None;
    public int notification_attempt_count { get; set; }
    public DateTime? notification_sent_at { get; set; }
    [MaxLength(500)] public string notification_last_error { get; set; } = string.Empty;
    public DateTime? notification_updated_at { get; set; }
    public long tenant_id { get; set; }
    public int created_by { get; set; }
    [MaxLength(128)] public string creator { get; set; } = string.Empty;
    public DateTime create_time { get; set; }
    public DateTime last_update_time { get; set; }
    [ConcurrencyCheck] public long row_version { get; set; }
    public List<DispatchPackingTaskEntity> packing_tasks { get; set; } = [];
    public List<DispatchlistEntity> details { get; set; } = [];
    public List<DispatchSourceChangeEventEntity> source_change_events { get; set; } = [];
}

/// <summary>One immutable SellFox task identity attached to a WMS order.</summary>
[Table("dispatch_packing_task")]
[Index(nameof(active_source_task_id), IsUnique = true)]
[Index(nameof(dispatch_order_id), nameof(source_task_id), IsUnique = true)]
[Index(nameof(dispatch_order_id), nameof(is_active))]
public class DispatchPackingTaskEntity : BaseModel
{
    [ForeignKey(nameof(dispatch_order_id))]
    public DispatchOrderEntity dispatch_order { get; set; } = null!;
    public int dispatch_order_id { get; set; }
    [MaxLength(64)] public string task_no { get; set; } = string.Empty;
    public long source_task_id { get; set; }
    /// <summary>
    /// Must equal source_task_id whenever is_active is true and must be null otherwise.
    /// MySQL unique-null semantics enforce one active order while retaining cancelled history.
    /// The workflow service owns this two-field invariant transactionally.
    /// </summary>
    public long? active_source_task_id { get; set; }
    [MaxLength(64)] public string source_task_no { get; set; } = string.Empty;
    public string source_cartons_json { get; set; } = string.Empty;
    public DispatchOrderStatus status { get; set; } = DispatchOrderStatus.PendingPick;
    public int measured_box_count { get; set; }
    public int expected_box_count { get; set; }
    [MaxLength(64)] public string source_version { get; set; } = string.Empty;
    public bool stable_box_identity_verified { get; set; }
    [MaxLength(500)] public string box_identity_validation_error { get; set; } = string.Empty;
    public bool is_active { get; set; } = true;
    public DateTime? source_cancelled_at { get; set; }
    // TODO: SellFox weight/dimension writeback is task-batch only and is not implemented.
    [MaxLength(32)] public string writeback_status { get; set; } = "NOT_READY";
    [MaxLength(64)] public string writeback_request_hash { get; set; } = string.Empty;
    public string writeback_response { get; set; } = string.Empty;
    public int writeback_retry_count { get; set; }
    public DateTime? writeback_last_attempt_at { get; set; }
    public DateTime create_time { get; set; }
    public DateTime last_update_time { get; set; }
    [ConcurrencyCheck] public long row_version { get; set; }
    public List<DispatchlistEntity> details { get; set; } = [];
    public List<DispatchPackingTaskItemEntity> items { get; set; } = [];
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
[Index(nameof(packing_task_id), nameof(source_item_id), IsUnique = true)]
[Index(nameof(packing_task_id), nameof(is_active))]
public class DispatchPackingTaskItemEntity : BaseModel
{
    [ForeignKey(nameof(packing_task_id))]
    public DispatchPackingTaskEntity packing_task { get; set; } = null!;
    public int packing_task_id { get; set; }
    public long source_item_id { get; set; }
    public long? source_commodity_id { get; set; }
    public int? wms_sku_id { get; set; }
    [MaxLength(255)] public string commodity_sku { get; set; } = string.Empty;
    [MaxLength(500)] public string commodity_name { get; set; } = string.Empty;
    [MaxLength(128)] public string fn_sku { get; set; } = string.Empty;
    [MaxLength(255)] public string msku { get; set; } = string.Empty;
    public int? required_qty { get; set; }
    public int? source_quantity_shipped { get; set; }
    public int? source_stock_available { get; set; }
    [MaxLength(64)] public string source_version { get; set; } = string.Empty;
    public string source_snapshot { get; set; } = string.Empty;
    public bool is_active { get; set; } = true;
    public DateTime create_time { get; set; }
    public DateTime last_update_time { get; set; }
    [ConcurrencyCheck] public long row_version { get; set; }
    public List<DispatchlistEntity> dispatch_details { get; set; } = [];
    public List<DispatchpicklistEntity> pick_allocations { get; set; } = [];
}

/// <summary>
/// Append-only source-change adjudication/audit event. Services must insert a new row and never update it.
/// </summary>
[Table("dispatch_source_change_event")]
[Index(nameof(dispatch_order_id), nameof(source_version), nameof(decision), IsUnique = true)]
[Index(nameof(event_idempotency_key), IsUnique = true)]
public class DispatchSourceChangeEventEntity : BaseModel
{
    [ForeignKey(nameof(dispatch_order_id))]
    [DeleteBehavior(DeleteBehavior.Restrict)]
    public DispatchOrderEntity dispatch_order { get; set; } = null!;
    public int dispatch_order_id { get; set; }
    [MaxLength(64)] public string source_version { get; set; } = string.Empty;
    [MaxLength(64)] public string event_idempotency_key { get; set; } = string.Empty;
    public DispatchSourceChangeDecision decision { get; set; }
    public int operator_id { get; set; }
    [MaxLength(128)] public string operator_name { get; set; } = string.Empty;
    public DateTime decision_time { get; set; }
    [MaxLength(500)] public string reason { get; set; } = string.Empty;
    public string diff_snapshot { get; set; } = string.Empty;
}

/// <summary>
/// WMS measurements for a physical box proven to exist in source cartons_json.
/// Array position is display-only and must never be used as source_box_identity.
/// This new-flow table is intentionally separate from historical wms_dispatch_weighing_box,
/// whose ERP FBA identities and measurements remain unchanged and are never migrated here.
/// </summary>
[Table("weighing_box")]
[Index(nameof(packing_task_id), nameof(source_box_identity), IsUnique = true)]
[Index(nameof(packing_task_id), nameof(measurement_status))]
public class WeighingBoxEntity : BaseModel
{
    [ForeignKey(nameof(packing_task_id))]
    public DispatchPackingTaskEntity packing_task { get; set; } = null!;
    public int packing_task_id { get; set; }
    [MaxLength(64)] public string box_identity { get; set; } = string.Empty;
    [MaxLength(256)] public string source_box_identity { get; set; } = string.Empty;
    public int box_sequence { get; set; }
    public decimal? weight { get; set; }
    public decimal? length { get; set; }
    public decimal? width { get; set; }
    public decimal? height { get; set; }
    [MaxLength(16)] public string measurement_status { get; set; } = "UNMEASURED";
    public int? measured_by { get; set; }
    [MaxLength(128)] public string measured_by_name { get; set; } = string.Empty;
    public DateTime? measured_at { get; set; }
    public int? copied_from_box_id { get; set; }
    [ForeignKey(nameof(copied_from_box_id))]
    public WeighingBoxEntity? copied_from_box { get; set; }
    public string source_snapshot { get; set; } = string.Empty;
    public bool is_invalidated { get; set; }
    public DateTime? invalidated_at { get; set; }
    public DateTime create_time { get; set; }
    public DateTime last_update_time { get; set; }
    [ConcurrencyCheck] public long row_version { get; set; }
}
