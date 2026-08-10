using System.ComponentModel.DataAnnotations;

namespace ModernWMS.WMS.Entities.ViewModels;

public class DispatchWeighingShipmentViewModel
{
    public int id { get; set; }
    public string dispatch_no { get; set; } = string.Empty;
    public byte dispatch_status { get; set; }
    public long fba_shipment_id { get; set; }
    public string fba_no { get; set; } = string.Empty;
    public string main_image { get; set; } = string.Empty;
    public string commodity_name { get; set; } = string.Empty;
    public string fba_sku { get; set; } = string.Empty;
    public string shop_name { get; set; } = string.Empty;
    public string dept_name { get; set; } = string.Empty;
    public string order_user_name { get; set; } = string.Empty;
    public int shipment_total_qty { get; set; }
    public int variant_qty { get; set; }
    public int box_count { get; set; }
    public int weighed_box_count { get; set; }
    public int dimension_started_box_count { get; set; }
    public int dimension_measured_box_count { get; set; }
    public decimal weighing_weight { get; set; }
    public bool is_todo => box_count == 0 || weighed_box_count < box_count || dimension_measured_box_count < box_count;
}

public class DispatchWeighingBoxViewModel
{
    public long erp_box_id { get; set; }
    public string box_no { get; set; } = string.Empty;
    public string tracking_id { get; set; } = string.Empty;
    public int box_index { get; set; }
    public decimal weighing_weight { get; set; }
    public decimal weighing_length { get; set; }
    public decimal weighing_width { get; set; }
    public decimal weighing_height { get; set; }
    public decimal weighing_volume { get; set; }
    public bool is_weighed { get; set; }
}

public class SaveDispatchWeighingBoxViewModel
{
    [Required]
    public string dispatch_no { get; set; } = string.Empty;
    public long fba_shipment_id { get; set; }
    public long erp_box_id { get; set; }
    [Range(typeof(decimal), "0.01", "999999999")]
    public decimal weighing_weight { get; set; }
    [Range(typeof(decimal), "0.01", "99999")]
    public decimal weighing_length { get; set; }
    [Range(typeof(decimal), "0.01", "99999")]
    public decimal weighing_width { get; set; }
    [Range(typeof(decimal), "0.01", "99999")]
    public decimal weighing_height { get; set; }
}
