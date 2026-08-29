using System.Data;
using Dapper;
using ModernWMS.WMS.IServices.StockAllocation;

namespace ModernWMS.WMS.Services.StockAllocation;

/// <summary>
/// Owns packing mutations against <c>trk_stock</c>. It never reads or writes WMS
/// warehouse, area, location, allocation, owner-map or SKU-map tables.
/// </summary>
public sealed class PackingStockMutationService : IPackingStockMutationService
{
    private const string SavepointName = "mwms_packing_stock_mutation";

    /// <inheritdoc />
    public async Task PrelockAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        IReadOnlyCollection<long> erpWarehouseIds,
        IReadOnlyCollection<PackingStockPrelockRequest> requests,
        CancellationToken cancellationToken = default)
    {
        ValidateConnectionAndTransaction(connection, transaction);
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0) return;
        var validated = PackingStockPrelockPolicy.Validate(
            erpWarehouseIds,
            requests.Select(request =>
                new PackingStockPrelockIdentity(request.Context.ErpWarehouseId, request.ErpStockId))
                .ToArray());

        foreach (var request in requests
                     .OrderBy(request => request.Context.Reservation?.ExistingReservationId ?? long.MaxValue)
                     .ThenBy(request => request.Context.Reservation?.ExistingReservationItemId ?? long.MaxValue)
                     .ThenBy(request => request.ErpStockId))
        {
            await StockReservationMutationCoordinator.LockOwnerAsync(
                connection, transaction, request.Context, request.ErpStockId,
                request.EventType, cancellationToken);
        }

        var rows = (await connection.QueryAsync<StockWarehouseRow>(new CommandDefinition(
            """
            SELECT `id` Id,`warehouse_id` WarehouseId
              FROM `trk_stock`
             WHERE `id` IN @stockIds AND `deleted`=b'0'
             ORDER BY `id` FOR UPDATE
            """,
            new { stockIds = validated.StockIds }, transaction,
            cancellationToken: cancellationToken))).AsList();
        if (rows.Count != validated.StockIds.Count)
            throw new KeyNotFoundException("预锁期间ERP库存不存在或已删除");
        var actualWarehouseIds = rows.Select(row => row.WarehouseId).Distinct().Order().ToArray();
        if (!actualWarehouseIds.SequenceEqual(validated.WarehouseIds))
            throw new InvalidOperationException("预锁库存实际所属仓库与请求仓库不一致");
    }

    /// <inheritdoc />
    public Task<PackingStockMutationResult> ReserveAsync(
        IDbConnection connection, IDbTransaction transaction, StockMutationContext context,
        long erpStockId, long quantity, CancellationToken cancellationToken = default) =>
        MutateAsync(connection, transaction, context, erpStockId, "LOCK", quantity, cancellationToken);

    /// <inheritdoc />
    public Task<PackingStockMutationResult> ReleaseAsync(
        IDbConnection connection, IDbTransaction transaction, StockMutationContext context,
        long erpStockId, long quantity, CancellationToken cancellationToken = default) =>
        MutateAsync(connection, transaction, context, erpStockId, "UNLOCK", quantity, cancellationToken);

    /// <inheritdoc />
    public Task<PackingStockMutationResult> ShipLockedAsync(
        IDbConnection connection, IDbTransaction transaction, StockMutationContext context,
        long erpStockId, long quantity, CancellationToken cancellationToken = default) =>
        MutateAsync(connection, transaction, context, erpStockId, "SHIP_OUT", quantity, cancellationToken);

    /// <inheritdoc />
    public Task<PackingStockMutationResult> AdjustAvailableAsync(
        IDbConnection connection, IDbTransaction transaction, StockMutationContext context,
        long erpStockId, long quantityDelta, CancellationToken cancellationToken = default) =>
        MutateAsync(connection, transaction, context, erpStockId, "ADJUST", quantityDelta, cancellationToken);

    private static async Task<PackingStockMutationResult> MutateAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        StockMutationContext context,
        long erpStockId,
        string eventType,
        long quantity,
        CancellationToken cancellationToken)
    {
        ValidateInvocation(connection, transaction, context, erpStockId);
        if (eventType == "ADJUST" ? quantity == 0 : quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity), "库存变更数量无效");

        await ExecuteAsync(connection, transaction, $"SAVEPOINT `{SavepointName}`;", cancellationToken);
        try
        {
            StockReservationMutationCoordinator.LockedOwner? owner = null;
            if (StockReservationMutationCoordinator.RequiresReservation(eventType))
            {
                owner = await StockReservationMutationCoordinator.LockOwnerAsync(
                    connection, transaction, context, erpStockId, eventType, cancellationToken);
            }

            var stock = await LockStockAsync(connection, transaction, erpStockId, cancellationToken);
            if (stock.WarehouseId != context.ErpWarehouseId)
                throw new InvalidOperationException("ERP库存实际所属仓库与业务仓库不一致");

            StockReservationMutationCoordinator.MutationState? reservationState = null;
            if (owner != null)
            {
                reservationState = await StockReservationMutationCoordinator.BeginMutationAsync(
                    connection, transaction, context, owner, erpStockId, null,
                    eventType, quantity, cancellationToken);
            }

            var existing = await LoadRecordAsync(
                connection, transaction, context.OperationKey, cancellationToken);
            if (existing != null)
            {
                if (existing.StockId != erpStockId)
                    throw new InvalidOperationException("库存操作键已被其他ERP库存使用");
                if (reservationState is { Command.IsReplay: false })
                    throw new InvalidOperationException("库存流水已存在但共享预占命令不是重放");
                await ExecuteAsync(connection, transaction,
                    $"RELEASE SAVEPOINT `{SavepointName}`;", cancellationToken);
                return FromRecord(context.OperationKey, eventType, existing, reservationState);
            }
            if (reservationState is { Command.IsReplay: true })
                throw new InvalidOperationException("共享预占命令已成功但库存流水缺失，禁止重复变更库存");

            var before = stock.Quantities;
            var after = PackingStockMutationPolicy.Apply(eventType, quantity, before);
            var now = DateTime.Now;
            var affected = await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE `trk_stock`
                   SET `available_qty`=@availableQty,`occupied_qty`=@occupiedQty,
                       `total_qty`=@totalQty,`updater`=@operatorName,`update_time`=@now
                 WHERE `id`=@id AND `deleted`=b'0'
                   AND `available_qty`=@beforeAvailable
                   AND `occupied_qty`=@beforeOccupied
                   AND `total_qty`=@beforeTotal
                """,
                new
                {
                    id = stock.Id,
                    availableQty = after.AvailableQty,
                    occupiedQty = after.OccupiedQty,
                    totalQty = after.TotalQty,
                    operatorName = context.Operator,
                    now,
                    beforeAvailable = before.AvailableQty,
                    beforeOccupied = before.OccupiedQty,
                    beforeTotal = before.TotalQty
                }, transaction, cancellationToken: cancellationToken));
            if (affected != 1)
                throw new InvalidOperationException("ERP库存已被并发修改");

            var recordId = await InsertRecordAsync(
                connection, transaction, context, stock, before, after,
                eventType, now, reservationState, cancellationToken);
            if (reservationState != null)
            {
                await StockReservationMutationCoordinator.CompleteAsync(
                    connection, transaction, context, reservationState, recordId, cancellationToken);
            }
            await ExecuteAsync(connection, transaction,
                $"RELEASE SAVEPOINT `{SavepointName}`;", cancellationToken);
            return new PackingStockMutationResult(
                context.OperationKey, eventType, stock.Id, recordId, before, after, false,
                reservationState?.Command.CommandId,
                reservationState?.Owner.ReservationId,
                reservationState?.Owner.ReservationItemId);
        }
        catch (Exception exception)
        {
            try
            {
                await ExecuteAsync(connection, transaction,
                    $"ROLLBACK TO SAVEPOINT `{SavepointName}`;", cancellationToken);
                await ExecuteAsync(connection, transaction,
                    $"RELEASE SAVEPOINT `{SavepointName}`;", cancellationToken);
            }
            catch (Exception rollbackException)
            {
                throw new StockAllocationTransactionFatalException(
                    "装箱库存变更保存点回滚失败；调用方必须回滚整个事务",
                    new AggregateException(exception, rollbackException));
            }
            throw;
        }
    }

    private static async Task<StockRow> LockStockAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        long stockId,
        CancellationToken cancellationToken) =>
        await connection.QuerySingleOrDefaultAsync<StockRow>(new CommandDefinition(
            """
            SELECT `id` Id,`warehouse_id` WarehouseId,`freight_forwarder_id` FreightForwarderId,
                   `dept_id` DeptId,`order_user_id` OrderUserId,`commodity_id` CommodityId,
                   `commodity_sku` CommoditySku,`commodity_name` CommodityName,
                   `available_qty` AvailableQty,`occupied_qty` OccupiedQty,`total_qty` TotalQty
              FROM `trk_stock`
             WHERE `id`=@stockId AND `deleted`=b'0'
             FOR UPDATE
            """,
            new { stockId }, transaction, cancellationToken: cancellationToken))
        ?? throw new KeyNotFoundException($"ERP库存不存在或已删除：{stockId}");

    private static Task<StockRecordRow?> LoadRecordAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        string operationKey,
        CancellationToken cancellationToken) =>
        connection.QuerySingleOrDefaultAsync<StockRecordRow>(new CommandDefinition(
            """
            SELECT `id` Id,`stock_id` StockId,
                   `before_available_qty` BeforeAvailableQty,`after_available_qty` AfterAvailableQty,
                   `before_occupied_qty` BeforeOccupiedQty,`after_occupied_qty` AfterOccupiedQty,
                   `before_total_qty` BeforeTotalQty,`after_total_qty` AfterTotalQty
              FROM `trk_stock_record`
             WHERE `operation_key`=@operationKey AND `deleted`=b'0'
             ORDER BY `id` DESC LIMIT 1 FOR UPDATE
            """,
            new { operationKey }, transaction, cancellationToken: cancellationToken));

    private static async Task<long> InsertRecordAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        StockMutationContext context,
        StockRow stock,
        StockQuantitySnapshot before,
        StockQuantitySnapshot after,
        string eventType,
        DateTime now,
        StockReservationMutationCoordinator.MutationState? reservationState,
        CancellationToken cancellationToken)
    {
        var totalDelta = after.TotalQty - before.TotalQty;
        var availableDelta = after.AvailableQty - before.AvailableQty;
        var occupiedDelta = after.OccupiedQty - before.OccupiedQty;
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
                (@operationKey,@bizType,@bizId,@bizItemId,@bizNo,@stockId,
                 @freightForwarderId,@warehouseId,@deptId,@orderUserId,@commodityId,
                 @commoditySku,@commodityName,@totalDelta,@beforeTotal,@afterTotal,@direction,
                 @now,@operatorId,@operatorName,@remark,@operatorName,@now,
                 @operatorName,@now,b'0',@operationKey,@availableDelta,
                 @occupiedDelta,@totalDelta,@beforeAvailable,@afterAvailable,
                 @beforeOccupied,@afterOccupied,@beforeTotal,@afterTotal,
                 @sharedCommandId,@reservationId,@reservationItemId,@reservationAction)
            """,
            new
            {
                context.OperationKey,
                context.BizType,
                context.BizId,
                context.BizItemId,
                bizNo,
                stockId = stock.Id,
                stock.FreightForwarderId,
                warehouseId = stock.WarehouseId,
                stock.DeptId,
                stock.OrderUserId,
                stock.CommodityId,
                stock.CommoditySku,
                stock.CommodityName,
                totalDelta,
                beforeTotal = before.TotalQty,
                afterTotal = after.TotalQty,
                direction = totalDelta > 0 || eventType == "UNLOCK" ? "IN" : "OUT",
                now,
                context.OperatorId,
                operatorName = context.Operator,
                remark = context.Remark ?? string.Empty,
                availableDelta,
                occupiedDelta,
                beforeAvailable = before.AvailableQty,
                afterAvailable = after.AvailableQty,
                beforeOccupied = before.OccupiedQty,
                afterOccupied = after.OccupiedQty,
                sharedCommandId = reservationState?.Command.CommandId,
                reservationId = reservationState?.Owner.ReservationId,
                reservationItemId = reservationState?.Owner.ReservationItemId,
                reservationAction = reservationState?.Command.Action
            }, transaction, cancellationToken: cancellationToken));
        return await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT LAST_INSERT_ID();", transaction: transaction,
            cancellationToken: cancellationToken));
    }

    private static PackingStockMutationResult FromRecord(
        string operationKey,
        string eventType,
        StockRecordRow record,
        StockReservationMutationCoordinator.MutationState? state) =>
        new(
            operationKey,
            eventType,
            record.StockId,
            record.Id,
            new StockQuantitySnapshot(
                record.BeforeAvailableQty, record.BeforeOccupiedQty, record.BeforeTotalQty),
            new StockQuantitySnapshot(
                record.AfterAvailableQty, record.AfterOccupiedQty, record.AfterTotalQty),
            true,
            state?.Command.CommandId,
            state?.Owner.ReservationId,
            state?.Owner.ReservationItemId);

    private static Task ExecuteAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        string sql,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            sql, transaction: transaction, cancellationToken: cancellationToken));

    private static void ValidateInvocation(
        IDbConnection connection,
        IDbTransaction transaction,
        StockMutationContext context,
        long stockId)
    {
        ValidateConnectionAndTransaction(connection, transaction);
        ArgumentNullException.ThrowIfNull(context);
        if (stockId <= 0) throw new ArgumentOutOfRangeException(nameof(stockId));
        if (context.ErpWarehouseId <= 0) throw new ArgumentOutOfRangeException(nameof(context.ErpWarehouseId));
        Required(context.OperationKey, 64, nameof(context.OperationKey));
        Required(context.BizType, 32, nameof(context.BizType));
        Required(context.Operator, 64, nameof(context.Operator));
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

    private static void Required(string value, int maxLength, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
            throw new ArgumentException($"值不能为空且不能超过{maxLength}个字符", parameterName);
    }

    private sealed class StockWarehouseRow
    {
        public long Id { get; init; }
        public long WarehouseId { get; init; }
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
        public StockQuantitySnapshot Quantities => new(AvailableQty, OccupiedQty, TotalQty);
    }

    private sealed class StockRecordRow
    {
        public long Id { get; init; }
        public long StockId { get; init; }
        public long BeforeAvailableQty { get; init; }
        public long AfterAvailableQty { get; init; }
        public long BeforeOccupiedQty { get; init; }
        public long AfterOccupiedQty { get; init; }
        public long BeforeTotalQty { get; init; }
        public long AfterTotalQty { get; init; }
    }
}
