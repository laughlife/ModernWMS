using System.Data;
using Dapper;
using ModernWMS.WMS.IServices.StockAllocation;

namespace ModernWMS.WMS.Services.StockAllocation;

/// <summary>
/// The single mutation owner for ERP balance and WMS allocation quantities.
/// It never opens, commits or rolls back the caller's transaction and never
/// writes the retired wms_stock/wms_stock_record tables.
/// Strong-lock order is runtime configuration, ERP stock, allocations by id,
/// then idempotency and audit rows.
/// </summary>
public sealed class StockAllocationMutationService : IStockAllocationMutationService
{
    private const string CanonicalMode = "CANONICAL_ERP";
    private const string SavepointName = "mwms_stock_allocation_mutation";

    /// <inheritdoc />
    public async Task PrelockReservationOwnersAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        IReadOnlyCollection<long> erpWarehouseIds,
        IReadOnlyCollection<StockReservationPrelockRequest> requests,
        CancellationToken cancellationToken = default)
    {
        ValidateConnectionAndTransaction(connection, transaction);
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0) return;
        var warehouseIds = ValidateIds(erpWarehouseIds, nameof(erpWarehouseIds));
            throw new InvalidOperationException("批量预锁包含跨租户预占来源");
        await LockRuntimeConfigsAsync(connection, transaction, warehouseIds, cancellationToken);
        foreach (var request in requests
                     .OrderBy(x => x.Context.Reservation?.ExistingReservationId ?? long.MaxValue)
                     .ThenBy(x => x.Context.Reservation?.SourceSystem, StringComparer.Ordinal)
                     .ThenBy(x => x.Context.Reservation?.ReservationBizType, StringComparer.Ordinal)
                     .ThenBy(x => x.Context.Reservation?.ReservationBizId)
                     .ThenBy(x => x.Context.Reservation?.ExistingReservationItemId ?? long.MaxValue)
                     .ThenBy(x => x.Context.Reservation?.SourceLineKey, StringComparer.Ordinal)
                     .ThenBy(x => x.ErpStockId))
            await StockReservationMutationCoordinator.LockOwnerAsync(
                connection, transaction, request.Context, request.ErpStockId,
                request.EventType, cancellationToken);
        await PrelockAsync(connection, transaction, warehouseIds,
            requests.Select(x => x.ErpStockId).Distinct().ToArray(),
            requests.Select(x => x.AllocationId).Distinct().ToArray(), cancellationToken);
    }

    /// <inheritdoc />
    public async Task PrelockAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        IReadOnlyCollection<long> erpWarehouseIds,
        IReadOnlyCollection<long> erpStockIds,
        IReadOnlyCollection<long> allocationIds,
        CancellationToken cancellationToken = default)
    {
        ValidateConnectionAndTransaction(connection, transaction);
        var warehouseIds = ValidateIds(erpWarehouseIds, nameof(erpWarehouseIds));
        var stockIds = ValidateIds(erpStockIds, nameof(erpStockIds));
        var requestedAllocationIds = ValidateIds(allocationIds, nameof(allocationIds), false);

        await LockRuntimeConfigsAsync(
            connection, transaction, warehouseIds, cancellationToken);

        var lockedStocks = (await connection.QueryAsync<StockRow>(new CommandDefinition(
            """
            SELECT `id` Id,`warehouse_id` WarehouseId,`freight_forwarder_id` FreightForwarderId,
                   `dept_id` DeptId,`order_user_id` OrderUserId,`commodity_id` CommodityId,
                   `commodity_sku` CommoditySku,`commodity_name` CommodityName,
                   `available_qty` AvailableQty,`occupied_qty` OccupiedQty,`total_qty` TotalQty
              FROM `trk_stock`
             WHERE `id` IN @stockIds AND `deleted`=b'0'
             ORDER BY `id` FOR UPDATE
            """,
            new { stockIds }, transaction, cancellationToken: cancellationToken))).AsList();
        if (lockedStocks.Count != stockIds.Length)
            throw new KeyNotFoundException("批量预锁期间ERP库存发生变化");
        var actualWarehouseIds = lockedStocks.Select(t => t.WarehouseId).Distinct().OrderBy(t => t).ToArray();
        if (!actualWarehouseIds.SequenceEqual(warehouseIds))
            throw new InvalidOperationException("批量预锁传入的ERP仓库与库存实际所属仓库不一致");

        if (requestedAllocationIds.Length == 0) return;
        var allocations = (await connection.QueryAsync<AllocationRow>(new CommandDefinition(
            """
            SELECT `id` Id,`erp_stock_id` ErpStockId,`allocated_qty` AllocatedQty,
                   `occupied_qty` OccupiedQty,`location_state` LocationState
              FROM `wms_erp_stock_allocation`
             WHERE `erp_stock_id` IN @stockIds
               AND `id` IN @allocationIds
             ORDER BY `erp_stock_id`,`id` FOR UPDATE
            """,
            new { stockIds, allocationIds = requestedAllocationIds },
            transaction,
            cancellationToken: cancellationToken))).AsList();
        if (allocations.Count != requestedAllocationIds.Length)
            throw new KeyNotFoundException("批量预锁包含不存在、跨租户或不属于指定ERP库存的位置分配");
        foreach (var stock in lockedStocks)
        {
            var ids = allocations.Where(t => t.ErpStockId == stock.Id).Select(t => t.Id).ToArray();
            if (ids.Length > 0)
                await EnsureAllocationReferencesAsync(
                    connection, transaction, stock, ids, cancellationToken);
        }
    }

    /// <inheritdoc />
    public Task<StockAllocationMutationResult> AdjustAvailableAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        StockMutationContext context,
        long erpStockId,
        long allocationId,
        long quantityDelta,
        CancellationToken cancellationToken = default)
    {
        if (quantityDelta == 0)
            throw new ArgumentOutOfRangeException(nameof(quantityDelta), "库存调整数量不能为零");

        return MutateAsync(connection, transaction, context, erpStockId, allocationId,
            MutationKind.Adjust, quantityDelta, cancellationToken);
    }

    /// <inheritdoc />
    public Task<StockAllocationMutationResult> ReserveAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        StockMutationContext context,
        long erpStockId,
        long allocationId,
        long quantity,
        CancellationToken cancellationToken = default)
    {
        EnsurePositive(quantity, nameof(quantity));
        return MutateAsync(connection, transaction, context, erpStockId, allocationId,
            MutationKind.Reserve, quantity, cancellationToken);
    }

    /// <inheritdoc />
    public Task<StockAllocationMutationResult> ReleaseAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        StockMutationContext context,
        long erpStockId,
        long allocationId,
        long quantity,
        CancellationToken cancellationToken = default)
    {
        EnsurePositive(quantity, nameof(quantity));
        return MutateAsync(connection, transaction, context, erpStockId, allocationId,
            MutationKind.Release, quantity, cancellationToken);
    }

    /// <inheritdoc />
    public Task<StockAllocationMutationResult> ShipLockedAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        StockMutationContext context,
        long erpStockId,
        long allocationId,
        long quantity,
        CancellationToken cancellationToken = default)
    {
        EnsurePositive(quantity, nameof(quantity));
        return MutateAsync(connection, transaction, context, erpStockId, allocationId,
            MutationKind.ShipLocked, quantity, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<StockAllocationMutationResult> MoveLocationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        StockMutationContext context,
        long erpStockId,
        long sourceAllocationId,
        long targetAllocationId,
        long quantity,
        CancellationToken cancellationToken = default)
    {
        ValidateInvocation(connection, transaction, context, erpStockId);
        EnsurePositive(quantity, nameof(quantity));
        if (sourceAllocationId <= 0) throw new ArgumentOutOfRangeException(nameof(sourceAllocationId));
        if (targetAllocationId <= 0) throw new ArgumentOutOfRangeException(nameof(targetAllocationId));
        if (sourceAllocationId == targetAllocationId)
            throw new ArgumentException("移出和移入分配不能相同", nameof(targetAllocationId));

        await CreateSavepointAsync(connection, transaction, cancellationToken);
        try
        {
            await EnsureRuntimeAllowsMutationAsync(
                connection, transaction, context.ErpWarehouseId, cancellationToken);
            var stock = await LockStockAsync(connection, transaction, erpStockId, cancellationToken);
            EnsureWarehouseUnchanged(erpStockId, context.ErpWarehouseId, stock.WarehouseId);

            // Runtime configuration is the first strong lock. Stock rows precede
            // allocation rows, and idempotency/audit rows are locked last.
            var allocations = await LockAllocationsAsync(
                connection,
                transaction,
                erpStockId,
                [sourceAllocationId, targetAllocationId],
                cancellationToken);
            var source = allocations.Single(t => t.Id == sourceAllocationId);
            var target = allocations.Single(t => t.Id == targetAllocationId);
            await EnsureAllocationReferencesAsync(
                connection,
                transaction,
                stock,
                [sourceAllocationId, targetAllocationId],
                cancellationToken);
            var operation = await LockInventoryOperationAsync(
                connection, transaction, context, cancellationToken);
            var existingLogs = await ReadOperationLogsAsync(
                connection, transaction, context, cancellationToken);

            if (operation != null)
            {
                EnsureOperationMatches(
                    operation,
                    context,
                    "MOVE_LOCATION",
                    erpStockId,
                    sourceAllocationId,
                    targetAllocationId,
                    quantity);
                EnsureOperationSucceeded(operation);
                if (operation.ErpStockRecordId != null)
                    throw IdempotencyConflict(context.OperationKey);
                var replay = BuildMoveReplay(context, stock, source, target, quantity, existingLogs);
                await EnsureConservationAsync(
                    connection, transaction, stock.Id, cancellationToken);
                await ReleaseSavepointAsync(connection, transaction);
                return replay;
            }
            if (existingLogs.Count > 0) throw IdempotencyConflict(context.OperationKey);
            await InsertInventoryOperationAsync(
                connection,
                transaction,
                context,
                "MOVE_LOCATION",
                erpStockId,
                sourceAllocationId,
                targetAllocationId,
                quantity,
                null,
                cancellationToken);

            EnsureMoveSourceUsable(source);
            EnsureMoveTargetUsable(target);
            if (source.AllocatedQty - source.OccupiedQty < quantity)
                throw new InvalidOperationException("移出库位可用分配数量不足");

            var now = DateTime.Now;
            var sourceAfter = new AllocationRow
            {
                Id = source.Id,
                ErpStockId = source.ErpStockId,
                AllocatedQty = checked(source.AllocatedQty - quantity),
                OccupiedQty = source.OccupiedQty,
                LocationState = source.AllocatedQty - quantity == 0 && source.OccupiedQty == 0
                    ? "RETIRED"
                    : source.LocationState
            };
            var targetAfter = new AllocationRow
            {
                Id = target.Id,
                ErpStockId = target.ErpStockId,
                AllocatedQty = checked(target.AllocatedQty + quantity),
                OccupiedQty = target.OccupiedQty,
                LocationState = target.LocationState
            };

            var changes = new[]
            {
                new PendingAllocationChange(source, sourceAfter, "MOVE_OUT", target.Id),
                new PendingAllocationChange(target, targetAfter, "MOVE_IN", source.Id)
            };
            foreach (var change in changes.OrderBy(t => t.Before.Id))
                await UpdateAllocationAsync(connection, transaction, context, change, now, cancellationToken);

            foreach (var change in changes.OrderBy(t => t.Before.Id))
                await InsertAllocationLogAsync(
                    connection, transaction, context, stock.Id, null, change, now, null,
                    cancellationToken);

            await EnsureConservationAsync(connection, transaction, stock.Id, cancellationToken);
            await CompleteInventoryOperationAsync(
                connection, transaction, context, null, now, cancellationToken);
            await ReleaseSavepointAsync(connection, transaction);
            return BuildResult(context, "MOVE_LOCATION", stock, stock, null, changes, false);
        }
        catch (Exception exception)
        {
            await RollbackSavepointAsync(connection, transaction, exception);
            throw;
        }
    }

    private static async Task<StockAllocationMutationResult> MutateAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        StockMutationContext context,
        long erpStockId,
        long allocationId,
        MutationKind kind,
        long quantity,
        CancellationToken cancellationToken)
    {
        ValidateInvocation(connection, transaction, context, erpStockId);
        if (allocationId <= 0) throw new ArgumentOutOfRangeException(nameof(allocationId));

        await CreateSavepointAsync(connection, transaction, cancellationToken);
        try
        {
            await EnsureRuntimeAllowsMutationAsync(
                connection, transaction, context.ErpWarehouseId, cancellationToken);
            var deltas = MutationDeltas.For(kind, quantity);
            StockReservationMutationCoordinator.LockedOwner? reservationOwner = null;
            if (StockReservationMutationCoordinator.RequiresReservation(deltas.EventType))
                reservationOwner = await StockReservationMutationCoordinator.LockOwnerAsync(
                    connection, transaction, context, erpStockId, deltas.EventType, cancellationToken);
            var stock = await LockStockAsync(connection, transaction, erpStockId, cancellationToken);
            EnsureWarehouseUnchanged(erpStockId, context.ErpWarehouseId, stock.WarehouseId);
            var allocation = (await LockAllocationsAsync(
                connection,
                transaction,
                erpStockId,
                [allocationId],
                cancellationToken)).Single();
            await EnsureAllocationReferencesAsync(
                connection, transaction, stock, [allocationId], cancellationToken);
            StockReservationMutationCoordinator.MutationState? reservationState = null;
            if (reservationOwner != null)
                reservationState = await StockReservationMutationCoordinator.BeginMutationAsync(
                    connection, transaction, context, reservationOwner, erpStockId,
                    allocationId, deltas.EventType, quantity, cancellationToken);
            var operation = await LockInventoryOperationAsync(
                connection, transaction, context, cancellationToken);
            var existingLogs = await ReadOperationLogsAsync(
                connection, transaction, context, cancellationToken);

            if (operation != null)
            {
                EnsureOperationMatches(
                    operation,
                    context,
                    deltas.EventType,
                    erpStockId,
                    allocationId,
                    null,
                    quantity);
                EnsureOperationSucceeded(operation);
                if (operation.ErpStockRecordId == null)
                    throw IdempotencyConflict(context.OperationKey);
                if (reservationState is { Command.IsReplay: false })
                    throw new InvalidOperationException("本地库存命令已成功但共享预占命令不是重放，已拒绝不一致状态");
                var replay = await BuildMutationReplayAsync(
                    connection,
                    transaction,
                    context,
                    stock,
                    allocation,
                    deltas,
                    existingLogs,
                    operation.ErpStockRecordId.Value,
                    cancellationToken);
                await EnsureConservationAsync(
                    connection, transaction, stock.Id, cancellationToken);
                await ReleaseSavepointAsync(connection, transaction);
                return replay with
                {
                    SharedCommandId = reservationState?.Command.CommandId,
                    ReservationId = reservationState?.Owner.ReservationId,
                    ReservationItemId = reservationState?.Owner.ReservationItemId
                };
            }
            if (reservationState is { Command.IsReplay: true })
                throw new InvalidOperationException("共享预占命令已成功但WMS本地执行结果缺失，禁止重复变更库存");
            if (existingLogs.Count > 0) throw IdempotencyConflict(context.OperationKey);
            await InsertInventoryOperationAsync(
                connection,
                transaction,
                context,
                deltas.EventType,
                erpStockId,
                allocationId,
                null,
                quantity,
                reservationState,
                cancellationToken);

            EnsureAllocationUsable(kind, allocation);
            var stockAfter = Apply(stock, deltas);
            var allocationAfter = Apply(allocation, deltas);
            StockBalanceInvariant.EnsureValid(
                stockAfter.AvailableQty,
                stockAfter.OccupiedQty,
                stockAfter.TotalQty,
                allocationAfter.AllocatedQty,
                allocationAfter.OccupiedQty);

            var now = DateTime.Now;
            await UpdateStockAsync(connection, transaction, context, stock, stockAfter, now, cancellationToken);
            var pendingChange = new PendingAllocationChange(
                allocation,
                allocationAfter,
                deltas.EventType,
                null);
            await UpdateAllocationAsync(connection, transaction, context, pendingChange, now, cancellationToken);
            var recordId = await InsertStockRecordAsync(
                connection, transaction, context, stock, stockAfter, deltas, now,
                reservationState, cancellationToken);
            await InsertAllocationLogAsync(
                connection, transaction, context, stock.Id, recordId, pendingChange, now,
                reservationState, cancellationToken);
            if (reservationState != null)
                await StockReservationMutationCoordinator.EnsureConservationAsync(
                    connection, transaction, stock.Id, allocationId,
                    reservationState, cancellationToken);
            await EnsureConservationAsync(connection, transaction, stock.Id, cancellationToken);
            if (reservationState != null)
                await StockReservationMutationCoordinator.CompleteAsync(
                    connection, transaction, context, reservationState, recordId, cancellationToken);
            await CompleteInventoryOperationAsync(
                connection, transaction, context, recordId, now, cancellationToken);

            await ReleaseSavepointAsync(connection, transaction);
            return BuildResult(
                context,
                deltas.EventType,
                stock,
                stockAfter,
                recordId,
                [pendingChange],
                false,
                reservationState);
        }
        catch (Exception exception)
        {
            await RollbackSavepointAsync(connection, transaction, exception);
            throw;
        }
    }

    private static void ValidateInvocation(
        IDbConnection connection,
        IDbTransaction transaction,
        StockMutationContext context,
        long erpStockId)
    {
        ValidateConnectionAndTransaction(connection, transaction);
        ArgumentNullException.ThrowIfNull(context);
        if (erpStockId <= 0) throw new ArgumentOutOfRangeException(nameof(erpStockId));
        if (context.ErpWarehouseId <= 0) throw new ArgumentOutOfRangeException(nameof(context.ErpWarehouseId));
        EnsureRequiredLength(context.OperationKey, 64, nameof(context.OperationKey));
        EnsureRequiredLength(context.BizType, 32, nameof(context.BizType));
        EnsureRequiredLength(context.Operator, 64, nameof(context.Operator));
        if (context.BizId <= 0) throw new ArgumentOutOfRangeException(nameof(context.BizId));
        if (context.BizItemId < 0) throw new ArgumentOutOfRangeException(nameof(context.BizItemId));
        if (context.OperatorId < 0) throw new ArgumentOutOfRangeException(nameof(context.OperatorId));
        if ((context.Remark ?? string.Empty).Length > 500)
            throw new ArgumentException("库存变更备注不能超过500个字符", nameof(context.Remark));
    }

    private static void ValidateConnectionAndTransaction(
        IDbConnection connection,
        IDbTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);
        if (connection.State != ConnectionState.Open)
            throw new InvalidOperationException("库存变更要求已打开的数据库连接");
        if (!ReferenceEquals(transaction.Connection, connection))
            throw new InvalidOperationException("库存变更连接与事务不属于同一数据库会话");
    }

    private static long[] ValidateIds(
        IReadOnlyCollection<long> ids,
        string parameterName,
        bool requireAny = true)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var result = ids.Distinct().OrderBy(t => t).ToArray();
        if (requireAny && result.Length == 0)
            throw new ArgumentException("至少需要一个ID", parameterName);
        if (result.Any(t => t <= 0))
            throw new ArgumentOutOfRangeException(parameterName, "ID必须大于零");
        return result;
    }

    private static void EnsureRequiredLength(string value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("值不能为空", parameterName);
        if (value.Length > maxLength)
            throw new ArgumentException($"值不能超过{maxLength}个字符", parameterName);
    }

    private static void EnsurePositive(long quantity, string parameterName)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(parameterName, "数量必须大于零");
    }

    private static async Task<InventoryOperationRow?> LockInventoryOperationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        StockMutationContext context,
        CancellationToken cancellationToken)
    {
        // A non-locking snapshot avoids taking an absent-key gap lock after stock
        // and allocation locks. If a concurrent row is not visible, the global
        // unique key arbitrates the later insert and the savepoint safely rejects it.
        var snapshot = await connection.QuerySingleOrDefaultAsync<InventoryOperationRow>(
            new CommandDefinition(
            """
            SELECT `id` Id,`operation_key` OperationKey,`biz_type` BizType,`biz_id` BizId,
                   `biz_item_id` BizItemId,`mutation_type` MutationType,
                   `erp_stock_id` ErpStockId,`allocation_id` AllocationId,
                   `counterpart_allocation_id` CounterpartAllocationId,`quantity` Quantity,
                   `result_status` ResultStatus,`erp_stock_record_id` ErpStockRecordId
              FROM `wms_inventory_operation`
             WHERE `operation_key`=@operationKey
            """,
            new { operationKey = context.OperationKey },
            transaction,
            cancellationToken: cancellationToken));
        if (snapshot == null) return null;
        return await connection.QuerySingleAsync<InventoryOperationRow>(new CommandDefinition(
            """
            SELECT `id` Id,`operation_key` OperationKey,`biz_type` BizType,`biz_id` BizId,
                   `biz_item_id` BizItemId,`mutation_type` MutationType,
                   `erp_stock_id` ErpStockId,`allocation_id` AllocationId,
                   `counterpart_allocation_id` CounterpartAllocationId,`quantity` Quantity,
                   `result_status` ResultStatus,`erp_stock_record_id` ErpStockRecordId
              FROM `wms_inventory_operation`
             WHERE `id`=@id
             FOR UPDATE
            """,
            new { snapshot.Id},
            transaction,
            cancellationToken: cancellationToken));
    }

    private static async Task InsertInventoryOperationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        StockMutationContext context,
        string mutationType,
        long erpStockId,
        long allocationId,
        long? counterpartAllocationId,
        long quantity,
        StockReservationMutationCoordinator.MutationState? reservationState,
        CancellationToken cancellationToken)
    {
        var now = DateTime.Now;
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO `wms_inventory_operation`
                (`operation_key`,`shared_command_id`,`reservation_id`,`reservation_item_id`,
                 `biz_type`,`biz_id`,`biz_item_id`,`mutation_type`,
                 `erp_stock_id`,`allocation_id`,`counterpart_allocation_id`,`quantity`,
                 `result_status`,`erp_stock_record_id`,`operator`,`create_time`,`update_time`)
            VALUES
                (@operationKey,@sharedCommandId,@reservationId,@reservationItemId,
                 @bizType,@bizId,@bizItemId,@mutationType,
                 @erpStockId,@allocationId,@counterpartAllocationId,@quantity,
                 'PENDING',NULL,@operatorName,@now,@now)
            """,
            new
            {
                operationKey = context.OperationKey,
                sharedCommandId = reservationState?.Command.CommandId,
                reservationId = reservationState?.Owner.ReservationId,
                reservationItemId = reservationState?.Owner.ReservationItemId,
                bizType = context.BizType,
                bizId = context.BizId,
                bizItemId = context.BizItemId,
                mutationType,
                erpStockId,
                allocationId,
                counterpartAllocationId,
                quantity,
                operatorName = ErpOperator(context.Operator),
                now
            }, transaction, cancellationToken: cancellationToken));
    }

    private static async Task CompleteInventoryOperationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        StockMutationContext context,
        long? erpStockRecordId,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE `wms_inventory_operation`
               SET `result_status`='SUCCEEDED',`erp_stock_record_id`=@erpStockRecordId,
                   `update_time`=@now
             WHERE `operation_key`=@operationKey
               AND `result_status`='PENDING'
            """,
            new
            {
                operationKey = context.OperationKey,
                erpStockRecordId,
                now
            }, transaction, cancellationToken: cancellationToken));
        if (affected != 1)
            throw new InvalidOperationException("库存幂等命令头状态发生变化");
    }

    private static void EnsureOperationMatches(
        InventoryOperationRow operation,
        StockMutationContext context,
        string mutationType,
        long erpStockId,
        long allocationId,
        long? counterpartAllocationId,
        long quantity)
    {
        if (operation.OperationKey != context.OperationKey
            || operation.BizType != context.BizType
            || operation.BizId != context.BizId
            || operation.BizItemId != context.BizItemId
            || operation.MutationType != mutationType
            || operation.ErpStockId != erpStockId
            || operation.AllocationId != allocationId
            || operation.CounterpartAllocationId != counterpartAllocationId
            || operation.Quantity != quantity)
            throw IdempotencyConflict(context.OperationKey);
    }

    private static void EnsureOperationSucceeded(InventoryOperationRow operation)
    {
        if (!string.Equals(operation.ResultStatus, "SUCCEEDED", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"幂等命令 {operation.OperationKey} 尚未成功完成，必须回滚或修复原事务后重试");
    }

    private static async Task<List<AllocationLogRow>> ReadOperationLogsAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        StockMutationContext context,
        CancellationToken cancellationToken) =>
        (await connection.QueryAsync<AllocationLogRow>(new CommandDefinition(
            """
            SELECT `operation_key` OperationKey,`biz_type` BizType,`biz_id` BizId,
                   `biz_item_id` BizItemId,`event_type` EventType,`erp_stock_id` ErpStockId,
                   `allocation_id` AllocationId,`counterpart_allocation_id` CounterpartAllocationId,
                   `erp_stock_record_id` ErpStockRecordId,`allocated_delta` AllocatedDelta,
                   `occupied_delta` OccupiedDelta,`before_allocated_qty` BeforeAllocatedQty,
                   `after_allocated_qty` AfterAllocatedQty,`before_occupied_qty` BeforeOccupiedQty,
                   `after_occupied_qty` AfterOccupiedQty
             FROM `wms_erp_stock_allocation_log`
             WHERE `operation_key`=@operationKey
             ORDER BY `allocation_id`,`event_type`
            """,
            new { operationKey = context.OperationKey },
            transaction,
            cancellationToken: cancellationToken))).AsList();

    private static void EnsureWarehouseUnchanged(
        long erpStockId,
        long expectedWarehouseId,
        long lockedWarehouseId)
    {
        if (expectedWarehouseId != lockedWarehouseId)
            throw new InvalidOperationException(
                $"ERP库存 {erpStockId} 的仓库在加锁期间发生变化，库存变更已拒绝");
    }

    private static async Task<StockRow> LockStockAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        long erpStockId,
        CancellationToken cancellationToken) =>
        await connection.QuerySingleOrDefaultAsync<StockRow>(new CommandDefinition(
            """
            SELECT `id` Id,`warehouse_id` WarehouseId,`freight_forwarder_id` FreightForwarderId,
                   `dept_id` DeptId,`order_user_id` OrderUserId,`commodity_id` CommodityId,
                   `commodity_sku` CommoditySku,`commodity_name` CommodityName,
                   `available_qty` AvailableQty,`occupied_qty` OccupiedQty,`total_qty` TotalQty
              FROM `trk_stock`
             WHERE `id`=@erpStockId AND `deleted`=b'0'
             FOR UPDATE
            """,
            new { erpStockId }, transaction, cancellationToken: cancellationToken))
        ?? throw new KeyNotFoundException($"ERP库存不存在或已删除：{erpStockId}");

    private static async Task EnsureRuntimeAllowsMutationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        long warehouseId,
        CancellationToken cancellationToken)
        => await LockRuntimeConfigsAsync(
            connection, transaction, [warehouseId], cancellationToken);

    /// <summary>
    /// Normal inventory transactions take shared runtime-gate locks so they do
    /// not serialize an entire warehouse. Maintenance/cutover code must acquire
    /// the same config rows with FOR UPDATE before changing the gate.
    /// </summary>
    private static async Task LockRuntimeConfigsAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        IReadOnlyCollection<long> warehouseIds,
        CancellationToken cancellationToken)
    {
        var ids = warehouseIds.Distinct().OrderBy(t => t).ToArray();
        var configs = (await connection.QueryAsync<RuntimeConfigRow>(new CommandDefinition(
            """
            SELECT `erp_warehouse_id` WarehouseId,`mode` Mode,
                   `maintenance_enabled` MaintenanceEnabled
              FROM `wms_inventory_runtime_config`
             WHERE `erp_warehouse_id` IN @ids
             ORDER BY `erp_warehouse_id`
             LOCK IN SHARE MODE
            """,
            new { ids }, transaction, cancellationToken: cancellationToken))).AsList();
        if (configs.Count != ids.Length)
            throw new InvalidOperationException("一个或多个ERP仓库尚未配置WMS统一库存模式");
        var maintenance = configs.FirstOrDefault(t => t.MaintenanceEnabled);
        if (maintenance != null)
            throw new InvalidOperationException(
                $"ERP仓库 {maintenance.WarehouseId} 正处于库存维护窗口，禁止库存变更");
        var legacy = configs.FirstOrDefault(
            t => !string.Equals(t.Mode, CanonicalMode, StringComparison.Ordinal));
        if (legacy != null)
            throw new InvalidOperationException(
                $"ERP仓库 {legacy.WarehouseId} 尚未切换为唯一ERP库存模式");
    }

    private static async Task<List<AllocationRow>> LockAllocationsAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        long erpStockId,
        IReadOnlyCollection<long> allocationIds,
        CancellationToken cancellationToken)
    {
        var ids = allocationIds.Distinct().OrderBy(t => t).ToArray();
        var rows = (await connection.QueryAsync<AllocationRow>(new CommandDefinition(
            """
            SELECT `id` Id,`erp_stock_id` ErpStockId,`allocated_qty` AllocatedQty,
                   `occupied_qty` OccupiedQty,`location_state` LocationState
              FROM `wms_erp_stock_allocation`
             WHERE `erp_stock_id`=@erpStockId AND `id` IN @ids
             ORDER BY `id` FOR UPDATE
            """,
            new { erpStockId, ids }, transaction, cancellationToken: cancellationToken))).AsList();
        if (rows.Count != ids.Length)
            throw new KeyNotFoundException("库存位置分配不存在、跨租户或不属于指定ERP库存");
        return rows;
    }

    private static async Task EnsureAllocationReferencesAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        StockRow stock,
        IReadOnlyCollection<long> allocationIds,
        CancellationToken cancellationToken)
    {
        var invalidCount = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            """
            SELECT COUNT(*)
              FROM `wms_erp_stock_allocation` allocation
             WHERE allocation.`erp_stock_id`=@erpStockId
               AND allocation.`id` IN @allocationIds
               AND
               (
                 NOT EXISTS
                 (
                   SELECT 1
                     FROM `wms_erp_goods_owner_map` owner_map
                    WHERE owner_map.`wms_goods_owner_id`=allocation.`goods_owner_id`
                      AND owner_map.`erp_dept_id` <=> @deptId
                      AND owner_map.`erp_order_user_id` <=> @orderUserId
                 )
                 OR
                 (
                   allocation.`location_state` IN ('ACTIVE','UNLOCATED')
                   AND allocation.`goods_location_id` IS NOT NULL
                   AND
                   (
                     allocation.`warehouse_area_id` IS NULL
                     OR NOT EXISTS
                     (
                       SELECT 1
                         FROM `wms_goodslocation` location
                         JOIN `wms_warehouse` warehouse
                           ON warehouse.`id`=location.`warehouse_id`

                        WHERE location.`id`=allocation.`goods_location_id`
                          AND location.`warehouse_area_id`=allocation.`warehouse_area_id`

                          AND location.`is_valid`=1
                          AND warehouse.`erp_warehouse_id`=@warehouseId
                     )
                   )
                 )
                 OR
                 (
                   allocation.`location_state` IN ('ACTIVE','UNLOCATED')
                   AND allocation.`goods_location_id` IS NULL
                   AND allocation.`warehouse_area_id` IS NOT NULL
                   AND NOT EXISTS
                   (
                     SELECT 1
                       FROM `wms_warehousearea` area
                       JOIN `wms_warehouse` warehouse
                         ON warehouse.`id`=area.`warehouse_id`

                      WHERE area.`id`=allocation.`warehouse_area_id`

                        AND area.`is_valid`=1
                        AND warehouse.`erp_warehouse_id`=@warehouseId
                   )
                 )
                 OR allocation.`location_state` NOT IN ('ACTIVE','UNLOCATED','RETIRED')
               )
            """,
            new
            {

                erpStockId = stock.Id,
                allocationIds,
                warehouseId = stock.WarehouseId,
                deptId = stock.DeptId,
                orderUserId = stock.OrderUserId
            }, transaction, cancellationToken: cancellationToken));
        if (invalidCount > 0)
            throw new InvalidOperationException(
                $"ERP库存 {stock.Id} 的库位仓库或库存所属人映射不一致");
    }

    private static void EnsureAllocationUsable(MutationKind kind, AllocationRow allocation)
    {
        if (string.Equals(allocation.LocationState, "RETIRED", StringComparison.Ordinal))
            throw new InvalidOperationException("已退役的位置分配不能执行库存变更");
        if (kind is MutationKind.Reserve or MutationKind.ShipLocked
            && !string.Equals(allocation.LocationState, "ACTIVE", StringComparison.Ordinal))
            throw new InvalidOperationException("待确认库位不可新增预占或出库");
        if (kind is MutationKind.Release
            && !string.Equals(allocation.LocationState, "ACTIVE", StringComparison.Ordinal)
            && !string.Equals(allocation.LocationState, "UNLOCATED", StringComparison.Ordinal))
            throw new InvalidOperationException("仅有效库位或待确认库位允许释放预占");
    }

    private static void EnsureMoveSourceUsable(AllocationRow allocation)
    {
        if (!string.Equals(allocation.LocationState, "ACTIVE", StringComparison.Ordinal)
            && !string.Equals(allocation.LocationState, "UNLOCATED", StringComparison.Ordinal))
            throw new InvalidOperationException("移出位置分配必须是有效库位或待确认库位");
    }

    private static void EnsureMoveTargetUsable(AllocationRow allocation)
    {
        if (!string.Equals(allocation.LocationState, "ACTIVE", StringComparison.Ordinal))
            throw new InvalidOperationException("移入目标必须是真实有效库位，禁止移入待确认库位");
    }

    private static StockRow Apply(StockRow before, MutationDeltas deltas) => new()
    {
        Id = before.Id,
        WarehouseId = before.WarehouseId,
        FreightForwarderId = before.FreightForwarderId,
        DeptId = before.DeptId,
        OrderUserId = before.OrderUserId,
        CommodityId = before.CommodityId,
        CommoditySku = before.CommoditySku,
        CommodityName = before.CommodityName,
        AvailableQty = checked(before.AvailableQty + deltas.AvailableDelta),
        OccupiedQty = checked(before.OccupiedQty + deltas.OccupiedDelta),
        TotalQty = checked(before.TotalQty + deltas.TotalDelta)
    };

    private static AllocationRow Apply(AllocationRow before, MutationDeltas deltas) => new()
    {
        Id = before.Id,
        ErpStockId = before.ErpStockId,
        LocationState = before.LocationState,
        AllocatedQty = checked(before.AllocatedQty + deltas.AllocatedDelta),
        OccupiedQty = checked(before.OccupiedQty + deltas.AllocationOccupiedDelta)
    };

    private static async Task UpdateStockAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        StockMutationContext context,
        StockRow before,
        StockRow after,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE `trk_stock`
               SET `available_qty`=@availableQty,`occupied_qty`=@occupiedQty,`total_qty`=@totalQty,
                   `updater`=@operatorName,`update_time`=@now
             WHERE `id`=@id AND `deleted`=b'0'
               AND `available_qty`=@beforeAvailableQty
               AND `occupied_qty`=@beforeOccupiedQty AND `total_qty`=@beforeTotalQty
            """,
            new
            {
                id = before.Id,
                availableQty = after.AvailableQty,
                occupiedQty = after.OccupiedQty,
                totalQty = after.TotalQty,
                operatorName = ErpOperator(context.Operator),
                now,
                beforeAvailableQty = before.AvailableQty,
                beforeOccupiedQty = before.OccupiedQty,
                beforeTotalQty = before.TotalQty
            }, transaction, cancellationToken: cancellationToken));
        if (affected != 1) throw new InvalidOperationException("ERP库存已被并发修改");
    }

    private static async Task UpdateAllocationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        StockMutationContext context,
        PendingAllocationChange change,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE `wms_erp_stock_allocation`
               SET `allocated_qty`=@allocatedQty,`occupied_qty`=@occupiedQty,
                   `location_state`=@afterLocationState,
                   `row_version`=`row_version`+1,`updater`=@operatorName,`update_time`=@now
             WHERE `id`=@id
               AND `allocated_qty`=@beforeAllocatedQty AND `occupied_qty`=@beforeOccupiedQty
               AND `location_state`=@beforeLocationState
            """,
            new
            {
                id = change.Before.Id,
                allocatedQty = change.After.AllocatedQty,
                occupiedQty = change.After.OccupiedQty,
                afterLocationState = change.After.LocationState,
                operatorName = context.Operator,
                now,
                beforeAllocatedQty = change.Before.AllocatedQty,
                beforeOccupiedQty = change.Before.OccupiedQty,
                beforeLocationState = change.Before.LocationState
            }, transaction, cancellationToken: cancellationToken));
        if (affected != 1) throw new InvalidOperationException("库存位置分配已被并发修改");
    }

    private static async Task<long> InsertStockRecordAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        StockMutationContext context,
        StockRow before,
        StockRow after,
        MutationDeltas deltas,
        DateTime now,
        StockReservationMutationCoordinator.MutationState? reservationState,
        CancellationToken cancellationToken)
    {
        var bizNo = $"{context.BizType}-{context.BizId}";
        if (bizNo.Length > 64) bizNo = bizNo[..64];
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO `trk_stock_record`
                (`record_no`,`biz_type`,`biz_id`,`biz_item_id`,`biz_no`,`stock_id`,
                 `freight_forwarder_id`,`warehouse_id`,`dept_id`,`order_user_id`,`commodity_id`,
                 `commodity_sku`,`commodity_name`,`change_qty`,`before_qty`,`after_qty`,`direction`,
                 `operate_time`,`operator_id`,`operator_name`,`remark`,`creator`,`create_time`,
                 `updater`,`update_time`,`deleted`,`operation_key`,`available_change_qty`,
                 `occupied_change_qty`,`total_change_qty`,`before_available_qty`,
                 `after_available_qty`,`before_occupied_qty`,`after_occupied_qty`,
                 `before_total_qty`,`after_total_qty`,`reservation_command_id`,`reservation_id`,
                 `reservation_item_id`,`reservation_action`)
            VALUES
                (@recordNo,@recordType,@bizId,@bizItemId,@bizNo,@stockId,
                 @forwarderId,@warehouseId,@deptId,@orderUserId,@commodityId,
                 @sku,@commodityName,@totalDelta,@beforeTotal,@afterTotal,@direction,
                 @now,@operatorId,@operatorName,@remark,@operatorName,@now,
                 @operatorName,@now,b'0',@operationKey,@availableDelta,
                 @occupiedDelta,@totalDelta,@beforeAvailable,@afterAvailable,
                 @beforeOccupied,@afterOccupied,@beforeTotal,@afterTotal,
                 @reservationCommandId,@reservationId,@reservationItemId,@reservationAction)
            """,
            new
            {
                recordNo = context.OperationKey,
                recordType = context.BizType,
                bizId = context.BizId,
                bizItemId = context.BizItemId,
                bizNo,
                stockId = before.Id,
                forwarderId = before.FreightForwarderId,
                warehouseId = before.WarehouseId,
                deptId = before.DeptId,
                orderUserId = before.OrderUserId,
                commodityId = before.CommodityId,
                sku = before.CommoditySku,
                commodityName = before.CommodityName,
                totalDelta = deltas.TotalDelta,
                beforeTotal = before.TotalQty,
                afterTotal = after.TotalQty,
                direction = deltas.Direction,
                now,
                operatorId = context.OperatorId,
                operatorName = ErpOperator(context.Operator),
                remark = context.Remark ?? string.Empty,
                operationKey = context.OperationKey,
                availableDelta = deltas.AvailableDelta,
                occupiedDelta = deltas.OccupiedDelta,
                beforeAvailable = before.AvailableQty,
                afterAvailable = after.AvailableQty,
                beforeOccupied = before.OccupiedQty,
                afterOccupied = after.OccupiedQty,
                reservationCommandId = reservationState?.Command.CommandId,
                reservationId = reservationState?.Owner.ReservationId,
                reservationItemId = reservationState?.Owner.ReservationItemId,
                reservationAction = reservationState?.Command.Action
            }, transaction, cancellationToken: cancellationToken));
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT LAST_INSERT_ID();", transaction: transaction, cancellationToken: cancellationToken));
    }

    private static async Task InsertAllocationLogAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        StockMutationContext context,
        long erpStockId,
        long? erpStockRecordId,
        PendingAllocationChange change,
        DateTime now,
        StockReservationMutationCoordinator.MutationState? reservationState,
        CancellationToken cancellationToken) =>
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO `wms_erp_stock_allocation_log`
                (`operation_key`,`shared_command_id`,`reservation_id`,`reservation_item_id`,
                 `biz_type`,`biz_id`,`biz_item_id`,`event_type`,
                 `erp_stock_id`,`allocation_id`,`counterpart_allocation_id`,`erp_stock_record_id`,
                 `allocated_delta`,`occupied_delta`,`before_allocated_qty`,`after_allocated_qty`,
                 `before_occupied_qty`,`after_occupied_qty`,`operator`,`operate_time`,`remark`)
            VALUES
                (@operationKey,@sharedCommandId,@reservationId,@reservationItemId,
                 @bizType,@bizId,@bizItemId,@eventType,
                 @erpStockId,@allocationId,@counterpartAllocationId,@erpStockRecordId,
                 @allocatedDelta,@occupiedDelta,@beforeAllocated,@afterAllocated,
                 @beforeOccupied,@afterOccupied,@operatorName,@now,@remark)
            """,
            new
            {
                operationKey = context.OperationKey,
                sharedCommandId = reservationState?.Command.CommandId,
                reservationId = reservationState?.Owner.ReservationId,
                reservationItemId = reservationState?.Owner.ReservationItemId,
                bizType = context.BizType,
                bizId = context.BizId,
                bizItemId = context.BizItemId,
                eventType = change.EventType,
                erpStockId,
                allocationId = change.Before.Id,
                counterpartAllocationId = change.CounterpartAllocationId,
                erpStockRecordId,
                allocatedDelta = change.After.AllocatedQty - change.Before.AllocatedQty,
                occupiedDelta = change.After.OccupiedQty - change.Before.OccupiedQty,
                beforeAllocated = change.Before.AllocatedQty,
                afterAllocated = change.After.AllocatedQty,
                beforeOccupied = change.Before.OccupiedQty,
                afterOccupied = change.After.OccupiedQty,
                operatorName = ErpOperator(context.Operator),
                now,
                remark = context.Remark ?? string.Empty
            }, transaction, cancellationToken: cancellationToken));

    private static async Task EnsureConservationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        long erpStockId,
        CancellationToken cancellationToken)
    {
        var state = await connection.QuerySingleAsync<ConservationRow>(new CommandDefinition(
            """
            SELECT s.`available_qty` AvailableQty,s.`occupied_qty` OccupiedQty,s.`total_qty` TotalQty,
                   COALESCE(SUM(CASE WHEN a.`location_state`<>'RETIRED' THEN a.`allocated_qty` ELSE 0 END),0) AllocatedQty,
                   COALESCE(SUM(CASE WHEN a.`location_state`<>'RETIRED' THEN a.`occupied_qty` ELSE 0 END),0) AllocationOccupiedQty
              FROM `trk_stock` s
              LEFT JOIN `wms_erp_stock_allocation` a
                ON a.`erp_stock_id`=s.`id`
             WHERE s.`id`=@erpStockId AND s.`deleted`=b'0'
             GROUP BY s.`id`,s.`available_qty`,s.`occupied_qty`,s.`total_qty`
            """,
            new { erpStockId }, transaction, cancellationToken: cancellationToken));
        if (state.TotalQty != state.AllocatedQty
            || state.OccupiedQty != state.AllocationOccupiedQty
            || state.AvailableQty != checked(state.AllocatedQty - state.AllocationOccupiedQty))
            throw new InvalidOperationException(
                $"ERP库存 {erpStockId} 与WMS位置分配不守恒，库存变更已拒绝");
    }

    private static async Task<StockAllocationMutationResult> BuildMutationReplayAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        StockMutationContext context,
        StockRow currentStock,
        AllocationRow currentAllocation,
        MutationDeltas expected,
        IReadOnlyList<AllocationLogRow> logs,
        long expectedRecordId,
        CancellationToken cancellationToken)
    {
        if (logs.Count != 1) throw IdempotencyConflict(context.OperationKey);
        var log = logs[0];
        EnsureSameBusiness(context, currentStock.Id, log);
        if (log.EventType != expected.EventType
            || log.AllocationId != currentAllocation.Id
            || log.CounterpartAllocationId != null
            || log.AllocatedDelta != expected.AllocatedDelta
            || log.OccupiedDelta != expected.AllocationOccupiedDelta
            || !IsLogInternallyConsistent(log)
            || log.ErpStockRecordId != expectedRecordId)
            throw IdempotencyConflict(context.OperationKey);

        var record = await connection.QuerySingleOrDefaultAsync<StockRecordRow>(new CommandDefinition(
            """
            SELECT `id` Id,`operation_key` OperationKey,`biz_type` BizType,
                   `available_change_qty` AvailableDelta,`occupied_change_qty` OccupiedDelta,
                   `total_change_qty` TotalDelta,`before_available_qty` BeforeAvailableQty,
                   `after_available_qty` AfterAvailableQty,`before_occupied_qty` BeforeOccupiedQty,
                   `after_occupied_qty` AfterOccupiedQty,`before_total_qty` BeforeTotalQty,
                   `after_total_qty` AfterTotalQty
              FROM `trk_stock_record`
             WHERE `id`=@id AND `stock_id`=@stockId AND `deleted`=b'0'
             FOR UPDATE
            """,
            new { id = log.ErpStockRecordId.Value, stockId = currentStock.Id },
            transaction,
            cancellationToken: cancellationToken));
        if (record == null
            || record.OperationKey != context.OperationKey
            || record.BizType != context.BizType
            || record.AvailableDelta != expected.AvailableDelta
            || record.OccupiedDelta != expected.OccupiedDelta
            || record.TotalDelta != expected.TotalDelta
            || !IsRecordInternallyConsistent(record))
            throw IdempotencyConflict(context.OperationKey);

        var beforeStock = currentStock.WithQuantities(
            record.BeforeAvailableQty, record.BeforeOccupiedQty, record.BeforeTotalQty);
        var afterStock = currentStock.WithQuantities(
            record.AfterAvailableQty, record.AfterOccupiedQty, record.AfterTotalQty);
        var change = PendingAllocationChange.FromLog(currentAllocation, log);
        return BuildResult(context, expected.EventType, beforeStock, afterStock, record.Id, [change], true);
    }

    private static StockAllocationMutationResult BuildMoveReplay(
        StockMutationContext context,
        StockRow stock,
        AllocationRow source,
        AllocationRow target,
        long quantity,
        IReadOnlyList<AllocationLogRow> logs)
    {
        if (logs.Count != 2) throw IdempotencyConflict(context.OperationKey);
        foreach (var log in logs) EnsureSameBusiness(context, stock.Id, log);
        var outLog = logs.SingleOrDefault(t => t.EventType == "MOVE_OUT");
        var inLog = logs.SingleOrDefault(t => t.EventType == "MOVE_IN");
        if (outLog == null || inLog == null
            || outLog.AllocationId != source.Id || outLog.CounterpartAllocationId != target.Id
            || inLog.AllocationId != target.Id || inLog.CounterpartAllocationId != source.Id
            || outLog.ErpStockRecordId != null || inLog.ErpStockRecordId != null
            || outLog.AllocatedDelta != -quantity || inLog.AllocatedDelta != quantity
            || outLog.OccupiedDelta != 0 || inLog.OccupiedDelta != 0
            || !IsLogInternallyConsistent(outLog) || !IsLogInternallyConsistent(inLog))
            throw IdempotencyConflict(context.OperationKey);
        var changes = new[]
        {
            PendingAllocationChange.FromLog(source, outLog),
            PendingAllocationChange.FromLog(target, inLog)
        };
        return BuildResult(context, "MOVE_LOCATION", stock, stock, null, changes, true);
    }

    private static void EnsureSameBusiness(
        StockMutationContext context,
        long erpStockId,
        AllocationLogRow log)
    {
        if (log.OperationKey != context.OperationKey
            || log.BizType != context.BizType
            || log.BizId != context.BizId
            || log.BizItemId != context.BizItemId
            || log.ErpStockId != erpStockId)
            throw IdempotencyConflict(context.OperationKey);
    }

    private static InvalidOperationException IdempotencyConflict(string operationKey) =>
        new($"幂等键 {operationKey} 已被不同的库存命令使用，已拒绝重复变更");

    private static string ErpOperator(string value) =>
        value.Length <= 64 ? value : value[..64];

    private static bool IsLogInternallyConsistent(AllocationLogRow log)
    {
        try
        {
            return checked(log.BeforeAllocatedQty + log.AllocatedDelta) == log.AfterAllocatedQty
                && checked(log.BeforeOccupiedQty + log.OccupiedDelta) == log.AfterOccupiedQty;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static bool IsRecordInternallyConsistent(StockRecordRow record)
    {
        try
        {
            return checked(record.BeforeAvailableQty + record.AvailableDelta) == record.AfterAvailableQty
                && checked(record.BeforeOccupiedQty + record.OccupiedDelta) == record.AfterOccupiedQty
                && checked(record.BeforeTotalQty + record.TotalDelta) == record.AfterTotalQty
                && record.BeforeTotalQty == checked(record.BeforeAvailableQty + record.BeforeOccupiedQty)
                && record.AfterTotalQty == checked(record.AfterAvailableQty + record.AfterOccupiedQty);
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static StockAllocationMutationResult BuildResult(
        StockMutationContext context,
        string mutationType,
        StockRow beforeStock,
        StockRow afterStock,
        long? recordId,
        IEnumerable<PendingAllocationChange> changes,
        bool isReplay,
        StockReservationMutationCoordinator.MutationState? reservationState = null) =>
        new(
            context.OperationKey,
            mutationType,
            beforeStock.Id,
            recordId,
            new StockQuantitySnapshot(
                beforeStock.AvailableQty, beforeStock.OccupiedQty, beforeStock.TotalQty),
            new StockQuantitySnapshot(
                afterStock.AvailableQty, afterStock.OccupiedQty, afterStock.TotalQty),
            changes.OrderBy(t => t.Before.Id).Select(t => new StockAllocationMutationChange(
                t.Before.Id,
                t.EventType,
                t.CounterpartAllocationId,
                new StockAllocationQuantitySnapshot(t.Before.AllocatedQty, t.Before.OccupiedQty),
                new StockAllocationQuantitySnapshot(t.After.AllocatedQty, t.After.OccupiedQty))).ToArray(),
            isReplay,
            reservationState?.Command.CommandId,
            reservationState?.Owner.ReservationId,
            reservationState?.Owner.ReservationItemId);

    private static Task CreateSavepointAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            $"SAVEPOINT `{SavepointName}`;", transaction: transaction, cancellationToken: cancellationToken));

    private static Task ReleaseSavepointAsync(IDbConnection connection, IDbTransaction transaction) =>
        connection.ExecuteAsync($"RELEASE SAVEPOINT `{SavepointName}`;", transaction: transaction);

    private static async Task RollbackSavepointAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        Exception originalException)
    {
        try
        {
            await connection.ExecuteAsync($"ROLLBACK TO SAVEPOINT `{SavepointName}`;", transaction: transaction);
            await connection.ExecuteAsync($"RELEASE SAVEPOINT `{SavepointName}`;", transaction: transaction);
        }
        catch (Exception rollbackException)
        {
            throw new StockAllocationTransactionFatalException(
                "库存变更保存点回滚失败；调用方必须立即回滚整个数据库事务，严禁提交",
                new AggregateException(originalException, rollbackException));
        }
    }

    private enum MutationKind
    {
        Adjust,
        Reserve,
        Release,
        ShipLocked
    }

    private sealed record MutationDeltas(
        string EventType,
        string Direction,
        long AvailableDelta,
        long OccupiedDelta,
        long TotalDelta,
        long AllocatedDelta,
        long AllocationOccupiedDelta)
    {
        public static MutationDeltas For(MutationKind kind, long quantity) => kind switch
        {
            MutationKind.Adjust => new("ADJUST", quantity < 0 ? "OUT" : "IN", quantity, 0, quantity, quantity, 0),
            MutationKind.Reserve => new("LOCK", "OUT", -quantity, quantity, 0, 0, quantity),
            MutationKind.Release => new("UNLOCK", "IN", quantity, -quantity, 0, 0, -quantity),
            MutationKind.ShipLocked => new("SHIP_OUT", "OUT", 0, -quantity, -quantity, -quantity, -quantity),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private sealed class StockRow
    {
        public long Id { get; init; }
        public long WarehouseId { get; init; }
        public long? FreightForwarderId { get; init; }
        public long? DeptId { get; init; }
        public long? OrderUserId { get; init; }
        public long? CommodityId { get; init; }
        public string? CommoditySku { get; init; }
        public string? CommodityName { get; init; }
        public long AvailableQty { get; init; }
        public long OccupiedQty { get; init; }
        public long TotalQty { get; init; }

        public StockRow WithQuantities(long available, long occupied, long total) => new()
        {
            Id = Id,
            WarehouseId = WarehouseId,
            FreightForwarderId = FreightForwarderId,
            DeptId = DeptId,
            OrderUserId = OrderUserId,
            CommodityId = CommodityId,
            CommoditySku = CommoditySku,
            CommodityName = CommodityName,
            AvailableQty = available,
            OccupiedQty = occupied,
            TotalQty = total
        };
    }

    private sealed class AllocationRow
    {
        public long Id { get; init; }
        public long ErpStockId { get; init; }
        public long AllocatedQty { get; init; }
        public long OccupiedQty { get; init; }
        public string LocationState { get; init; } = string.Empty;
    }

    private sealed class RuntimeConfigRow
    {
        public long WarehouseId { get; init; }
        public string Mode { get; init; } = string.Empty;
        public bool MaintenanceEnabled { get; init; }
    }

    private sealed class InventoryOperationRow
    {
        public long Id { get; init; }
        public string OperationKey { get; init; } = string.Empty;
        public string BizType { get; init; } = string.Empty;
        public long BizId { get; init; }
        public long BizItemId { get; init; }
        public string MutationType { get; init; } = string.Empty;
        public long ErpStockId { get; init; }
        public long AllocationId { get; init; }
        public long? CounterpartAllocationId { get; init; }
        public long Quantity { get; init; }
        public string ResultStatus { get; init; } = string.Empty;
        public long? ErpStockRecordId { get; init; }
    }

    private sealed class AllocationLogRow
    {
        public string OperationKey { get; init; } = string.Empty;
        public string BizType { get; init; } = string.Empty;
        public long BizId { get; init; }
        public long BizItemId { get; init; }
        public string EventType { get; init; } = string.Empty;
        public long ErpStockId { get; init; }
        public long AllocationId { get; init; }
        public long? CounterpartAllocationId { get; init; }
        public long? ErpStockRecordId { get; init; }
        public long AllocatedDelta { get; init; }
        public long OccupiedDelta { get; init; }
        public long BeforeAllocatedQty { get; init; }
        public long AfterAllocatedQty { get; init; }
        public long BeforeOccupiedQty { get; init; }
        public long AfterOccupiedQty { get; init; }
    }

    private sealed class StockRecordRow
    {
        public long Id { get; init; }
        public string OperationKey { get; init; } = string.Empty;
        public string BizType { get; init; } = string.Empty;
        public long AvailableDelta { get; init; }
        public long OccupiedDelta { get; init; }
        public long TotalDelta { get; init; }
        public long BeforeAvailableQty { get; init; }
        public long AfterAvailableQty { get; init; }
        public long BeforeOccupiedQty { get; init; }
        public long AfterOccupiedQty { get; init; }
        public long BeforeTotalQty { get; init; }
        public long AfterTotalQty { get; init; }
    }

    private sealed class ConservationRow
    {
        public long AvailableQty { get; init; }
        public long OccupiedQty { get; init; }
        public long TotalQty { get; init; }
        public long AllocatedQty { get; init; }
        public long AllocationOccupiedQty { get; init; }
    }

    private sealed record PendingAllocationChange(
        AllocationRow Before,
        AllocationRow After,
        string EventType,
        long? CounterpartAllocationId)
    {
        public static PendingAllocationChange FromLog(AllocationRow current, AllocationLogRow log) => new(
            new AllocationRow
            {
                Id = current.Id,
                ErpStockId = current.ErpStockId,
                LocationState = current.LocationState,
                AllocatedQty = log.BeforeAllocatedQty,
                OccupiedQty = log.BeforeOccupiedQty
            },
            new AllocationRow
            {
                Id = current.Id,
                ErpStockId = current.ErpStockId,
                LocationState = current.LocationState,
                AllocatedQty = log.AfterAllocatedQty,
                OccupiedQty = log.AfterOccupiedQty
            },
            log.EventType,
            log.CounterpartAllocationId);
    }
}
