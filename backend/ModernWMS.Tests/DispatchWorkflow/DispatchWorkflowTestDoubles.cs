using System.Reflection;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.IServices;

namespace ModernWMS.Tests.DispatchWorkflow;

internal sealed class RecordingWarehouseAccess
{
    public List<long> CheckedWarehouseIds { get; } = [];
    public long? DefaultWarehouseId { get; set; } = 320118;
    public IWarehouseAccessService Contract { get; }

    public RecordingWarehouseAccess()
    {
        Contract = DispatchProxy.Create<IWarehouseAccessService, WarehouseAccessProxy>();
        ((WarehouseAccessProxy)(object)Contract).Owner = this;
    }

    internal Task<ModernWMS.WMS.Entities.ViewModels.WarehouseAccessViewModel> GetAllowedAsync(CurrentUser currentUser) =>
        Task.FromResult(new ModernWMS.WMS.Entities.ViewModels.WarehouseAccessViewModel
        {
            default_warehouse_id = DefaultWarehouseId,
            warehouses = DefaultWarehouseId is long id
                ? [new ModernWMS.WMS.Entities.ViewModels.ErpWarehouseOptionViewModel { id = id, name = $"仓库-{id}" }]
                : []
        });

    internal Task EnsureAllowedAsync(long warehouseId, CurrentUser currentUser)
    {
        CheckedWarehouseIds.Add(warehouseId);
        return Task.CompletedTask;
    }
}

public class WarehouseAccessProxy : DispatchProxy
{
    internal RecordingWarehouseAccess Owner { get; set; } = null!;

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
    {
        nameof(IWarehouseAccessService.GetAllowedAsync) => Owner.GetAllowedAsync((CurrentUser)args![0]!),
        nameof(IWarehouseAccessService.EnsureAllowedAsync) => Owner.EnsureAllowedAsync(
            (long)args![0]!, (CurrentUser)args[1]!),
        _ => throw new NotSupportedException(targetMethod?.Name)
    };
}
