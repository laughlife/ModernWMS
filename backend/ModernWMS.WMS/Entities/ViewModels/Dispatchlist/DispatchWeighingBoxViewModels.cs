using System.ComponentModel.DataAnnotations;

namespace ModernWMS.WMS.Entities.ViewModels;

/// <summary>
/// 表示 DispatchWeighingShipmentViewModel 类型。
/// </summary>
public class DispatchWeighingShipmentViewModel
{
    /// <summary>
    /// 获取或设置 id。
    /// </summary>
    public int id { get; set; }
    /// <summary>
    /// 获取或设置 dispatch_no。
    /// </summary>
    public string dispatch_no { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 dispatch_status。
    /// </summary>
    public byte dispatch_status { get; set; }
    /// <summary>
    /// 获取或设置 fba_shipment_id。
    /// </summary>
    public long fba_shipment_id { get; set; }
    /// <summary>
    /// 获取或设置 fba_no。
    /// </summary>
    public string fba_no { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 main_image。
    /// </summary>
    public string main_image { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 commodity_name。
    /// </summary>
    public string commodity_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 fba_sku。
    /// </summary>
    public string fba_sku { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 shop_name。
    /// </summary>
    public string shop_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 dept_name。
    /// </summary>
    public string dept_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 order_user_name。
    /// </summary>
    public string order_user_name { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 creator。
    /// </summary>
    public string creator { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 shipment_total_qty。
    /// </summary>
    public int shipment_total_qty { get; set; }
    /// <summary>
    /// 获取或设置 variant_qty。
    /// </summary>
    public int variant_qty { get; set; }
    /// <summary>
    /// 获取或设置 box_count。
    /// </summary>
    public int box_count { get; set; }
    /// <summary>
    /// 获取或设置 weighed_box_count。
    /// </summary>
    public int weighed_box_count { get; set; }
    /// <summary>
    /// 获取或设置 dimension_started_box_count。
    /// </summary>
    public int dimension_started_box_count { get; set; }
    /// <summary>
    /// 获取或设置 dimension_measured_box_count。
    /// </summary>
    public int dimension_measured_box_count { get; set; }
    /// <summary>
    /// 获取或设置 weighing_weight。
    /// </summary>
    public decimal weighing_weight { get; set; }
    /// <summary>
    /// 获取或设置 is_todo。
    /// </summary>
    public bool is_todo => box_count == 0 || weighed_box_count < box_count || dimension_measured_box_count < box_count;
    /// <summary>
    /// 获取或设置 can_complete_dispatch。
    /// </summary>
    public bool can_complete_dispatch { get; set; }
}

/// <summary>
/// 表示 DispatchWeighingBoxViewModel 类型。
/// </summary>
public class DispatchWeighingBoxViewModel
{
    /// <summary>
    /// 获取或设置 erp_box_id。
    /// </summary>
    public long erp_box_id { get; set; }
    /// <summary>
    /// 获取或设置 box_no。
    /// </summary>
    public string box_no { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 tracking_id。
    /// </summary>
    public string tracking_id { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 box_index。
    /// </summary>
    public int box_index { get; set; }
    /// <summary>
    /// 获取或设置 weighing_weight。
    /// </summary>
    public decimal weighing_weight { get; set; }
    /// <summary>
    /// 获取或设置 weighing_length。
    /// </summary>
    public decimal weighing_length { get; set; }
    /// <summary>
    /// 获取或设置 weighing_width。
    /// </summary>
    public decimal weighing_width { get; set; }
    /// <summary>
    /// 获取或设置 weighing_height。
    /// </summary>
    public decimal weighing_height { get; set; }
    /// <summary>
    /// 获取或设置 weighing_volume。
    /// </summary>
    public decimal weighing_volume { get; set; }
    /// <summary>
    /// 获取或设置 is_weighed。
    /// </summary>
    public bool is_weighed { get; set; }
}

/// <summary>
/// 表示 SaveDispatchWeighingBoxViewModel 类型。
/// </summary>
public class SaveDispatchWeighingBoxViewModel
{
    /// <summary>
    /// 获取或设置 dispatch_no。
    /// </summary>
    [Required]
    public string dispatch_no { get; set; } = string.Empty;
    /// <summary>
    /// 获取或设置 fba_shipment_id。
    /// </summary>
    public long fba_shipment_id { get; set; }
    /// <summary>
    /// 获取或设置 erp_box_id。
    /// </summary>
    public long erp_box_id { get; set; }
    /// <summary>
    /// 获取或设置 weighing_weight。
    /// </summary>
    [Range(typeof(decimal), "0.01", "999999999")]
    public decimal weighing_weight { get; set; }
    /// <summary>
    /// 获取或设置 weighing_length。
    /// </summary>
    [Range(typeof(decimal), "0.01", "99999")]
    public decimal weighing_length { get; set; }
    /// <summary>
    /// 获取或设置 weighing_width。
    /// </summary>
    [Range(typeof(decimal), "0.01", "99999")]
    public decimal weighing_width { get; set; }
    /// <summary>
    /// 获取或设置 weighing_height。
    /// </summary>
    [Range(typeof(decimal), "0.01", "99999")]
    public decimal weighing_height { get; set; }
}
