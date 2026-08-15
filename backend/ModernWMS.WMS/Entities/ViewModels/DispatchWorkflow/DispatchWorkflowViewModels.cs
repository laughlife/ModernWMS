namespace ModernWMS.WMS.Entities.ViewModels.DispatchWorkflow;

public sealed class CreateDispatchOrderRequest
{
    public long warehouse_id { get; set; }
    public List<long> source_task_ids { get; set; } = [];
    public string idempotency_key { get; set; } = string.Empty;
}

public sealed class CompletePickingRequest
{
    public string request_id { get; set; } = string.Empty;
    public long row_version { get; set; }
}

public sealed class CompletePickingResult
{
    public int order_id { get; set; }
    public string request_id { get; set; } = string.Empty;
    public string status { get; set; } = string.Empty;
    public long row_version { get; set; }
}

public sealed class WeighingOrderCommandRequest
{
    public string request_id { get; set; } = string.Empty;
    public long row_version { get; set; }
}

public sealed class WeighingCommandResult
{
    public int order_id { get; set; }
    public string request_id { get; set; } = string.Empty;
    public string status { get; set; } = string.Empty;
    public long row_version { get; set; }
}

public sealed record SaveWeighingBoxRequest
{
    public string request_id { get; set; } = string.Empty;
    public long row_version { get; set; }
    public long box_row_version { get; set; }
    public decimal weight { get; set; }
    public decimal length { get; set; }
    public decimal width { get; set; }
    public decimal height { get; set; }
}

public sealed class CopyWeighingBoxRequest
{
    public string request_id { get; set; } = string.Empty;
    public long row_version { get; set; }
    public int source_box_id { get; set; }
    public long target_box_row_version { get; set; }
}

public sealed class WeighingBoxViewModel
{
    public int id { get; set; }
    public int packing_task_id { get; set; }
    public string source_box_identity { get; set; } = string.Empty;
    public int box_sequence { get; set; }
    public decimal? weight { get; set; }
    public decimal? length { get; set; }
    public decimal? width { get; set; }
    public decimal? height { get; set; }
    public string measurement_status { get; set; } = string.Empty;
    public int? copied_from_box_id { get; set; }
    public long row_version { get; set; }
}

public sealed class SourceDecisionRequest
{
    public string decision { get; set; } = string.Empty;
    public string source_version { get; set; } = string.Empty;
    public string reason { get; set; } = string.Empty;
    public string request_id { get; set; } = string.Empty;
    public long row_version { get; set; }
}

public sealed class PostPickSourceGuardResult
{
    public bool source_change_pending { get; set; }
    public string error_code { get; set; } = string.Empty;
    public string source_version { get; set; } = string.Empty;
    public long row_version { get; set; }
}

public sealed class SourceDecisionResult
{
    public int order_id { get; set; }
    public string request_id { get; set; } = string.Empty;
    public string decision { get; set; } = string.Empty;
    public string source_version { get; set; } = string.Empty;
    public string status { get; set; } = string.Empty;
    public bool source_change_pending { get; set; }
    public long row_version { get; set; }
}

public sealed class DispatchOrderPageRequest
{
    public string status { get; set; } = string.Empty;
    public long warehouse_id { get; set; }
    public string keyword { get; set; } = string.Empty;
    public int pageIndex { get; set; } = 1;
    public int pageSize { get; set; } = 20;
}

public class DispatchOrderSummaryViewModel
{
    public int id { get; set; }
    public string dispatch_no { get; set; } = string.Empty;
    public long warehouse_id { get; set; }
    public string status { get; set; } = string.Empty;
    public List<string> packing_task_nos { get; set; } = [];
    public string creator { get; set; } = string.Empty;
    public DateTime create_time { get; set; }
    public DateTime last_update_time { get; set; }
    public bool source_change_pending { get; set; }
    public long row_version { get; set; }
}

public sealed class DispatchOrderDetailViewModel : DispatchOrderSummaryViewModel
{
    public string source_version { get; set; } = string.Empty;
    public List<DispatchPackingTaskViewModel> packing_tasks { get; set; } = [];
}

public sealed class DispatchPackingTaskViewModel
{
    public int id { get; set; }
    public long source_task_id { get; set; }
    public string source_task_no { get; set; } = string.Empty;
    public string status { get; set; } = string.Empty;
    public string source_version { get; set; } = string.Empty;
    public int expected_box_count { get; set; }
    public int measured_box_count { get; set; }
    public List<DispatchPackingTaskItemViewModel> items { get; set; } = [];
}

public sealed class DispatchPackingTaskItemViewModel
{
    public int id { get; set; }
    public long source_item_id { get; set; }
    public long? source_commodity_id { get; set; }
    public int? wms_sku_id { get; set; }
    public string commodity_sku { get; set; } = string.Empty;
    public string commodity_name { get; set; } = string.Empty;
    public string fn_sku { get; set; } = string.Empty;
    public string msku { get; set; } = string.Empty;
    public int? required_qty { get; set; }
    public int? source_stock_available { get; set; }
}

public sealed record DispatchOrderPageResult(
    List<DispatchOrderSummaryViewModel> Data,
    int Totals);

public sealed record DispatchOrderStatusCounts(
    IReadOnlyDictionary<string, int> Counts);
