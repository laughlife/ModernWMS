using System.Data;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using ModernWMS.WMS.IServices.StockAllocation;

namespace ModernWMS.WMS.Services.StockAllocation;

/// <summary>
/// Owns the shared ERP reservation/command rows and the WMS location-level
/// reservation decomposition. The caller must already hold the runtime gate;
/// owner rows are locked before ERP stock and allocation rows.
/// </summary>
internal static class StockReservationMutationCoordinator
{
    internal sealed record LockedOwner(
        long ReservationId,
        long ReservationItemId,
        long ReservationVersion,
        long ItemVersion,
        long ItemRemainingQty,
        long ItemReleasedQty,
        long ItemConsumedQty);

    internal sealed record ClaimedCommand(
        long CommandId,
        string Action,
        string RequestFingerprint,
        bool IsReplay);

    internal sealed record MutationState(
        LockedOwner Owner,
        ClaimedCommand Command,
        long AllocationReservationId,
        long RemainingAfter);

    internal static bool RequiresReservation(string eventType) =>
        eventType is "LOCK" or "UNLOCK" or "SHIP_OUT";

    internal static async Task<LockedOwner> LockOwnerAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        StockMutationContext context,
        long stockId,
        string eventType,
        CancellationToken cancellationToken)
    {
        if (!RequiresReservation(eventType))
            throw new InvalidOperationException($"库存动作 {eventType} 不应进入预占所有权流程");
        var request = RequireRequest(context);
        ValidateRequest(request, eventType);

        var ownerSql = request.ExistingReservationId != null ? """
            SELECT `id` Id,`version` Version,`status` Status
              FROM `trk_stock_reservation`
             WHERE `tenant_id`=@tenantId AND `id`=@existingReservationId AND `deleted`=b'0'
             FOR UPDATE
            """ : """
            SELECT `id` Id,`version` Version,`status` Status
              FROM `trk_stock_reservation`
             WHERE `tenant_id`=@tenantId AND `source_system`=@sourceSystem
               AND `biz_type`=@bizType AND `biz_id`=@bizId AND `deleted`=b'0'
             FOR UPDATE
            """;
        var owner = await connection.QuerySingleOrDefaultAsync<ReservationRow>(new CommandDefinition(
            ownerSql,
            new
            {
                tenantId = context.TenantId,
                existingReservationId = request.ExistingReservationId,
                request.SourceSystem,
                bizType = request.ReservationBizType,
                bizId = request.ReservationBizId
            }, transaction, cancellationToken: cancellationToken));

        if (owner == null)
        {
            if (eventType != "LOCK" || request.ExistingReservationId != null
                || request.ExistingReservationItemId != null)
                throw new InvalidOperationException("预占来源不存在，禁止释放或消费无主占用");
            var now = DateTime.Now;
            var reservationNo = BuildReservationNo(context.TenantId, request);
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO `trk_stock_reservation`
                    (`tenant_id`,`reservation_no`,`source_system`,`biz_type`,`biz_id`,`biz_no`,
                     `carrier_biz_type`,`carrier_biz_id`,`status`,`close_mode`,`version`,
                     `creator`,`create_time`,`updater`,`update_time`,`deleted`)
                VALUES
                    (@tenantId,@reservationNo,@sourceSystem,@bizType,@bizId,@bizNo,
                     @carrierBizType,@carrierBizId,'ACTIVE',NULL,0,
                     @operatorName,@now,@operatorName,@now,b'0')
                """,
                new
                {
                    tenantId = context.TenantId,
                    reservationNo,
                    request.SourceSystem,
                    bizType = request.ReservationBizType,
                    bizId = request.ReservationBizId,
                    bizNo = EmptyToNull(request.ReservationBizNo),
                    request.CarrierBizType,
                    request.CarrierBizId,
                    operatorName = ErpOperator(context.Operator),
                    now
                }, transaction, cancellationToken: cancellationToken));
            var id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT LAST_INSERT_ID();", transaction: transaction,
                cancellationToken: cancellationToken));
            owner = new ReservationRow(id, 0, "ACTIVE");
        }

        if (request.ExistingReservationId is { } expectedOwnerId && expectedOwnerId != owner.Id)
            throw new InvalidOperationException("业务记录携带的预占主单与稳定来源不一致");
        var itemSql = request.ExistingReservationItemId != null ? """
            SELECT `id` Id,`version` Version,`remaining_qty` RemainingQty,
                   `released_qty` ReleasedQty,`consumed_qty` ConsumedQty,`status` Status
              FROM `trk_stock_reservation_item`
             WHERE `tenant_id`=@tenantId AND `id`=@existingReservationItemId
               AND `reservation_id`=@reservationId AND `stock_id`=@stockId AND `deleted`=b'0'
             FOR UPDATE
            """ : """
            SELECT `id` Id,`version` Version,`remaining_qty` RemainingQty,
                   `released_qty` ReleasedQty,`consumed_qty` ConsumedQty,`status` Status
              FROM `trk_stock_reservation_item`
             WHERE `tenant_id`=@tenantId AND `reservation_id`=@reservationId
               AND `source_line_key`=@sourceLineKey AND `stock_id`=@stockId AND `deleted`=b'0'
             FOR UPDATE
            """;
        var item = await connection.QuerySingleOrDefaultAsync<ReservationItemRow>(new CommandDefinition(
            itemSql,
            new
            {
                tenantId = context.TenantId,
                reservationId = owner.Id,
                existingReservationItemId = request.ExistingReservationItemId,
                request.SourceLineKey,
                stockId
            }, transaction, cancellationToken: cancellationToken));
        if (item == null)
        {
            if (eventType != "LOCK" || request.ExistingReservationItemId != null)
                throw new InvalidOperationException("预占明细不存在，禁止释放或消费无主占用");
            var now = DateTime.Now;
            var sourceFingerprint = Hash(
                $"{request.SourceSystem}|{request.ReservationBizType}|{request.ReservationBizId}|" +
                $"{request.SourceLineType}|{request.SourceLineId}|{request.SourceLineKey}|{stockId}");
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO `trk_stock_reservation_item`
                    (`tenant_id`,`reservation_id`,`source_line_type`,`source_line_id`,`source_line_key`,
                     `stock_id`,`reserved_qty`,`released_qty`,`consumed_qty`,`remaining_qty`,
                     `status`,`version`,`source_snapshot_json`,`source_fingerprint`,
                     `creator`,`create_time`,`updater`,`update_time`,`deleted`)
                VALUES
                    (@tenantId,@reservationId,@sourceLineType,@sourceLineId,@sourceLineKey,
                     @stockId,0,0,0,0,'ACTIVE',0,NULL,@sourceFingerprint,
                     @operatorName,@now,@operatorName,@now,b'0')
                """,
                new
                {
                    tenantId = context.TenantId,
                    reservationId = owner.Id,
                    request.SourceLineType,
                    request.SourceLineId,
                    request.SourceLineKey,
                    stockId,
                    sourceFingerprint,
                    operatorName = ErpOperator(context.Operator),
                    now
                }, transaction, cancellationToken: cancellationToken));
            var id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
                "SELECT LAST_INSERT_ID();", transaction: transaction,
                cancellationToken: cancellationToken));
            item = new ReservationItemRow(id, 0, 0, 0, 0, "ACTIVE");
        }

        if (request.ExistingReservationItemId is { } expectedItemId && expectedItemId != item.Id)
            throw new InvalidOperationException("业务记录携带的预占明细与稳定来源不一致");
        return new LockedOwner(owner.Id, item.Id, owner.Version, item.Version,
            item.RemainingQty, item.ReleasedQty, item.ConsumedQty);
    }

    internal static async Task<MutationState> BeginMutationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        StockMutationContext context,
        LockedOwner owner,
        long stockId,
        long allocationId,
        string eventType,
        long quantity,
        CancellationToken cancellationToken)
    {
        var request = RequireRequest(context);
        var proposedAction = ResolveAction(eventType, quantity, owner.ItemRemainingQty);
        var allocationFingerprint = Hash($"{stockId}|{allocationId}|{quantity}");
        var allocationReservation = await LockOrCreateAllocationReservationAsync(
            connection, transaction, context, owner.ReservationItemId, stockId,
            allocationId, eventType, cancellationToken);

        var command = await connection.QuerySingleOrDefaultAsync<CommandRow>(new CommandDefinition(
            """
            SELECT `id` Id,`action` Action,`reservation_id` ReservationId,
                   `request_fingerprint` RequestFingerprint,`result_status` ResultStatus
              FROM `trk_stock_reservation_command`
             WHERE `tenant_id`=@tenantId AND `namespace`=@namespace AND `command_id`=@commandId
             FOR UPDATE
            """,
            new { tenantId = context.TenantId, request.Namespace, request.CommandId },
            transaction, cancellationToken: cancellationToken));
        if (command != null)
        {
            if (!ActionMatchesEvent(command.Action, eventType))
                throw new InvalidOperationException("共享预占命令动作与库存动作不一致");
            var replayFingerprint = RequestFingerprint(request, command.Action, owner,
                stockId, allocationId, quantity, allocationFingerprint);
            if (command.ReservationId != owner.ReservationId
                || command.RequestFingerprint != replayFingerprint)
                throw new InvalidOperationException("共享预占命令ID已被不同请求使用");
            if (command.ResultStatus != "SUCCEEDED")
                throw new InvalidOperationException("共享预占命令仍处于PENDING，必须回滚原事务后重试");
            var resultRemaining = await connection.ExecuteScalarAsync<long?>(new CommandDefinition(
                """
                SELECT `result_remaining_qty` FROM `trk_stock_reservation_command_item`
                 WHERE `tenant_id`=@tenantId AND `command_header_id`=@commandId
                   AND `reservation_item_id`=@reservationItemId
                """,
                new
                {
                    tenantId = context.TenantId,
                    commandId = command.Id,
                    reservationItemId = owner.ReservationItemId
                }, transaction, cancellationToken: cancellationToken));
            return new MutationState(owner,
                new ClaimedCommand(command.Id, command.Action, replayFingerprint, true),
                allocationReservation.Id, resultRemaining ?? owner.ItemRemainingQty);
        }

        var action = proposedAction;
        var requestFingerprint = RequestFingerprint(request, action, owner,
            stockId, allocationId, quantity, allocationFingerprint);
        var now = DateTime.Now;
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO `trk_stock_reservation_command`
                (`tenant_id`,`namespace`,`command_id`,`action`,`reservation_id`,
                 `request_fingerprint`,`result_status`,`result_fingerprint`,`operator_id`,
                 `operator_name`,`complete_time`,`version`,`creator`,`create_time`,`updater`,
                 `update_time`,`deleted`)
            VALUES
                (@tenantId,@namespace,@commandId,@action,@reservationId,
                 @requestFingerprint,'PENDING',NULL,@operatorId,
                 @operatorName,NULL,0,@operatorName,@now,@operatorName,@now,b'0')
            """,
            new
            {
                tenantId = context.TenantId,
                request.Namespace,
                request.CommandId,
                action,
                reservationId = owner.ReservationId,
                requestFingerprint,
                operatorId = context.OperatorId,
                operatorName = ErpOperator(context.Operator),
                now
            }, transaction, cancellationToken: cancellationToken));
        var sharedCommandId = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT LAST_INSERT_ID();", transaction: transaction,
            cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO `trk_stock_reservation_command_item`
                (`tenant_id`,`command_header_id`,`line_no`,`reservation_id`,`reservation_item_id`,
                 `source_line_key`,`stock_id`,`action_qty`,`expected_reservation_version`,
                 `expected_item_version`,`allocation_plan_fingerprint`,`request_line_fingerprint`,
                 `stock_record_id`,`result_remaining_qty`,`result_line_fingerprint`,
                 `creator`,`create_time`,`updater`,`update_time`,`deleted`)
            VALUES
                (@tenantId,@commandHeaderId,1,@reservationId,@reservationItemId,
                 @sourceLineKey,@stockId,@quantity,@reservationVersion,
                 @itemVersion,@allocationFingerprint,@requestFingerprint,
                 NULL,NULL,NULL,@operatorName,@now,@operatorName,@now,b'0')
            """,
            new
            {
                tenantId = context.TenantId,
                commandHeaderId = sharedCommandId,
                reservationId = owner.ReservationId,
                reservationItemId = owner.ReservationItemId,
                request.SourceLineKey,
                stockId,
                quantity,
                reservationVersion = owner.ReservationVersion,
                itemVersion = owner.ItemVersion,
                allocationFingerprint,
                requestFingerprint,
                operatorName = ErpOperator(context.Operator),
                now
            }, transaction, cancellationToken: cancellationToken));

        var remainingAfter = checked(owner.ItemRemainingQty + (eventType switch
        {
            "LOCK" => quantity,
            "UNLOCK" or "SHIP_OUT" => -quantity,
            _ => throw new InvalidOperationException("未知预占动作")
        }));
        if (remainingAfter < 0) throw new InvalidOperationException("预占来源剩余数量不足");

        await UpdateReservationItemAsync(connection, transaction, context, owner,
            eventType, quantity, remainingAfter, now, cancellationToken);
        await UpdateAllocationReservationAsync(connection, transaction, context,
            allocationReservation, eventType, quantity, now, cancellationToken);
        return new MutationState(owner,
            new ClaimedCommand(sharedCommandId, action, requestFingerprint, false),
            allocationReservation.Id, remainingAfter);
    }

    internal static async Task CompleteAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        StockMutationContext context,
        MutationState state,
        long stockRecordId,
        CancellationToken cancellationToken)
    {
        if (state.Command.IsReplay) return;
        var now = DateTime.Now;
        var resultLineFingerprint = Hash(
            $"{state.Command.CommandId}|{state.Owner.ReservationItemId}|{stockRecordId}|{state.RemainingAfter}");
        var lineCount = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE `trk_stock_reservation_command_item`
               SET `stock_record_id`=@stockRecordId,`result_remaining_qty`=@remainingQty,
                   `result_line_fingerprint`=@resultLineFingerprint,
                   `updater`=@operatorName,`update_time`=@now
             WHERE `tenant_id`=@tenantId AND `command_header_id`=@commandId
               AND `reservation_item_id`=@reservationItemId
               AND `stock_record_id` IS NULL
            """,
            new
            {
                tenantId = context.TenantId,
                commandId = state.Command.CommandId,
                reservationItemId = state.Owner.ReservationItemId,
                stockRecordId,
                remainingQty = state.RemainingAfter,
                resultLineFingerprint,
                operatorName = ErpOperator(context.Operator),
                now
            }, transaction, cancellationToken: cancellationToken));
        if (lineCount != 1) throw new InvalidOperationException("共享预占命令明细完成状态发生变化");

        await RefreshReservationStatusAsync(connection, transaction, context,
            state.Owner.ReservationId, now, cancellationToken);
        var resultFingerprint = Hash(
            $"{state.Command.RequestFingerprint}|{stockRecordId}|{state.RemainingAfter}");
        var commandCount = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE `trk_stock_reservation_command`
               SET `result_status`='SUCCEEDED',`result_fingerprint`=@resultFingerprint,
                   `complete_time`=@now,`version`=`version`+1,
                   `updater`=@operatorName,`update_time`=@now
             WHERE `id`=@commandId AND `tenant_id`=@tenantId AND `result_status`='PENDING'
            """,
            new
            {
                commandId = state.Command.CommandId,
                tenantId = context.TenantId,
                resultFingerprint,
                operatorName = ErpOperator(context.Operator),
                now
            }, transaction, cancellationToken: cancellationToken));
        if (commandCount != 1) throw new InvalidOperationException("共享预占命令头完成状态发生变化");
    }

    internal static async Task EnsureConservationAsync(
        IDbConnection connection,
        IDbTransaction transaction,
        long tenantId,
        long stockId,
        long allocationId,
        MutationState state,
        CancellationToken cancellationToken)
    {
        var quantities = await connection.QuerySingleAsync<ReservationConservationRow>(new CommandDefinition(
            """
            SELECT item.`remaining_qty` ItemRemainingQty,
                   COALESCE((SELECT SUM(location_owner.`remaining_qty`)
                               FROM `wms_erp_stock_reservation_allocation` location_owner
                              WHERE location_owner.`tenant_id`=@tenantId
                                AND location_owner.`reservation_item_id`=item.`id`
                                AND location_owner.`deleted`=b'0'),0) ItemLocationRemainingQty,
                   stock.`occupied_qty` StockOccupiedQty,
                   COALESCE((SELECT SUM(stock_owner.`remaining_qty`)
                               FROM `trk_stock_reservation_item` stock_owner
                              WHERE stock_owner.`tenant_id`=@tenantId
                                AND stock_owner.`stock_id`=@stockId
                                AND stock_owner.`deleted`=b'0'),0) StockOwnerRemainingQty,
                   allocation.`occupied_qty` AllocationOccupiedQty,
                   COALESCE((SELECT SUM(allocation_owner.`remaining_qty`)
                               FROM `wms_erp_stock_reservation_allocation` allocation_owner
                              WHERE allocation_owner.`tenant_id`=@tenantId
                                AND allocation_owner.`stock_allocation_id`=@allocationId
                                AND allocation_owner.`deleted`=b'0'),0) AllocationOwnerRemainingQty
              FROM `trk_stock_reservation_item` item
              JOIN `trk_stock` stock ON stock.`id`=@stockId AND stock.`deleted`=b'0'
              JOIN `wms_erp_stock_allocation` allocation
                ON allocation.`id`=@allocationId AND allocation.`tenant_id`=@tenantId
             WHERE item.`id`=@reservationItemId AND item.`tenant_id`=@tenantId
               AND item.`stock_id`=@stockId AND item.`deleted`=b'0'
            """,
            new
            {
                tenantId,
                stockId,
                allocationId,
                reservationItemId = state.Owner.ReservationItemId
            }, transaction, cancellationToken: cancellationToken));
        if (quantities.ItemRemainingQty != quantities.ItemLocationRemainingQty)
            throw new InvalidOperationException("预占明细与库位预占分解不守恒");
        if (quantities.StockOccupiedQty != quantities.StockOwnerRemainingQty)
            throw new InvalidOperationException("ERP库存占用与预占来源合计不守恒");
        if (quantities.AllocationOccupiedQty != quantities.AllocationOwnerRemainingQty)
            throw new InvalidOperationException("库位占用与库位预占来源合计不守恒");
    }

    private static async Task<AllocationReservationRow> LockOrCreateAllocationReservationAsync(
        IDbConnection connection, IDbTransaction transaction, StockMutationContext context,
        long reservationItemId, long stockId, long allocationId, string eventType,
        CancellationToken cancellationToken)
    {
        var row = await connection.QuerySingleOrDefaultAsync<AllocationReservationRow>(new CommandDefinition(
            """
            SELECT `id` Id,`reserved_qty` ReservedQty,`released_qty` ReleasedQty,
                   `consumed_qty` ConsumedQty,`remaining_qty` RemainingQty,
                   `status` Status,`row_version` RowVersion
              FROM `wms_erp_stock_reservation_allocation`
             WHERE `tenant_id`=@tenantId AND `reservation_item_id`=@reservationItemId
               AND `stock_allocation_id`=@allocationId AND `deleted`=b'0'
             FOR UPDATE
            """,
            new { tenantId = context.TenantId, reservationItemId, allocationId },
            transaction, cancellationToken: cancellationToken));
        if (row != null) return row;
        if (eventType != "LOCK")
            throw new InvalidOperationException("库位预占来源不存在，禁止释放或消费");
        var now = DateTime.Now;
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO `wms_erp_stock_reservation_allocation`
                (`tenant_id`,`reservation_item_id`,`erp_stock_id`,`stock_allocation_id`,
                 `reserved_qty`,`released_qty`,`consumed_qty`,`remaining_qty`,`status`,
                 `row_version`,`creator`,`create_time`,`updater`,`update_time`,`deleted`)
            VALUES
                (@tenantId,@reservationItemId,@stockId,@allocationId,
                 0,0,0,0,'ACTIVE',0,@operatorName,@now,@operatorName,@now,b'0')
            """,
            new
            {
                tenantId = context.TenantId,
                reservationItemId,
                stockId,
                allocationId,
                operatorName = ErpOperator(context.Operator),
                now
            }, transaction, cancellationToken: cancellationToken));
        var id = await connection.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT LAST_INSERT_ID();", transaction: transaction,
            cancellationToken: cancellationToken));
        return new AllocationReservationRow(id, 0, 0, 0, 0, "ACTIVE", 0);
    }

    private static async Task UpdateReservationItemAsync(
        IDbConnection connection, IDbTransaction transaction, StockMutationContext context,
        LockedOwner owner, string eventType, long quantity, long remainingAfter, DateTime now,
        CancellationToken cancellationToken)
    {
        var reservedDelta = eventType == "LOCK" ? quantity : 0;
        var releasedDelta = eventType == "UNLOCK" ? quantity : 0;
        var consumedDelta = eventType == "SHIP_OUT" ? quantity : 0;
        var status = ItemStatus(remainingAfter,
            checked(owner.ItemReleasedQty + releasedDelta),
            checked(owner.ItemConsumedQty + consumedDelta));
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE `trk_stock_reservation_item`
               SET `reserved_qty`=`reserved_qty`+@reservedDelta,
                   `released_qty`=`released_qty`+@releasedDelta,
                   `consumed_qty`=`consumed_qty`+@consumedDelta,
                   `remaining_qty`=@remainingAfter,`status`=@status,
                   `version`=`version`+1,`updater`=@operatorName,`update_time`=@now
             WHERE `id`=@id AND `tenant_id`=@tenantId AND `version`=@expectedVersion
               AND `remaining_qty`=@beforeRemaining
            """,
            new
            {
                id = owner.ReservationItemId,
                tenantId = context.TenantId,
                reservedDelta,
                releasedDelta,
                consumedDelta,
                remainingAfter,
                status,
                expectedVersion = owner.ItemVersion,
                beforeRemaining = owner.ItemRemainingQty,
                operatorName = ErpOperator(context.Operator),
                now
            }, transaction, cancellationToken: cancellationToken));
        if (affected != 1) throw new InvalidOperationException("预占明细版本或剩余数量发生变化");
    }

    private static async Task UpdateAllocationReservationAsync(
        IDbConnection connection, IDbTransaction transaction, StockMutationContext context,
        AllocationReservationRow row, string eventType, long quantity, DateTime now,
        CancellationToken cancellationToken)
    {
        var reserved = checked(row.ReservedQty + (eventType == "LOCK" ? quantity : 0));
        var released = checked(row.ReleasedQty + (eventType == "UNLOCK" ? quantity : 0));
        var consumed = checked(row.ConsumedQty + (eventType == "SHIP_OUT" ? quantity : 0));
        var remaining = checked(row.RemainingQty + (eventType == "LOCK" ? quantity : -quantity));
        if (remaining < 0 || reserved != checked(released + consumed + remaining))
            throw new InvalidOperationException("库位预占数量不守恒");
        var status = ItemStatus(remaining, released, consumed);
        var affected = await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE `wms_erp_stock_reservation_allocation`
               SET `reserved_qty`=@reserved,`released_qty`=@released,
                   `consumed_qty`=@consumed,`remaining_qty`=@remaining,`status`=@status,
                   `row_version`=`row_version`+1,`updater`=@operatorName,`update_time`=@now
             WHERE `id`=@id AND `tenant_id`=@tenantId AND `row_version`=@rowVersion
            """,
            new
            {
                row.Id,
                tenantId = context.TenantId,
                reserved,
                released,
                consumed,
                remaining,
                status,
                rowVersion = row.RowVersion,
                operatorName = ErpOperator(context.Operator),
                now
            }, transaction, cancellationToken: cancellationToken));
        if (affected != 1) throw new InvalidOperationException("库位预占版本发生变化");
    }

    private static async Task RefreshReservationStatusAsync(
        IDbConnection connection, IDbTransaction transaction, StockMutationContext context,
        long reservationId, DateTime now, CancellationToken cancellationToken)
    {
        var totals = await connection.QuerySingleAsync<ReservationTotals>(new CommandDefinition(
            """
            SELECT COALESCE(SUM(`remaining_qty`),0) RemainingQty,
                   COALESCE(SUM(`released_qty`),0) ReleasedQty,
                   COALESCE(SUM(`consumed_qty`),0) ConsumedQty
              FROM `trk_stock_reservation_item`
             WHERE `tenant_id`=@tenantId AND `reservation_id`=@reservationId AND `deleted`=b'0'
            """,
            new { tenantId = context.TenantId, reservationId }, transaction,
            cancellationToken: cancellationToken));
        var status = totals.RemainingQty > 0
            ? totals.ReleasedQty + totals.ConsumedQty > 0 ? "PARTIALLY_SETTLED" : "ACTIVE"
            : totals.ReleasedQty > 0 && totals.ConsumedQty > 0 ? "MIXED_CLOSED"
            : totals.ConsumedQty > 0 ? "CONSUMED" : "RELEASED";
        var closeMode = totals.RemainingQty > 0 ? null
            : totals.ReleasedQty > 0 && totals.ConsumedQty > 0 ? "MIXED"
            : totals.ConsumedQty > 0 ? "CONSUME_ALL" : "RELEASE_ALL";
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE `trk_stock_reservation`
               SET `status`=@status,`close_mode`=@closeMode,`version`=`version`+1,
                   `updater`=@operatorName,`update_time`=@now
             WHERE `id`=@reservationId AND `tenant_id`=@tenantId
            """,
            new
            {
                reservationId,
                tenantId = context.TenantId,
                status,
                closeMode,
                operatorName = ErpOperator(context.Operator),
                now
            }, transaction, cancellationToken: cancellationToken));
    }

    private static StockReservationMutationContext RequireRequest(StockMutationContext context) =>
        context.Reservation ?? throw new InvalidOperationException(
            "唯一库存模式的预占、释放和锁定出库必须携带共享reservation来源");

    private static void ValidateRequest(StockReservationMutationContext request, string eventType)
    {
        Required(request.Namespace, 64, nameof(request.Namespace));
        Required(request.CommandId, 128, nameof(request.CommandId));
        Required(request.SourceSystem, 32, nameof(request.SourceSystem));
        Required(request.ReservationBizType, 64, nameof(request.ReservationBizType));
        Required(request.SourceLineType, 64, nameof(request.SourceLineType));
        Required(request.SourceLineKey, 128, nameof(request.SourceLineKey));
        if (request.ReservationBizId <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.ReservationBizId));
        if (request.SourceLineId is <= 0)
            throw new ArgumentOutOfRangeException(nameof(request.SourceLineId));
        if (eventType != "LOCK"
            && (request.ExistingReservationId is null || request.ExistingReservationItemId is null))
            throw new InvalidOperationException("释放或消费必须携带业务记录保存的reservation主键和明细主键");
    }

    private static string ResolveAction(string eventType, long quantity, long remainingBefore) =>
        eventType switch
        {
            "LOCK" => remainingBefore == 0 ? "RESERVE" : "RESERVE_MORE",
            "UNLOCK" => quantity == remainingBefore ? "RELEASE_ALL" : "PARTIAL_RELEASE",
            "SHIP_OUT" => quantity == remainingBefore ? "CONSUME_ALL" : "PARTIAL_CONSUME",
            _ => throw new InvalidOperationException($"不支持的预占动作：{eventType}")
        };

    private static bool ActionMatchesEvent(string action, string eventType) => eventType switch
    {
        "LOCK" => action is "RESERVE" or "RESERVE_MORE",
        "UNLOCK" => action is "PARTIAL_RELEASE" or "RELEASE_ALL"
            or "RECONCILE_ORPHAN_RELEASE",
        "SHIP_OUT" => action is "PARTIAL_CONSUME" or "CONSUME_ALL",
        _ => false
    };

    private static string RequestFingerprint(StockReservationMutationContext request, string action,
        LockedOwner owner, long stockId, long allocationId, long quantity,
        string allocationFingerprint) => Hash(
        $"{request.Namespace}|{request.CommandId}|{action}|{owner.ReservationId}|" +
        $"{owner.ReservationItemId}|{stockId}|{allocationId}|{quantity}|{allocationFingerprint}");

    private static string ItemStatus(long remaining, long releasedDelta, long consumedDelta) =>
        remaining > 0
            ? releasedDelta + consumedDelta > 0 ? "PARTIALLY_SETTLED" : "ACTIVE"
            : releasedDelta > 0 && consumedDelta > 0 ? "MIXED_CLOSED"
            : consumedDelta > 0 ? "CONSUMED" : "RELEASED";

    private static string BuildReservationNo(long tenantId, StockReservationMutationContext request) =>
        "MWMS-" + Hash($"{tenantId}|{request.SourceSystem}|{request.ReservationBizType}|{request.ReservationBizId}")[..59];

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string ErpOperator(string value) => value.Length <= 64 ? value : value[..64];
    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Required(string value, int maxLength, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength)
            throw new ArgumentException($"{name}不能为空且不能超过{maxLength}个字符", name);
    }

    private sealed record ReservationRow(long Id, long Version, string Status);
    private sealed record ReservationItemRow(long Id, long Version, long RemainingQty,
        long ReleasedQty, long ConsumedQty, string Status);
    private sealed record CommandRow(long Id, string Action, long? ReservationId,
        string RequestFingerprint, string ResultStatus);
    private sealed record AllocationReservationRow(long Id, long ReservedQty, long ReleasedQty,
        long ConsumedQty, long RemainingQty, string Status, long RowVersion);
    private sealed record ReservationTotals(long RemainingQty, long ReleasedQty, long ConsumedQty);
    private sealed record ReservationConservationRow(long ItemRemainingQty,
        long ItemLocationRemainingQty, long StockOccupiedQty, long StockOwnerRemainingQty,
        long AllocationOccupiedQty, long AllocationOwnerRemainingQty);
}
