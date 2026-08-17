using ModernWMS.Core.Database;
using ModernWMS.Core.DynamicSearch;

namespace ModernWMS.Tests.Database;

public class DapperSearchBuilderTests
{
    private static readonly IReadOnlyDictionary<string, string> AllowedColumns =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["warehouse_id"] = "o.warehouse_id",
            ["dispatch_no"] = "o.dispatch_no"
        };

    [Fact]
    public void Build_uses_only_allowlisted_columns_and_parameterized_values()
    {
        var filters = new[]
        {
            new SearchObject
            {
                Name = "warehouse_id",
                Operator = Operators.Equal,
                Text = "320118"
            },
            new SearchObject
            {
                Name = "dispatch_no",
                Operator = Operators.Contains,
                Text = "WMS-001"
            }
        };

        var result = DapperSearchBuilder.Build(filters, AllowedColumns);

        Assert.Equal(
            "o.warehouse_id = @filter0 AND o.dispatch_no LIKE @filter1 ESCAPE '!'",
            result.Sql);
        Assert.Equal("320118", result.Parameters.Get<string>("filter0"));
        Assert.Equal("%WMS-001%", result.Parameters.Get<string>("filter1"));
    }

    [Fact]
    public void Build_rejects_a_field_outside_the_endpoint_allowlist()
    {
        var filters = new[]
        {
            new SearchObject
            {
                Name = "dispatch_no; DROP TABLE wms_stock",
                Operator = Operators.Equal,
                Text = "x"
            }
        };

        var exception = Assert.Throws<ArgumentException>(
            () => DapperSearchBuilder.Build(filters, AllowedColumns));

        Assert.Contains("dispatch_no; DROP TABLE wms_stock", exception.Message);
    }

    [Fact]
    public void Contains_escapes_mysql_like_wildcards_and_the_escape_character()
    {
        var filters = new[]
        {
            new SearchObject
            {
                Name = "dispatch_no",
                Operator = Operators.Contains,
                Text = @"A%_!\B"
            }
        };

        var result = DapperSearchBuilder.Build(filters, AllowedColumns);

        Assert.Equal(@"%A!%!_!!\B%", result.Parameters.Get<string>("filter0"));
    }
}
