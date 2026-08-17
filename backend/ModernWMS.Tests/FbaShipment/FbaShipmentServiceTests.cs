using ModernWMS.Core.DBContext.Entities;
using ModernWMS.WMS.Entities.ViewModels;
using ModernWMS.WMS.Services;
using System.Reflection;

namespace ModernWMS.Tests.FbaShipment;

public class FbaShipmentServiceTests
{
    [Fact]
    public void BuildViewModel_includes_the_erp_shipment_creator()
    {
        var move = new ErpStockMoveEntity
        {
            id = 1,
            no = "MOVE-001",
            from_warehouse_id = 320118,
            transfer_type = "OVERSEA_FBA_SHIPMENT",
            status = "WAIT_SHIPMENT",
            shipment_status = "WAIT_SHIPMENT",
            creator = "ERP创建人",
            create_time = new DateTime(2026, 8, 11, 10, 0, 0),
            update_time = new DateTime(2026, 8, 11, 10, 0, 0)
        };
        var method = typeof(FbaShipmentService).GetMethod(
            "BuildViewModel",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        var snapshotType = typeof(FbaShipmentService).GetNestedType(
            "PreparedItemSnapshot",
            BindingFlags.NonPublic)!;
        var emptySnapshots = Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(long), snapshotType))!;
        var result = (FbaShipmentViewModel)method.Invoke(null,
        [
            move,
            new List<ErpStockMoveItemEntity>(),
            emptySnapshots,
            new Dictionary<long, ErpBusinessStockEntity>(),
            new Dictionary<long, ErpFbaShipmentEntity>()
        ])!;

        Assert.Equal("ERP创建人", result.creator);
    }
}
