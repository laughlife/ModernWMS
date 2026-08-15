namespace ModernWMS.WMS.Entities.ViewModels.PackingTask;

/// <summary>
/// Whether the shared SellFox packing-task schema can safely support the WMS workflow.
/// </summary>
public sealed record PackingTaskSourceCapability(bool IsSupported, string Error);

/// <summary>
/// One SellFox item kept inside its source task boundary.
/// </summary>
public sealed record PackingTaskSourceItem(
    long SourceItemId,
    long? CommodityId,
    string CommoditySku,
    string CommodityName,
    string MainImage,
    string FnSku,
    string Sku,
    string Msku,
    int Quantity,
    string SourceSnapshot);

/// <summary>
/// A physical box declared by SellFox. SourceSnapshot is read-only source evidence;
/// it is never a WMS measurement.
/// </summary>
public sealed record SellFoxSourceBox(
    string SourceBoxIdentity,
    int Sequence,
    string SourceSnapshot);

/// <summary>
/// Immutable read model used to reconcile one SellFox task into WMS-owned facts.
/// </summary>
public sealed record PackingTaskSourceSnapshot(
    long SourceTaskId,
    string TaskNo,
    long WarehouseId,
    string WarehouseName,
    string SourceVersion,
    bool IsCancelled,
    IReadOnlyList<PackingTaskSourceItem> Items,
    IReadOnlyList<SellFoxSourceBox> Boxes,
    string CartonsJson);

/// <summary>
/// Strict parsing result. Unsupported input never exposes a partial box collection.
/// </summary>
public sealed record SellFoxCartonParseResult(
    bool IsSupported,
    string Error,
    IReadOnlyList<SellFoxSourceBox> Boxes);
