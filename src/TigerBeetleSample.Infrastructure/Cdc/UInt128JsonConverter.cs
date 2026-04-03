using System.Text.Json;
using System.Text.Json.Serialization;

namespace TigerBeetleSample.Infrastructure.Cdc;

/// <summary>
/// Custom JSON converter for <see cref="System.UInt128"/>.
/// TigerBeetle encodes <c>u128</c> as JSON strings for large values.
/// Small values that fit in u64 may appear as JSON numbers (as in the docs examples).
/// </summary>
public sealed class UInt128JsonConverter : JsonConverter<System.UInt128>
{
    public override System.UInt128 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return System.UInt128.Parse(reader.GetString()!);

        if (reader.TokenType == JsonTokenType.Number)
        {
            if (reader.TryGetUInt64(out var u64))
                return (System.UInt128)u64;
        }

        throw new JsonException($"Cannot read UInt128 from JSON token type {reader.TokenType}.");
    }

    public override void Write(Utf8JsonWriter writer, System.UInt128 value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.ToString());
}
