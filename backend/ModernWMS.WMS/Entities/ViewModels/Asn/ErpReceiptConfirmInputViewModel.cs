namespace ModernWMS.WMS.Entities.ViewModels;

/// <summary>
/// Confirms receipt of one ERP logistics shipment.
/// The inbound quantity is intentionally omitted and is calculated by the service.
/// </summary>
public class ErpReceiptConfirmInputViewModel
{
    /// <summary>
    /// 获取或设置 shipment_id。
    /// </summary>
    public long shipment_id { get; set; }

    /// <summary>
    /// 获取或设置 source_version。
    /// </summary>
    public int source_version { get; set; }

    /// <summary>
    /// 获取或设置 items。
    /// </summary>
    public List<ErpReceiptConfirmItemInputViewModel> items { get; set; } = [];

    /// <summary>
    /// 获取或设置 receipt_freight_payment_status。
    /// </summary>
    public string receipt_freight_payment_status { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 receipt_freight_amount。
    /// </summary>
    public decimal? receipt_freight_amount { get; set; }

    /// <summary>
    /// 获取或设置 receipt_freight_files。
    /// </summary>
    public List<OssFileUploadViewModel> receipt_freight_files { get; set; } = [];

    /// <summary>
    /// 获取或设置 receipt_files。
    /// </summary>
    public List<OssFileUploadViewModel> receipt_files { get; set; } = [];

    /// <summary>
    /// 获取或设置 loss_reason。
    /// </summary>
    public string loss_reason { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 loss_files。
    /// </summary>
    public List<OssFileUploadViewModel> loss_files { get; set; } = [];

    /// <summary>
    /// 获取或设置 receipt_remark。
    /// </summary>
    public string receipt_remark { get; set; } = string.Empty;
}

/// <summary>
/// One product line confirmed by the warehouse operator.
/// The inbound quantity is intentionally omitted and is calculated by the service.
/// </summary>
public class ErpReceiptConfirmItemInputViewModel
{
    /// <summary>
    /// 获取或设置 source_item_key。
    /// </summary>
    public string source_item_key { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 commodity_id。
    /// </summary>
    public long? commodity_id { get; set; }

    /// <summary>
    /// 获取或设置 commodity_sku。
    /// </summary>
    public string commodity_sku { get; set; } = string.Empty;

    /// <summary>
    /// 获取或设置 shipment_qty。
    /// </summary>
    public long shipment_qty { get; set; }

    /// <summary>
    /// 获取或设置 actual_receipt_qty。
    /// </summary>
    public long actual_receipt_qty { get; set; }

    /// <summary>
    /// 获取或设置 loss_qty。
    /// </summary>
    public long loss_qty { get; set; }

    /// <summary>
    /// 获取或设置 allocations。
    /// </summary>
    public List<ErpReceiptAllocationInputViewModel> allocations { get; set; } = [];
}

/// <summary>
/// One warehouse-area and goods-owner allocation for a received product.
/// </summary>
public class ErpReceiptAllocationInputViewModel
{
    /// <summary>
    /// 获取或设置 warehouse_area_id。
    /// </summary>
    public int? warehouse_area_id { get; set; }

    /// <summary>
    /// Zero means the purchaser-derived default goods owner.
    /// </summary>
    public int goods_owner_id { get; set; }

    /// <summary>
    /// 获取或设置 qty。
    /// </summary>
    public long qty { get; set; }
}
