using System.Data;
using System.Reflection;
using Dapper;
using ModernWMS.WMS.Services;

namespace ModernWMS.Tests.Asn;

public sealed class HistoricalReceiptAllocationMaterializationTests
{
    [Fact]
    public void Dapper_materializes_history_allocation_with_mysql_integer_and_sum_decimal_values()
    {
        var rowType = typeof(ErpPendingReceiptService).GetNestedType(
            "HistoricalReceiptAllocationRow",
            BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("HistoricalReceiptAllocationRow type was not found");

        var table = new DataTable();
        table.Columns.Add("stock_record_id", typeof(long));
        table.Columns.Add("warehouse_area_id", typeof(int));
        table.Columns.Add("warehouse_area_name", typeof(string));
        table.Columns.Add("goods_location_id", typeof(int));
        table.Columns.Add("goods_location_name", typeof(string));
        table.Columns.Add("goods_owner_id", typeof(int));
        table.Columns.Add("goods_owner_name", typeof(string));
        table.Columns.Add("qty", typeof(decimal));
        table.Columns.Add("location_state", typeof(string));
        table.Rows.Add(1456L, 2, "深圳库区", 3, "A-01-01", 8, "测试货主", 500m, "ACTIVE");

        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());
        var parser = SqlMapper.GetRowParser(reader, rowType);

        var row = parser(reader);

        Assert.Equal(1456L, rowType.GetProperty("stock_record_id")?.GetValue(row));
        Assert.Equal(2, rowType.GetProperty("warehouse_area_id")?.GetValue(row));
        Assert.Equal(3, rowType.GetProperty("goods_location_id")?.GetValue(row));
        Assert.Equal(500L, rowType.GetProperty("qty")?.GetValue(row));
    }
}
