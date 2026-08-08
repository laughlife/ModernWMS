namespace ModernWMS.WMS.Entities.ViewModels;

/// <summary>
/// ERP domestic warehouse option.
/// </summary>
public class ErpWarehouseOptionViewModel
{
    /// <summary>
    /// ERP warehouse id.
    /// </summary>
    public long id { get; set; }

    /// <summary>
    /// ERP warehouse name.
    /// </summary>
    public string name { get; set; } = string.Empty;
}
