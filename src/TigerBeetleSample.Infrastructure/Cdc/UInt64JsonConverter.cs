using System.Text.Json;
using System.Text.Json.Serialization;

namespace TigerBeetleSample.Infrastructure.Cdc;

/// <summary>
/// Custom JSON converter for <see cref="ulong"/>.
/// TigerBeetle encodes <c>u64</c> values (such as timestamps) as JSON strings to preserve
/// precision, but small values may arrive as JSON numbers. Both forms are accepted.
/// </summary>
public sealed class UInt64JsonConverter : JsonConverter<ulong>
{
    public override ulong Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var raw = reader.GetString()!;
            if (!ulong.TryParse(raw, out var parsed))
                throw new JsonException($"TigerBeetle CDC: cannot parse '{raw}' as ulong.");
            return parsed;
        }

        if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetUInt64(out var u64))
                return u64;
        }

        throw new JsonException($"TigerBeetle CDC: cannot read ulong from JSON token type {reader.TokenType}.");
    }

    public override void Write(Utf8JsonWriter writer, ulong value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
