using Microsoft.Extensions.Configuration;
using ModernWMS.Core.Database;
using ModernWMS.Core.DBContext.Entities;
using ModernWMS.Core.JWT;
using ModernWMS.Core.Models;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.Entities.ViewModels.PackingTask;
using ModernWMS.WMS.IServices;
using ModernWMS.WMS.IServices.PackingTask;
using ModernWMS.WMS.IServices.StockAllocation;

namespace ModernWMS.WMS.Services;

internal sealed record PackingTaskPageRequest(
    string Keyword,
    long? WarehouseId,
    long GroupId,
    long MemberId,
    int Offset,
    int PageSize);

internal sealed record PackingTaskStockAvailability(
    string SkuCode,
    int StockQty,
    int LockedQty = 0,
    int? AvailableQty = null);

internal sealed record PackingTaskPageData(
    List<ErpPackingTaskEntity> Tasks,
    List<ErpPackingTaskItemEntity> Items,
    IReadOnlyDictionary<long, PackingTaskStockAvailability> AvailabilityByItemId,
    int Totals);

internal sealed record PackingTaskSelectableData(
    List<SelectableStockViewModel> Rows,
    long WarehouseId,
    string WarehouseName);

internal sealed record PackingTaskStockSaveResult(bool IsSuccess, string Message);

/// <summary>Testable query boundary implemented with Dapper in production.</summary>
internal interface IPackingTaskQueryDataSource
{
    Task<PackingTaskPageData> LoadPageAsync(PackingTaskPageRequest request);

    Task<PackingTaskSelectableData?> LoadSelectableStockAsync(
        PackingTaskStockPageRequest request,
        CurrentUser currentUser);

    Task<PackingTaskStockSaveResult> SaveSelectionAsync(
        PackingTaskStockSelectRequest request,
        CurrentUser currentUser);

    Task<PackingTaskStockSaveResult> DeleteSelectionAsync(
        PackingTaskStockSelectRequest request,
        CurrentUser currentUser);
}

/// <summary>Reads formal packing-task snapshots without creating dispatch business facts.</summary>
public class PackingTaskQueryService : IPackingTaskQueryService
{
    private readonly IPackingTaskQueryDataSource _dataSource;
    private readonly IConfiguration _configuration;
    private readonly IWarehouseAccessService? _warehouseAccessService;

    /// <summary>Initializes the packing-task query service.</summary>
    public PackingTaskQueryService(
        IMySqlConnectionFactory connectionFactory,
        IConfiguration configuration,
        IWarehouseAccessService warehouseAccessService,
        IPackingStockMutationService packingStockMutationService,
        ILegacyPackingSelectionReleaseAdapter legacyReleaseAdapter)
        : this(new DapperPackingTaskQueryDataSource(
            connectionFactory, packingStockMutationService, legacyReleaseAdapter),
            configuration, warehouseAccessService)
    {
    }

    internal PackingTaskQueryService(
        IPackingTaskQueryDataSource dataSource,
        IConfiguration configuration,
        IWarehouseAccessService? warehouseAccessService = null)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _warehouseAccessService = warehouseAccessService;
    }

    /// <summary>Gets a page of packing tasks.</summary>
    public async Task<PackingTaskQueryResult> PageAsync(
        PageSearch pageSearch,
        CurrentUser currentUser)
    {
        if (!_configuration.GetValue("Features:PackingTaskFirstStep", false))
            return Failure("装箱任务功能未启用");

        var warehouseText = FindSearchText(pageSearch, "warehouse_id");
        long? warehouseId = long.TryParse(warehouseText, out var parsedWarehouseId)
                            && parsedWarehouseId > 0
            ? parsedWarehouseId
            : null;
        if (_warehouseAccessService != null)
        {
            if (warehouseId == null)
            {
                warehouseId = (await _warehouseAccessService.GetAllowedAsync(currentUser))
                    .default_warehouse_id;
                if (warehouseId == null)
                    return new PackingTaskQueryResult(true, string.Empty, [], 0);
            }
            else
            {
                await _warehouseAccessService.EnsureAllowedAsync(warehouseId.Value, currentUser);
            }
        }

        var pageIndex = Math.Max(pageSearch.pageIndex, 1);
        var pageSize = Math.Clamp(pageSearch.pageSize, 1, 200);
        var groupId = long.TryParse(FindSearchText(pageSearch, "group_id"), out var parsedGroupId)
            ? parsedGroupId
            : 0;
        var memberId = long.TryParse(FindSearchText(pageSearch, "member_id"), out var parsedMemberId)
            ? parsedMemberId
            : 0;
        var page = await _dataSource.LoadPageAsync(new PackingTaskPageRequest(
            FindSearchText(pageSearch, "keyword"), warehouseId, groupId, memberId,
            (pageIndex - 1) * pageSize, pageSize));
        var itemsByTask = page.Items.GroupBy(item => item.sellfox_task_id)
            .ToDictionary(group => group.Key, group => group.ToList());
        var data = page.Tasks.Select(task => new PackingTaskQueryViewModel
        {
            id = task.id,
            sellfox_task_id = task.sellfox_task_id,
            packing_task_sn = task.packing_task_sn,
            warehouse_id = task.warehouse_id,
            warehouse_name = task.warehouse_name,
            complete_num = task.complete_num,
            task_num = task.task_num,
            create_name = task.create_name,
            source_create_time = task.source_create_time,
            item_count = task.item_count,
            shop_name = task.shop_name,
            marketplace_name = task.marketplace_name,
            item_list = (itemsByTask.GetValueOrDefault(task.sellfox_task_id) ?? [])
                .Select(item => BuildItemViewModel(item, page.AvailabilityByItemId))
                .ToList()
        }).ToList();
        return new PackingTaskQueryResult(true, string.Empty, data, page.Totals);
    }

    /// <summary>Gets task-creator stock from the task's ERP warehouse.</summary>
    public async Task<(List<SelectableStockViewModel> data, int totals)> SelectableStockPageAsync(
        PackingTaskStockPageRequest request,
        CurrentUser currentUser)
    {
        var loaded = await _dataSource.LoadSelectableStockAsync(request, currentUser);
        if (loaded == null) return ([], 0);
        foreach (var row in loaded.Rows)
        {
            row.warehouse_id = loaded.WarehouseId;
            row.warehouse_name = loaded.WarehouseName;
        }
        var ordered = loaded.Rows.OrderByDescending(row => row.selected)
            .ThenByDescending(row => row.matched)
            .ThenBy(row => row.erp_stock_id)
            .ToList();
        var pageIndex = Math.Max(request.page_index, 1);
        var pageSize = Math.Clamp(request.page_size, 1, 200);
        return (ordered.Skip((pageIndex - 1) * pageSize).Take(pageSize).ToList(), ordered.Count);
    }

    /// <summary>Selects or changes stock for a packing task item.</summary>
    public async Task<(bool flag, string message)> SelectStockAsync(
        PackingTaskStockSelectRequest request,
        CurrentUser currentUser)
    {
        if (request.variant <= 0) return (false, "变体数量必须大于0");
        var result = await _dataSource.SaveSelectionAsync(request, currentUser);
        return (result.IsSuccess, result.Message);
    }

    /// <summary>Cancels a packing-task stock selection.</summary>
    public async Task<(bool flag, string message)> DeleteStockSelectionAsync(
        PackingTaskStockSelectRequest request,
        CurrentUser currentUser)
    {
        var result = await _dataSource.DeleteSelectionAsync(request, currentUser);
        return (result.IsSuccess, result.Message);
    }

    private static PackingTaskQueryItemViewModel BuildItemViewModel(
        ErpPackingTaskItemEntity item,
        IReadOnlyDictionary<long, PackingTaskStockAvailability> availabilityByItemId)
    {
        var availability = availabilityByItemId.GetValueOrDefault(item.id);
        return new PackingTaskQueryItemViewModel
        {
            id = item.id,
            sellfox_item_id = item.sellfox_item_id,
            commodity_id = item.commodity_id,
            commodity_sku = item.commodity_sku,
            commodity_name = item.commodity_name,
            main_image = item.main_image,
            fn_sku = item.fn_sku,
            sku = item.sku,
            msku = item.msku,
            task_num = item.task_num,
            quantity_shipped = item.quantity_shipped,
            stock_available = item.stock_available,
            stock_sku_code = availability?.SkuCode,
            stock_qty = availability?.StockQty,
            stock_available_qty = availability == null
                ? null
                : availability.AvailableQty ?? availability.StockQty,
            locked_qty = availability?.LockedQty
        };
    }

    private static PackingTaskQueryResult Failure(string message) => new(false, message, [], 0);

    private static string FindSearchText(PageSearch pageSearch, string name) =>
        pageSearch.searchObjects.FirstOrDefault(search =>
            string.Equals(search.Name, name, StringComparison.OrdinalIgnoreCase))?.Text?.Trim()
        ?? string.Empty;
}
