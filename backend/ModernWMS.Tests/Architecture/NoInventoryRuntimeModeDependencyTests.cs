using System.Reflection;
using System.Reflection.Emit;
using ModernWMS.WMS.Services;

namespace ModernWMS.Tests.Architecture;

public sealed class NoInventoryRuntimeModeDependencyTests
{
    private static readonly string[] ForbiddenTerms =
    [
        "LEGACY_READ",
        "CANONICAL_ERP",
        "maintenance_enabled",
        "wms_inventory_runtime_config",
        "库存维护窗口",
        "旧库存模式"
    ];

    [Fact]
    public void Production_assembly_has_one_inventory_path_without_runtime_mode_or_maintenance_gate()
    {
        var violations = typeof(PackingTaskQueryService).Assembly.GetTypes()
            .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                | BindingFlags.Instance | BindingFlags.Static)
                .SelectMany(method => ReadStringLiterals(method)
                    .Select(literal => (type, method, literal))))
            .Where(value => ForbiddenTerms.Any(term =>
                value.literal.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Select(value => $"{value.type.FullName}.{value.method.Name}: {value.literal}")
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(violations.Length == 0,
            "Inventory runtime-mode dependencies remain:" + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
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
            {
                var token = BitConverter.ToInt32(il, offset);
                yield return method.Module.ResolveString(token);
            }
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
