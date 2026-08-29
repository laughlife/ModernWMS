using System.Reflection;
using System.Reflection.Emit;
using ModernWMS.WMS.Services;

namespace ModernWMS.Tests.PackingTask;

public sealed class LegacyPackingSelectionReleaseAdapterTests
{
    [Fact]
    public void Adapter_only_settles_existing_location_decomposition_and_never_creates_one()
    {
        var adapterType = typeof(LegacyPackingSelectionReleaseAdapter);
        var sql = adapterType.Assembly.GetTypes()
            .Where(type => type == adapterType
                || type.FullName?.StartsWith(adapterType.FullName + "+", StringComparison.Ordinal) == true)
            .SelectMany(type => type.GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static))
            .SelectMany(ReadStringLiterals)
            .Where(value => value.Contains("wms_erp_stock_reservation_allocation",
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.NotEmpty(sql);
        Assert.DoesNotContain(sql, value => value.Contains(
            "INSERT INTO `wms_erp_stock_reservation_allocation`", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(sql, value => value.Contains(
            "UPDATE `wms_erp_stock_reservation_allocation`", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> ReadStringLiterals(MethodInfo method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il == null) yield break;
        for (var offset = 0; offset < il.Length;)
        {
            var value = il[offset++];
            var key = value == 0xfe ? (ushort)(0xfe00 | il[offset++]) : value;
            if (!OpCodesByValue.TryGetValue(key, out var opCode)) yield break;
            if (opCode == OpCodes.Ldstr)
                yield return method.Module.ResolveString(BitConverter.ToInt32(il, offset));
            offset += OperandSize(opCode.OperandType, il, offset);
        }
    }

    private static int OperandSize(OperandType operandType, byte[] il, int offset) => operandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI or OperandType.InlineBrTarget or OperandType.InlineField
            or OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineString
            or OperandType.InlineTok or OperandType.InlineType or OperandType.ShortInlineR => 4,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + BitConverter.ToInt32(il, offset) * 4,
        _ => throw new InvalidOperationException($"Unsupported IL operand type: {operandType}")
    };

    private static readonly IReadOnlyDictionary<ushort, OpCode> OpCodesByValue =
        typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.FieldType == typeof(OpCode))
            .Select(field => (OpCode)field.GetValue(null)!)
            .ToDictionary(opCode => unchecked((ushort)opCode.Value));
}
