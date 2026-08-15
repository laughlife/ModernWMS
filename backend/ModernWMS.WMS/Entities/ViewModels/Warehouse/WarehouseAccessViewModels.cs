namespace ModernWMS.WMS.Entities.ViewModels;

/// <summary>
/// Warehouses the current user may access in packing-task dispatch workflows.
/// </summary>
public class WarehouseAccessViewModel
{
    public List<ErpWarehouseOptionViewModel> warehouses { get; set; } = [];

    public long? default_warehouse_id { get; set; }
}
