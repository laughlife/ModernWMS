using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ModernWMS.Core.Models;

namespace ModernWMS.WMS.Entities.Models;

/// <summary>Commands whose request IDs form the dispatch workflow idempotency boundary.</summary>
public enum DispatchWorkflowOperation : byte
{
    /// <summary>
    /// 表示 RollbackPendingPick 枚举值。
    /// </summary>
    RollbackPendingPick = 5,
    /// <summary>
    /// 表示回退到上一业务环节。
    /// </summary>
    RollbackPreviousStage = 6,
    /// <summary>
    /// 表示 CompletePicking 枚举值。
    /// </summary>
    CompletePicking = 10,
    /// <summary>
    /// 表示 StartWeighing 枚举值。
    /// </summary>
    StartWeighing = 20,
    /// <summary>
    /// 表示 SaveWeighing 枚举值。
    /// </summary>
    SaveWeighing = 30,
    /// <summary>
    /// 表示 SavePackingDraft 枚举值。
    /// </summary>
    SavePackingDraft = 32,
    /// <summary>
    /// 表示 CopyWeighing 枚举值。
    /// </summary>
    CopyWeighing = 35,
    /// <summary>
    /// 表示 ConfirmPacking 枚举值。
    /// </summary>
    ConfirmPacking = 36,
    /// <summary>
    /// 表示 ConfirmActualPacking 枚举值。
    /// </summary>
    ConfirmActualPacking = 37,
    /// <summary>
    /// 表示 CompleteTaskWeighing 枚举值。
    /// </summary>
    CompleteTaskWeighing = 40,
    /// <summary>
    /// 表示 CompleteWeighing 枚举值。
    /// </summary>
    CompleteWeighing = 50,
    /// <summary>
    /// 表示 ConfirmOutbound 枚举值。
    /// </summary>
    ConfirmOutbound = 60,
    /// <summary>
    /// 表示 CancelOutbound 枚举值。
    /// </summary>
    CancelOutbound = 65,
    /// <summary>
    /// 表示 ContinueAfterSourceChange 枚举值。
    /// </summary>
    ContinueAfterSourceChange = 70,
    /// <summary>
    /// 表示 CancelAfterSourceChange 枚举值。
    /// </summary>
    CancelAfterSourceChange = 80,
    /// <summary>
    /// 表示 Sign 枚举值。
    /// </summary>
    Sign = 90
}

/// <summary>
/// Result vocabulary reserved for workflow commands. The current workflow persists only
/// <see cref="Succeeded"/> rows; rejected and failed attempts roll back with the business transaction.
/// </summary>
public enum DispatchWorkflowOperationResultStatus : byte
{
    /// <summary>
    /// 表示 Started 枚举值。
    /// </summary>
    Started = 10,
    /// <summary>
    /// 表示 Succeeded 枚举值。
    /// </summary>
    Succeeded = 20,
    /// <summary>
    /// 表示 Rejected 枚举值。
    /// </summary>
    Rejected = 30,
    /// <summary>
    /// 表示 Failed 枚举值。
    /// </summary>
    Failed = 40
}

/// <summary>
/// WMS-owned idempotency ledger for state-changing dispatch commands.
/// One order, operation and request ID can produce only one persisted successful result.
/// Rejected or failed attempts are not recorded in this release, so the same request ID may
/// be retried after its business preconditions change. Successful replay returns the stored
/// order status and row version without executing the command again.
/// </summary>
[Table("dispatch_workflow_operation")]
public class DispatchWorkflowOperationEntity : BaseModel
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
    /// 获取或设置 operation。
    /// </summary>
    public DispatchWorkflowOperation operation { get; set; }

    /// <summary>
    /// 获取或设置 request_id。
    /// </summary>
    [MaxLength(64)]
    public string request_id { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 result_status。
    /// </summary>
    public DispatchWorkflowOperationResultStatus result_status { get; set; }
    /// <summary>
    /// 获取或设置 result_order_status。
    /// </summary>
    public DispatchOrderStatus? result_order_status { get; set; }
    /// <summary>
    /// 获取或设置 result_row_version。
    /// </summary>
    public long? result_row_version { get; set; }
    /// <summary>
    /// 获取或设置 create_operator。
    /// </summary>
    public int create_operator { get; set; }

    /// <summary>
    /// 获取或设置 create_operator_name。
    /// </summary>
    [MaxLength(128)]
    public string create_operator_name { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 create_time。
    /// </summary>
    public DateTime create_time { get; set; }
}
