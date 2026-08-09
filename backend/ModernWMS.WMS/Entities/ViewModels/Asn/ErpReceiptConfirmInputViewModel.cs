namespace ModernWMS.WMS.Entities.ViewModels;

/// <summary>
/// Confirms receipt of one ERP logistics shipment.
/// The inbound quantity is intentionally omitted and is calculated by the service.
/// </summary>
public class ErpReceiptConfirmInputViewModel
{
    public long shipment_id { get; set; }

    public int source_version { get; set; }

    public List<ErpReceiptConfirmItemInputViewModel> items { get; set; } = [];

    public string receipt_freight_payment_status { get; set; } = string.Empty;

    public decimal? receipt_freight_amount { get; set; }

    public List<OssFileUploadViewModel> receipt_freight_files { get; set; } = [];

    public List<OssFileUploadViewModel> receipt_files { get; set; } = [];

    public string loss_reason { get; set; } = string.Empty;

    public List<OssFileUploadViewModel> loss_files { get; set; } = [];

    public string receipt_remark { get; set; } = string.Empty;
}

/// <summary>
/// One product line confirmed by the warehouse operator.
/// The inbound quantity is intentionally omitted and is calculated by the service.
/// </summary>
public class ErpReceiptConfirmItemInputViewModel
{
    public string source_item_key { get; set; } = string.Empty;

    public long? commodity_id { get; set; }

    public string commodity_sku { get; set; } = string.Empty;

    public long shipment_qty { get; set; }

    public long actual_receipt_qty { get; set; }

    public long loss_qty { get; set; }
}
