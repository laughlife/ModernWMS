using System.Reflection;
using System.Reflection.Emit;
using ModernWMS.Core.JWT;
using ModernWMS.WMS.Services;

namespace ModernWMS.Tests.Architecture;

public sealed class NoTenantDependencyTests
{
    [Fact]
    public void Production_assemblies_expose_no_tenant_contract_or_runtime_literal()
    {
        var assemblies = new[]
        {
            typeof(CurrentUser).Assembly,
            typeof(PackingTaskQueryService).Assembly
        };
        var violations = assemblies
            .SelectMany(FindViolations)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(violations.Length == 0,
            "Production tenant dependencies remain:" + Environment.NewLine
            + string.Join(Environment.NewLine, violations));
    }

    private static IEnumerable<string> FindViolations(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            if (ContainsTenant(type.FullName)) yield return $"type: {type.FullName}";
            foreach (var member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic
                                                   | BindingFlags.Instance | BindingFlags.Static))
            {
                if (ContainsTenant(member.Name))
                    yield return $"member: {type.FullName}.{member.Name}";
            }
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                                                   | BindingFlags.Instance | BindingFlags.Static))
            {
                foreach (var parameter in method.GetParameters())
                {
                    if (ContainsTenant(parameter.Name))
                        yield return $"parameter: {type.FullName}.{method.Name}({parameter.Name})";
                }
                foreach (var literal in ReadStringLiterals(method))
                {
                    if (ContainsTenant(literal))
                        yield return $"literal: {type.FullName}.{method.Name}: {literal}";
                }
            }
        }
    }

    private static bool ContainsTenant(string? value) =>
        value?.Contains("tenant", StringComparison.OrdinalIgnoreCase) == true;

    private static IEnumerable<string> ReadStringLiterals(MethodInfo method)
    {
        var body = method.GetMethodBody();
        var il = body?.GetILAsByteArray();
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
