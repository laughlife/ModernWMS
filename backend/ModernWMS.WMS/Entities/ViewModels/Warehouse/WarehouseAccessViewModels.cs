namespace ModernWMS.WMS.Entities.ViewModels;

/// <summary>
/// Warehouses the current user may access in packing-task dispatch workflows.
/// </summary>
public class WarehouseAccessViewModel
{
    /// <summary>
    /// 获取或设置 warehouses。
    /// </summary>
    public List<ErpWarehouseOptionViewModel> warehouses { get; set; } = [];

    /// <summary>
    /// 获取或设置 default_warehouse_id。
    /// </summary>
    public long? default_warehouse_id { get; set; }
}
