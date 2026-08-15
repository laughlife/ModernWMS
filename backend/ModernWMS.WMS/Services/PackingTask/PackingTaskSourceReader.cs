using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModernWMS.Core.DBContext;
using ModernWMS.Core.DBContext.Entities;
using ModernWMS.WMS.Entities.ViewModels.PackingTask;
using ModernWMS.WMS.IServices.PackingTask;

namespace ModernWMS.WMS.Services.PackingTask;

/// <summary>
/// Reads current packing tasks from the local shared ERP database. The source is
/// maintained by XXL-JOB; this service has no SellFox network dependency.
/// </summary>
public sealed class PackingTaskSourceReader : IPackingTaskSourceReader
{
    private const string SourceTable = "ruiyi_sellfox_packing_task";
    private const string SourceItemTable = "ruiyi_sellfox_packing_task_item";
    private const string RequiredCartonsColumn = "cartons_json";
    private const int RequiredSourceColumnCount = 4;
    private readonly RuoyiDbContext _database;
    private readonly Func<CancellationToken, Task<PackingTaskSourceCapability>>? _capabilityProbe;

    public PackingTaskSourceReader(RuoyiDbContext database)
    {
        _database = database;
    }

    /// <summary>
    /// Test seam for exercising a failed physical-schema capability probe without
    /// requiring a live MySQL INFORMATION_SCHEMA.
    /// </summary>
    public PackingTaskSourceReader(
        RuoyiDbContext database,
        Func<CancellationToken, Task<PackingTaskSourceCapability>> capabilityProbe)
    {
        _database = database;
        _capabilityProbe = capabilityProbe ?? throw new ArgumentNullException(nameof(capabilityProbe));
    }

    public async Task<PackingTaskSourceCapability> VerifyCapabilityAsync(
        CancellationToken cancellationToken = default)
    {
        if (_capabilityProbe is not null)
        {
            return await _capabilityProbe(cancellationToken);
        }

        if (!_database.Database.IsRelational())
        {
            var task = _database.Model.FindEntityType(typeof(ErpPackingTaskEntity));
            var item = _database.Model.FindEntityType(typeof(ErpPackingTaskItemEntity));
            var hasRequiredModel = task?.FindProperty(nameof(ErpPackingTaskEntity.cartons_json)) is not null
                && task.FindProperty(nameof(ErpPackingTaskEntity.sellfox_task_id)) is not null
                && item?.FindProperty(nameof(ErpPackingTaskItemEntity.sellfox_task_id)) is not null
                && item.FindProperty(nameof(ErpPackingTaskItemEntity.sellfox_item_id)) is not null;
            return !hasRequiredModel
                ? UnsupportedCapability()
                : new PackingTaskSourceCapability(true, string.Empty);
        }

        var connection = _database.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        try
        {
            if (shouldClose)
            {
                await connection.OpenAsync(cancellationToken);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT COUNT(*)
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_SCHEMA = DATABASE()
                  AND (
                    (TABLE_NAME = @task_table AND COLUMN_NAME IN ('cartons_json', 'sellfox_task_id'))
                    OR
                    (TABLE_NAME = @item_table AND COLUMN_NAME IN ('sellfox_task_id', 'sellfox_item_id'))
                  )
                """;

            var tableParameter = command.CreateParameter();
            tableParameter.ParameterName = "@task_table";
            tableParameter.Value = SourceTable;
            command.Parameters.Add(tableParameter);

            var itemTableParameter = command.CreateParameter();
            itemTableParameter.ParameterName = "@item_table";
            itemTableParameter.Value = SourceItemTable;
            command.Parameters.Add(itemTableParameter);

            var value = await command.ExecuteScalarAsync(cancellationToken);
            var exists = value is not null
                && value != DBNull.Value
                && Convert.ToInt64(value) == RequiredSourceColumnCount;
            return exists
                ? new PackingTaskSourceCapability(true, string.Empty)
                : UnsupportedCapability();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new PackingTaskSourceCapability(
                false,
                $"无法验证共享表 {SourceTable}.{RequiredCartonsColumn}：{exception.Message}");
        }
        finally
        {
            if (shouldClose && connection.State == ConnectionState.Open)
            {
                await connection.CloseAsync();
            }
        }
    }

    public async Task<IReadOnlyList<PackingTaskSourceSnapshot>> ReadAsync(
        IReadOnlyCollection<long> sourceTaskIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceTaskIds);
        var requestedIds = sourceTaskIds.Distinct().OrderBy(x => x).ToArray();
        if (requestedIds.Length == 0)
        {
            return [];
        }

        if (requestedIds.Any(x => x <= 0))
        {
            throw new ArgumentException("SellFox 装箱任务ID必须为正数", nameof(sourceTaskIds));
        }

        var capability = await VerifyCapabilityAsync(cancellationToken);
        if (!capability.IsSupported)
        {
            throw new InvalidOperationException(capability.Error);
        }

        var tasks = await _database.PackingTasks.AsNoTracking()
            .Where(x => requestedIds.Contains(x.sellfox_task_id))
            .OrderBy(x => x.sellfox_task_id)
            .ThenBy(x => x.id)
            .ToListAsync(cancellationToken);

        var duplicateTask = tasks.GroupBy(x => x.sellfox_task_id).FirstOrDefault(x => x.Count() > 1);
        if (duplicateTask is not null)
        {
            throw new InvalidOperationException($"SellFox 装箱任务ID重复：{duplicateTask.Key}");
        }

        var actualIds = tasks.Select(x => x.sellfox_task_id).ToHashSet();
        var missingIds = requestedIds.Where(x => !actualIds.Contains(x)).ToArray();
        if (missingIds.Length > 0)
        {
            throw new InvalidOperationException(
                $"共享表缺少请求的 SellFox 装箱任务ID：{string.Join(", ", missingIds)}");
        }

        var items = await _database.PackingTaskItems.AsNoTracking()
            .Where(x => requestedIds.Contains(x.sellfox_task_id) && !x.source_deleted)
            .OrderBy(x => x.sellfox_task_id)
            .ThenBy(x => x.sellfox_item_id)
            .ThenBy(x => x.id)
            .ToListAsync(cancellationToken);
        var itemsByTask = items.GroupBy(x => x.sellfox_task_id).ToDictionary(x => x.Key, x => x.ToList());

        var snapshots = new List<PackingTaskSourceSnapshot>(tasks.Count);
        foreach (var task in tasks)
        {
            snapshots.Add(BuildSnapshot(task, itemsByTask.GetValueOrDefault(task.sellfox_task_id) ?? []));
        }

        return snapshots;
    }

    private static PackingTaskSourceSnapshot BuildSnapshot(
        ErpPackingTaskEntity task,
        IReadOnlyList<ErpPackingTaskItemEntity> sourceItems)
    {
        if (task.sellfox_task_id <= 0)
        {
            throw new InvalidOperationException("共享表存在无效 SellFox 装箱任务ID");
        }

        var cancelled = task.source_canceled || task.source_deleted;
        if (cancelled)
        {
            return BuildCancelledTombstone(task.sellfox_task_id);
        }

        if (task.warehouse_id is null or <= 0)
        {
            throw new InvalidOperationException($"装箱任务 {task.packing_task_sn} 缺少有效仓库ID");
        }

        if (task.task_num is null or <= 0)
        {
            throw new InvalidOperationException($"装箱任务 {task.packing_task_sn} 的 task_num 必须大于 0");
        }

        if (sourceItems.Count == 0)
        {
            throw new InvalidOperationException($"装箱任务 {task.packing_task_sn} 未包含有效商品，task_num 无法验证");
        }

        var duplicateItem = sourceItems
            .Where(x => x.sellfox_item_id > 0)
            .GroupBy(x => x.sellfox_item_id)
            .FirstOrDefault(x => x.Count() > 1);
        if (sourceItems.Any(x => x.sellfox_item_id <= 0) || duplicateItem is not null)
        {
            var identity = duplicateItem?.Key.ToString() ?? "空或非正数";
            throw new InvalidOperationException($"装箱任务 {task.packing_task_sn} 的 SellFox 商品ID无效或重复：{identity}");
        }

        var invalidQuantityItem = sourceItems.FirstOrDefault(x => x.task_num is null or <= 0);
        if (invalidQuantityItem is not null)
        {
            throw new InvalidOperationException(
                $"装箱任务 {task.packing_task_sn} 的商品 {invalidQuantityItem.sellfox_item_id} task_num 必须大于 0");
        }

        var parseResult = SellFoxCartonParser.Parse(task.cartons_json);
        if (!parseResult.IsSupported)
        {
            throw new InvalidOperationException(
                $"装箱任务 {task.packing_task_sn} 的箱数据不受支持：{parseResult.Error}");
        }
        var boxes = parseResult.Boxes;

        var mappedItems = sourceItems
            .OrderBy(x => x.sellfox_item_id)
            .ThenBy(x => x.id)
            .Select(MapItem)
            .ToArray();
        var sourceVersion = ComputeSourceVersion(task, cancelled, mappedItems, boxes);

        return new PackingTaskSourceSnapshot(
            task.sellfox_task_id,
            task.packing_task_sn?.Trim() ?? string.Empty,
            task.warehouse_id.Value,
            task.warehouse_name?.Trim() ?? string.Empty,
            sourceVersion,
            cancelled,
            mappedItems,
            boxes,
            task.cartons_json ?? string.Empty);
    }

    private static PackingTaskSourceSnapshot BuildCancelledTombstone(long sourceTaskId)
    {
        var versionSource = JsonSerializer.Serialize(new { sourceTaskId, cancelled = true });
        var sourceVersion = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(versionSource)))
            .ToLowerInvariant();
        return new PackingTaskSourceSnapshot(
            sourceTaskId,
            string.Empty,
            0,
            string.Empty,
            sourceVersion,
            true,
            [],
            [],
            string.Empty);
    }

    private static PackingTaskSourceItem MapItem(ErpPackingTaskItemEntity item)
    {
        var sourceSnapshot = JsonSerializer.Serialize(new
        {
            sourceItemId = item.sellfox_item_id,
            item.commodity_id,
            commoditySku = item.commodity_sku ?? string.Empty,
            commodityName = item.commodity_name ?? string.Empty,
            mainImage = item.main_image ?? string.Empty,
            fnSku = item.fn_sku ?? string.Empty,
            sku = item.sku ?? string.Empty,
            msku = item.msku ?? string.Empty,
            quantity = item.task_num ?? 0,
            shippedQuantity = item.quantity_shipped,
            item.shop_id,
            shopName = item.shop_name ?? string.Empty,
            item.source_hash
        });

        return new PackingTaskSourceItem(
            item.sellfox_item_id,
            item.commodity_id,
            item.commodity_sku?.Trim() ?? string.Empty,
            item.commodity_name?.Trim() ?? string.Empty,
            item.main_image?.Trim() ?? string.Empty,
            item.fn_sku?.Trim() ?? string.Empty,
            item.sku?.Trim() ?? string.Empty,
            item.msku?.Trim() ?? string.Empty,
            item.task_num ?? 0,
            sourceSnapshot);
    }

    private static string ComputeSourceVersion(
        ErpPackingTaskEntity task,
        bool cancelled,
        IReadOnlyList<PackingTaskSourceItem> items,
        IReadOnlyList<SellFoxSourceBox> boxes)
    {
        var canonicalSource = JsonSerializer.Serialize(new
        {
            sourceTaskId = task.sellfox_task_id,
            taskNo = task.packing_task_sn?.Trim() ?? string.Empty,
            warehouseId = task.warehouse_id,
            warehouseName = task.warehouse_name?.Trim() ?? string.Empty,
            sourceStatus = task.source_status,
            cancelled,
            taskQuantity = task.task_num,
            completedQuantity = task.complete_num,
            cartonCount = task.carton_num,
            task.shop_id,
            shopName = task.shop_name?.Trim() ?? string.Empty,
            marketplaceName = task.marketplace_name?.Trim() ?? string.Empty,
            task.source_hash,
            items = items.Select(x => new
            {
                x.SourceItemId,
                x.SourceSnapshot
            }),
            boxes = boxes.OrderBy(x => x.Sequence).Select(x => new
            {
                x.SourceBoxIdentity,
                x.Sequence,
                x.SourceSnapshot
            })
        });

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalSource)))
            .ToLowerInvariant();
    }

    private static PackingTaskSourceCapability UnsupportedCapability() => new(
        false,
        $"共享表缺少必需字段（包括 {SourceTable}.{RequiredCartonsColumn}、任务ID或商品ID），称重能力已失败关闭");
}
