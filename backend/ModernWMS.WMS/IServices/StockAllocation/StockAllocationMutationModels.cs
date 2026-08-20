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
    string Remark = "");

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
    bool IsReplay);

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
