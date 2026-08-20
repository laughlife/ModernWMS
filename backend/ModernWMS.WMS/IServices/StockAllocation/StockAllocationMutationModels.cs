namespace ModernWMS.WMS.IServices.StockAllocation;

/// <summary>
/// Stable business identity and operator information shared by the ERP balance
/// ledger and the WMS allocation audit.
/// </summary>
public sealed record StockMutationContext(
    long TenantId,
    long ErpWarehouseId,
    string OperationKey,
    string BizType,
    long BizId,
    long BizItemId,
    long OperatorId,
    string Operator,
    string Remark = "",
    StockReservationMutationContext? Reservation = null);

/// <summary>
/// Stable ownership identity required by every occupied-quantity mutation.
/// ExistingReservationId/ItemId are carried by downstream business rows after
/// the initial RESERVE; they prevent a later release or consume from guessing
/// the owner from stock dimensions.
/// </summary>
public sealed record StockReservationMutationContext(
    string Namespace,
    string CommandId,
    string SourceSystem,
    string ReservationBizType,
    long ReservationBizId,
    string? ReservationBizNo,
    string? CarrierBizType,
    long? CarrierBizId,
    string SourceLineType,
    long? SourceLineId,
    string SourceLineKey,
    long? ExistingReservationId = null,
    long? ExistingReservationItemId = null);

public sealed record StockReservationPrelockRequest(
    StockMutationContext Context,
    long ErpStockId,
    long AllocationId,
    string EventType);

public sealed record StockQuantitySnapshot(
    long AvailableQty,
    long OccupiedQty,
    long TotalQty);

public sealed record StockAllocationQuantitySnapshot(
    long AllocatedQty,
    long OccupiedQty);

public sealed record StockAllocationMutationChange(
    long AllocationId,
    string EventType,
    long? CounterpartAllocationId,
    StockAllocationQuantitySnapshot Before,
    StockAllocationQuantitySnapshot After);

public sealed record StockAllocationMutationResult(
    string OperationKey,
    string MutationType,
    long ErpStockId,
    long? ErpStockRecordId,
    StockQuantitySnapshot StockBefore,
    StockQuantitySnapshot StockAfter,
    IReadOnlyList<StockAllocationMutationChange> AllocationChanges,
    bool IsReplay,
    long? SharedCommandId = null,
    long? ReservationId = null,
    long? ReservationItemId = null);

/// <summary>
/// Indicates that rollback to the mutation savepoint failed. The caller must
/// roll back the entire database transaction and must never attempt to commit it.
/// </summary>
public sealed class StockAllocationTransactionFatalException : InvalidOperationException
{
    public StockAllocationTransactionFatalException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
