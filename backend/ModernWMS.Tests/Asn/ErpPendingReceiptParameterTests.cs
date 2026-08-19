using System.Collections;
using System.Reflection;
using Dapper;
using ModernWMS.WMS.Services;

namespace ModernWMS.Tests.Asn;

public class ErpPendingReceiptParameterTests
{
    [Fact]
    public void CreateParameters_converts_DbNull_to_a_database_null_parameter()
    {
        var method = typeof(ErpPendingReceiptService).GetMethod(
            "CreateParameters",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var input = new (string Name, object? Value)[]
        {
            ("@diffReason", DBNull.Value)
        };

        var parameters = Assert.IsType<DynamicParameters>(method.Invoke(null, [input]));

        var parameterStoreField = typeof(DynamicParameters).GetField(
            "parameters",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(parameterStoreField);
        var parameterStore = Assert.IsAssignableFrom<IDictionary>(parameterStoreField.GetValue(parameters));
        var parameterInfo = parameterStore["diffReason"];
        Assert.NotNull(parameterInfo);
        var valueProperty = parameterInfo.GetType().GetProperty(
            "Value",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(valueProperty);

        Assert.Null(valueProperty.GetValue(parameterInfo));
    }
}
