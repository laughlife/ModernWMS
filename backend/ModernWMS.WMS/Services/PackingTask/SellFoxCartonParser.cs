using System.Text;
using System.Text.Json;
using ModernWMS.WMS.Entities.ViewModels.PackingTask;

namespace ModernWMS.WMS.Services.PackingTask;

/// <summary>
/// Strict parser for SellFox physical boxes. Array position is display order only;
/// it is deliberately never accepted as a box identity.
/// </summary>
public static class SellFoxCartonParser
{
    private static readonly string[] IdentityKeys =
        ["boxId", "box_id", "cartonId", "carton_id", "id"];

    /// <summary>Parses SellFox carton JSON into normalized carton records.</summary>
    public static SellFoxCartonParseResult Parse(string? cartonsJson, bool allowEmpty = false)
    {
        if (string.IsNullOrWhiteSpace(cartonsJson))
        {
            return Unsupported("cartons_json 为空，无法验证稳定箱ID");
        }

        try
        {
            using var document = JsonDocument.Parse(cartonsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return Unsupported("cartons_json 必须是箱数组，无法验证稳定箱ID");
            }

            if (document.RootElement.GetArrayLength() == 0)
            {
                if (allowEmpty)
                {
                    return new SellFoxCartonParseResult(true, string.Empty, []);
                }

                return Unsupported("cartons_json 未包含物理箱，无法验证稳定箱ID");
            }

            var identities = new HashSet<string>(StringComparer.Ordinal);
            var boxes = new List<SellFoxSourceBox>(document.RootElement.GetArrayLength());
            var sequence = 0;
            foreach (var element in document.RootElement.EnumerateArray())
            {
                sequence++;
                if (element.ValueKind != JsonValueKind.Object)
                {
                    return Unsupported($"第 {sequence} 个箱不是对象，无法验证稳定箱ID");
                }

                var identity = ReadIdentity(element);
                if (string.IsNullOrWhiteSpace(identity))
                {
                    return Unsupported($"第 {sequence} 个箱缺少稳定箱ID");
                }

                identity = identity.Trim();
                if (!identities.Add(identity))
                {
                    return Unsupported($"稳定箱ID重复：{identity}");
                }

                boxes.Add(new SellFoxSourceBox(identity, sequence, Canonicalize(element)));
            }

            return new SellFoxCartonParseResult(true, string.Empty, boxes);
        }
        catch (JsonException exception)
        {
            return Unsupported($"cartons_json 不是有效 JSON，无法验证稳定箱ID：{exception.Message}");
        }
    }

    internal static string Canonicalize(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonical(writer, element);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string? ReadIdentity(JsonElement element)
    {
        foreach (var key in IdentityKeys)
        {
            if (!element.TryGetProperty(key, out var value))
            {
                continue;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null
            };
        }

        return null;
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }
                writer.WriteEndArray();
                break;
            default:
                element.WriteTo(writer);
                break;
        }
    }

    private static SellFoxCartonParseResult Unsupported(string error) =>
        new(false, error, []);
}
