using System.Data;
using System.Reflection;
using Dapper;
using ModernWMS.WMS.Services;

namespace ModernWMS.Tests.Asn;

public sealed class StockAllocationInvariantMaterializationTests
{
    [Fact]
    public void Dapper_materializes_mysql_sum_decimals_into_the_invariant_quantity_model()
    {
        var invariantType = typeof(ErpPendingReceiptService).GetNestedType(
            "StockAllocationInvariant",
            BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("StockAllocationInvariant type was not found");

        var table = new DataTable();
        table.Columns.Add("available_qty", typeof(long));
        table.Columns.Add("stock_occupied_qty", typeof(long));
        table.Columns.Add("total_qty", typeof(long));
        table.Columns.Add("allocated_qty", typeof(decimal));
        table.Columns.Add("occupied_qty", typeof(decimal));
        table.Rows.Add(500L, 0L, 500L, 500m, 0m);

        using var reader = table.CreateDataReader();
        Assert.True(reader.Read());
        var parser = SqlMapper.GetRowParser(reader, invariantType);

        var invariant = parser(reader);

        Assert.Equal(500L, invariantType.GetProperty("allocated_qty")?.GetValue(invariant));
        Assert.Equal(0L, invariantType.GetProperty("occupied_qty")?.GetValue(invariant));
    }
}
