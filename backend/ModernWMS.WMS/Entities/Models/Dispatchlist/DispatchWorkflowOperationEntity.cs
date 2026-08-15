using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using ModernWMS.Core.Models;

namespace ModernWMS.WMS.Entities.Models;

/// <summary>Commands whose request IDs form the dispatch workflow idempotency boundary.</summary>
public enum DispatchWorkflowOperation : byte
{
    CompletePicking = 10,
    StartWeighing = 20,
    SaveWeighing = 30,
    CompleteTaskWeighing = 40,
    CompleteWeighing = 50,
    ConfirmOutbound = 60,
    ContinueAfterSourceChange = 70,
    CancelAfterSourceChange = 80
}

/// <summary>
/// Result vocabulary reserved for workflow commands. The current workflow persists only
/// <see cref="Succeeded"/> rows; rejected and failed attempts roll back with the business transaction.
/// </summary>
public enum DispatchWorkflowOperationResultStatus : byte
{
    Started = 10,
    Succeeded = 20,
    Rejected = 30,
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
[Index(nameof(dispatch_order_id), nameof(operation), nameof(request_id), IsUnique = true)]
[Index(nameof(dispatch_order_id), nameof(create_time))]
public class DispatchWorkflowOperationEntity : BaseModel
{
    [ForeignKey(nameof(dispatch_order_id))]
    [DeleteBehavior(DeleteBehavior.Restrict)]
    public DispatchOrderEntity dispatch_order { get; set; } = null!;

    public int dispatch_order_id { get; set; }
    public DispatchWorkflowOperation operation { get; set; }

    [MaxLength(64)]
    public string request_id { get; set; } = string.Empty;

    public DispatchWorkflowOperationResultStatus result_status { get; set; }
    public DispatchOrderStatus? result_order_status { get; set; }
    public long? result_row_version { get; set; }
    public int create_operator { get; set; }

    [MaxLength(128)]
    public string create_operator_name { get; set; } = string.Empty;

    public DateTime create_time { get; set; }
}
